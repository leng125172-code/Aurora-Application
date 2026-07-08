using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using RuntimeWorkflowDefinition = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 工作流运行时应用服务实现。
/// </summary>
[Authorize]
[Route("/api/app/workflow")]
public class WorkflowRuntimeAppService : AuroraStruct3DAppService, IWorkflowRuntimeAppService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _registry;
    private readonly IWorkflowVariableBridge _bridge;
    private readonly IWorkflowRuntimeVariablePool _runtimeVariablePool;
    private readonly IOfflineVariableLibraryAppService _offlineVariableLibrary;
    private readonly WorkflowVariableCompileRequestFactory _compileRequestFactory;
    private readonly WorkflowExecutionKernel _kernel;
    private readonly IWorkflowDebugSessionStore _sessionStore;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IRepository<WorkflowProjectTaskConfig, Guid> _taskConfigRepository;
    private readonly IRepository<WorkflowProjectTask, Guid> _taskRepository;
    private readonly IRepository<WorkflowProjectDeployment, Guid> _deploymentRepository;
    private readonly IRepository<WorkflowProjectRun, Guid> _runRepository;
    private readonly IRepository<VariableDefinition, Guid> _variableDefinitionRepository;

    /// <summary>
    /// 初始化运行时服务。
    /// </summary>
    public WorkflowRuntimeAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry registry,
        IWorkflowVariableBridge bridge,
        IWorkflowRuntimeVariablePool runtimeVariablePool,
        IOfflineVariableLibraryAppService offlineVariableLibrary,
        WorkflowVariableCompileRequestFactory compileRequestFactory,
        WorkflowExecutionKernel kernel,
        IWorkflowDebugSessionStore sessionStore,
        IBackgroundJobClient backgroundJobClient,
        IRecurringJobManager recurringJobManager,
        IRepository<WorkflowProjectTaskConfig, Guid> taskConfigRepository,
        IRepository<WorkflowProjectTask, Guid> taskRepository,
        IRepository<WorkflowProjectDeployment, Guid> deploymentRepository,
        IRepository<WorkflowProjectRun, Guid> runRepository,
        IRepository<VariableDefinition, Guid> variableDefinitionRepository
    )
    {
        _repository = repository;
        _registry = registry;
        _bridge = bridge;
        _runtimeVariablePool = runtimeVariablePool;
        _offlineVariableLibrary = offlineVariableLibrary;
        _compileRequestFactory = compileRequestFactory;
        _kernel = kernel;
        _sessionStore = sessionStore;
        _backgroundJobClient = backgroundJobClient;
        _recurringJobManager = recurringJobManager;
        _taskConfigRepository = taskConfigRepository;
        _taskRepository = taskRepository;
        _deploymentRepository = deploymentRepository;
        _runRepository = runRepository;
        _variableDefinitionRepository = variableDefinitionRepository;
    }

    /// <inheritdoc/>
    [HttpGet("projects/{projectId:guid}/tasks")]
    public async Task<WorkflowProjectTaskBatchDto> GetProjectTasksAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        WorkflowProjectTaskConfig? taskConfig = await AsyncExecuter.FirstOrDefaultAsync(
            (await _taskConfigRepository.GetQueryableAsync()).Where(x => x.ProjectId == projectId)
        );

        List<WorkflowProjectTask> tasks = await AsyncExecuter.ToListAsync(
            (await _taskRepository.GetQueryableAsync()).Where(x => x.ProjectId == projectId)
        );

        if (tasks.Count == 0)
        {
            return new WorkflowProjectTaskBatchDto { ProjectId = projectId, Items = [] };
        }

        List<Guid> workflowIds = tasks.Select(x => x.WorkflowId).Distinct().ToList();
        Dictionary<Guid, string> workflowNameMap = (
            await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync()).Where(x =>
                    x.ProjectId == projectId && workflowIds.Contains(x.Id)
                )
            )
        ).ToDictionary(x => x.Id, x => x.Name);

        WorkflowProjectTaskType taskType =
            taskConfig?.TaskType ?? WorkflowProjectTaskType.Immediate;
        int? cycleIntervalSeconds =
            taskConfig?.TaskType == WorkflowProjectTaskType.Cyclic
                ? taskConfig.CycleIntervalSeconds
                : null;

        return new WorkflowProjectTaskBatchDto
        {
            ProjectId = projectId,
            TaskType = taskType,
            CycleIntervalSeconds = cycleIntervalSeconds,
            Items = tasks
                .Where(x => workflowNameMap.ContainsKey(x.WorkflowId))
                .OrderBy(x => x.OrderNo)
                .ThenBy(x => x.CreationTime)
                .Select(x => MapTaskDto(x, workflowNameMap[x.WorkflowId]))
                .ToList(),
        };
    }

    /// <inheritdoc/>
    [HttpPut("projects/tasks")]
    public async Task<WorkflowProjectTaskBatchDto> UpdateProjectTasksAsync(
        WorkflowProjectTaskBatchDto input
    )
    {
        if (input is null)
        {
            throw new UserFriendlyException("修改参数不能为空。");
        }

        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        if (input.Items.Count == 0)
        {
            throw new UserFriendlyException("至少需要一条任务配置。");
        }

        if (
            input.TaskType == WorkflowProjectTaskType.Cyclic
            && input.CycleIntervalSeconds is null or <= 0
        )
        {
            throw new UserFriendlyException("周期触发必须提供大于 0 的周期间隔（秒）。");
        }

        List<Guid> workflowIds = input.Items.Select(x => x.WorkflowId).Distinct().ToList();
        if (workflowIds.Any(x => x == Guid.Empty) || workflowIds.Count != input.Items.Count)
        {
            throw new UserFriendlyException("任务配置中的 WorkflowId 不能为空且不能重复。");
        }

        IQueryable<WorkflowDefinition> workflowQuery = await _repository.GetQueryableAsync();
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            workflowQuery.Where(x => x.ProjectId == input.ProjectId && workflowIds.Contains(x.Id))
        );
        if (workflows.Count != workflowIds.Count)
        {
            throw new UserFriendlyException("存在不属于当前项目的工作流，无法保存任务配置。");
        }

        int? cycleIntervalSeconds =
            input.TaskType == WorkflowProjectTaskType.Cyclic ? input.CycleIntervalSeconds : null;

        WorkflowProjectTaskConfig? taskConfig = await AsyncExecuter.FirstOrDefaultAsync(
            (await _taskConfigRepository.GetQueryableAsync()).Where(x =>
                x.ProjectId == input.ProjectId
            )
        );
        if (taskConfig is null)
        {
            taskConfig = WorkflowProjectTaskConfig.Create(
                GuidGenerator.Create(),
                input.ProjectId,
                input.TaskType,
                cycleIntervalSeconds
            );
            await _taskConfigRepository.InsertAsync(taskConfig, autoSave: true);
        }
        else
        {
            taskConfig.SetTrigger(input.TaskType, cycleIntervalSeconds);
            await _taskConfigRepository.UpdateAsync(taskConfig, autoSave: true);
        }

        Dictionary<Guid, WorkflowProjectTask> existingMap = (
            await AsyncExecuter.ToListAsync(
                (await _taskRepository.GetQueryableAsync()).Where(x =>
                    x.ProjectId == input.ProjectId
                )
            )
        ).ToDictionary(x => x.WorkflowId);

        foreach (WorkflowProjectTaskDto item in input.Items)
        {
            if (existingMap.TryGetValue(item.WorkflowId, out WorkflowProjectTask? task))
            {
                task.SetEnabled(item.IsEnabled);
                task.SetOrderNo(item.OrderNo);
                await _taskRepository.UpdateAsync(task, autoSave: true);
            }
            else
            {
                WorkflowProjectTask newTask = WorkflowProjectTask.Create(
                    GuidGenerator.Create(),
                    input.ProjectId,
                    item.WorkflowId,
                    item.IsEnabled,
                    item.OrderNo
                );
                await _taskRepository.InsertAsync(newTask, autoSave: true);
                existingMap[item.WorkflowId] = newTask;
            }
        }

        return await GetProjectTasksAsync(input.ProjectId);
    }

    /// <inheritdoc/>
    [HttpGet("projects/{projectId:guid}/deployments")]
    public async Task<List<WorkflowProjectDeploymentDto>> GetProjectDeploymentsAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        List<WorkflowProjectDeployment> deployments = await AsyncExecuter.ToListAsync(
            (await _deploymentRepository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId)
                .OrderByDescending(x => x.Revision)
        );

        return deployments.Select(MapDeploymentDto).ToList();
    }

    /// <inheritdoc/>
    [HttpPost("projects/{projectId:guid}/deployments")]
    public async Task<WorkflowProjectDeploymentDto> PublishProjectDeploymentAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        List<WorkflowProjectTask> tasks = await AsyncExecuter.ToListAsync(
            (await _taskRepository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId && x.IsEnabled)
                .OrderBy(x => x.OrderNo)
                .ThenBy(x => x.CreationTime)
        );

        if (tasks.Count == 0)
        {
            throw new UserFriendlyException("当前项目没有已启用的工作流任务，无法发布部署快照。");
        }

        int nextRevision =
            (
                await AsyncExecuter.FirstOrDefaultAsync(
                    (await _deploymentRepository.GetQueryableAsync())
                        .Where(x => x.ProjectId == projectId)
                        .OrderByDescending(x => x.Revision)
                        .Select(x => (int?)x.Revision)
                ) ?? 0
            ) + 1;

        List<Guid> workflowIds = tasks.Select(x => x.WorkflowId).Distinct().ToList();
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x =>
                x.ProjectId == projectId && workflowIds.Contains(x.Id)
            )
        );

        if (workflows.Count != workflowIds.Count)
        {
            throw new UserFriendlyException("存在已启用但已不存在的工作流，无法发布部署快照。");
        }

        Dictionary<Guid, WorkflowDefinition> workflowMap = workflows.ToDictionary(x => x.Id);
        List<WorkflowProjectDeploymentItem> items = tasks
            .Select(x => new WorkflowProjectDeploymentItem
            {
                WorkflowId = x.WorkflowId,
                WorkflowName = workflowMap[x.WorkflowId].Name,
                OrderNo = x.OrderNo,
                GraphHash = ComputeGraphHash(workflowMap[x.WorkflowId].GraphData),
            })
            .ToList();

        // 冻结完整工作流图（按发布顺序固化 GraphData）。
        List<WorkflowProjectFrozenGraph> frozenGraphs = items
            .Select(x => new WorkflowProjectFrozenGraph
            {
                WorkflowId = x.WorkflowId,
                GraphData = workflowMap[x.WorkflowId].GraphData,
            })
            .ToList();

        // 冻结项目变量定义默认值（与运行时变量池初始化一致）。
        List<VariableDefinition> variableDefinitions = await AsyncExecuter.ToListAsync(
            (await _variableDefinitionRepository.GetQueryableAsync()).Where(x =>
                x.ProjectId == projectId
            )
        );
        List<WorkflowProjectFrozenVariable> frozenVariables = variableDefinitions
            .Select(x => new WorkflowProjectFrozenVariable
            {
                OwnerWorkflowId = x.OwnerWorkflowId,
                Name = x.Name,
                TypeName = x.TypeName,
                DefaultValueJson = x.DefaultValueJson,
            })
            .ToList();

        string snapshotHash = ComputeSnapshotHash(items);
        WorkflowProjectDeployment deployment = WorkflowProjectDeployment.CreatePublished(
            GuidGenerator.Create(),
            projectId,
            nextRevision,
            items,
            frozenGraphs,
            frozenVariables,
            snapshotHash
        );
        await _deploymentRepository.InsertAsync(deployment, autoSave: true);

        return MapDeploymentDto(deployment);
    }

    /// <inheritdoc/>
    [HttpPost("deployments/{deploymentId:guid}/activate")]
    public async Task<WorkflowProjectDeploymentDto> ActivateProjectDeploymentAsync(
        Guid deploymentId
    )
    {
        if (deploymentId == Guid.Empty)
        {
            throw new UserFriendlyException("DeploymentId 不能为空。");
        }

        WorkflowProjectDeployment deployment = await _deploymentRepository.GetAsync(deploymentId);
        return await SwitchActiveDeploymentAsync(deployment, "激活", allowAlreadyActive: true);
    }

    /// <inheritdoc/>
    [HttpPost("deployments/{deploymentId:guid}/reactivate")]
    public async Task<WorkflowProjectDeploymentDto> ReactivateProjectDeploymentAsync(
        Guid deploymentId
    )
    {
        if (deploymentId == Guid.Empty)
        {
            throw new UserFriendlyException("DeploymentId 不能为空。");
        }

        WorkflowProjectDeployment deployment = await _deploymentRepository.GetAsync(deploymentId);
        return await SwitchActiveDeploymentAsync(deployment, "重新激活", allowAlreadyActive: true);
    }

    /// <inheritdoc/>
    [HttpPost("deployments/{deploymentId:guid}/rollback")]
    public async Task<WorkflowProjectDeploymentDto> RollbackProjectDeploymentAsync(
        Guid deploymentId
    )
    {
        if (deploymentId == Guid.Empty)
        {
            throw new UserFriendlyException("DeploymentId 不能为空。");
        }

        WorkflowProjectDeployment deployment = await _deploymentRepository.GetAsync(deploymentId);
        return await SwitchActiveDeploymentAsync(deployment, "回滚", allowAlreadyActive: false);
    }

    /// <inheritdoc/>
    [HttpPost("runs")]
    public async Task<WorkflowProjectRunEnqueueResultDto> EnqueueProjectRunAsync(
        WorkflowProjectRunEnqueueInput input
    )
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        if (!Enum.IsDefined(typeof(WorkflowProjectRunStartType), input.StartType))
        {
            throw new UserFriendlyException("StartType 参数不合法。");
        }

        if (
            input.StartType == WorkflowProjectRunStartType.Cyclic
            && input.CycleIntervalSeconds is null or <= 0
        )
        {
            throw new UserFriendlyException("周期启动必须提供大于 0 的周期间隔（秒）。");
        }

        Guid projectId = input.ProjectId;

        // 项目独占校验：同一项目已有 Queued 或 Running 运行时拒绝创建新运行。
        bool hasActiveRun = await AsyncExecuter.AnyAsync(
            (await _runRepository.GetQueryableAsync()).Where(x =>
                x.ProjectId == projectId
                && (
                    x.Status == WorkflowProjectRunStatus.Queued
                    || x.Status == WorkflowProjectRunStatus.Running
                )
            )
        );
        if (hasActiveRun)
        {
            throw new UserFriendlyException(
                "该项目已有进行中的运行，请等待完成、取消或删除现有运行后再启动新运行。"
            );
        }

        (List<Guid> workflowIds, Guid deploymentId, int deploymentRevision) =
            await ResolveActiveDeploymentAsync(projectId);

        bool continueOnError = input.OnErrorAction == WorkflowProjectRunOnErrorAction.ContinueRun;

        Guid runId = GuidGenerator.Create();
        WorkflowProjectRun run = WorkflowProjectRun.Create(
            runId,
            projectId,
            input.Name.Trim(),
            input.StartType,
            input.StartType == WorkflowProjectRunStartType.Cyclic
                ? input.CycleIntervalSeconds
                : null,
            deploymentId,
            deploymentRevision,
            workflowIds,
            continueOnError
        );
        await _runRepository.InsertAsync(run, autoSave: true);

        string hangfireJobId = _backgroundJobClient.Enqueue<WorkflowProjectRunJob>(job =>
            job.ExecuteAsync(
                new WorkflowProjectRunJobArgs
                {
                    RunId = runId,
                    ProjectId = projectId,
                    DeploymentId = deploymentId,
                    WorkflowIds = workflowIds,
                    ContinueOnError = continueOnError,
                }
            )
        );
        run.SetHangfireJobId(hangfireJobId);
        await _runRepository.UpdateAsync(run, autoSave: true);

        // 周期任务：注册项目级 RecurringJob，后续按周期创建新的运行实例。
        if (input.StartType == WorkflowProjectRunStartType.Cyclic)
        {
            _recurringJobManager.AddOrUpdate<WorkflowProjectRunJob>(
                BuildRecurringJobId(projectId),
                job =>
                    job.ExecuteRecurringAsync(
                        new WorkflowProjectRunRecurringArgs
                        {
                            ProjectId = projectId,
                            DeploymentId = deploymentId,
                            DeploymentRevision = deploymentRevision,
                            ContinueOnError = continueOnError,
                            NamePrefix = input.Name.Trim(),
                        }
                    ),
                BuildCronExpression(input.CycleIntervalSeconds!.Value)
            );
        }

        return new WorkflowProjectRunEnqueueResultDto
        {
            RunId = runId,
            ProjectId = projectId,
            WorkflowCount = workflowIds.Count,
            HangfireJobId = hangfireJobId,
            DeploymentId = deploymentId,
            DeploymentRevision = deploymentRevision,
        };
    }

    private async Task<(
        List<Guid> WorkflowIds,
        Guid DeploymentId,
        int DeploymentRevision
    )> ResolveActiveDeploymentAsync(Guid projectId)
    {
        WorkflowProjectDeployment? activeDeployment = await AsyncExecuter.FirstOrDefaultAsync(
            (await _deploymentRepository.GetQueryableAsync())
                .Where(x =>
                    x.ProjectId == projectId
                    && x.Status == WorkflowProjectDeploymentStatus.Activated
                )
                .OrderByDescending(x => x.Revision)
        );

        if (activeDeployment is null)
        {
            throw new UserFriendlyException(
                "当前项目没有已激活的部署快照，请先发布并激活部署后再创建运行。"
            );
        }

        List<WorkflowProjectDeploymentItem> deploymentItems =
            DeserializeJson<List<WorkflowProjectDeploymentItem>>(activeDeployment.SnapshotJson)
            ?? [];

        List<Guid> deploymentWorkflowIds = deploymentItems
            .OrderBy(x => x.OrderNo)
            .Select(x => x.WorkflowId)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (deploymentWorkflowIds.Count == 0)
        {
            throw new UserFriendlyException("当前项目的激活部署快照为空，无法创建运行。");
        }

        return (deploymentWorkflowIds, activeDeployment.Id, activeDeployment.Revision);
    }

    private static string BuildRecurringJobId(Guid projectId) =>
        $"workflow-project-run:{projectId:N}";

    private static string BuildCronExpression(int intervalSeconds)
    {
        if (intervalSeconds < 60)
        {
            int seconds = Math.Clamp(intervalSeconds, 1, 59);
            return $"*/{seconds} * * * * *";
        }

        int minutes = Math.Clamp(intervalSeconds / 60, 1, 1440);
        return $"*/{minutes} * * * *";
    }

    /// <inheritdoc/>
    [HttpGet("runs")]
    public async Task<List<WorkflowProjectRunStatusDto>> GetProjectRunsAsync(
        WorkflowProjectRunListInput input
    )
    {
        if (input is null)
        {
            throw new UserFriendlyException("查询参数不能为空。");
        }

        if (input.SkipCount < 0)
        {
            throw new UserFriendlyException("SkipCount 不能小于 0。");
        }

        if (input.MaxResultCount <= 0 || input.MaxResultCount > 200)
        {
            throw new UserFriendlyException("MaxResultCount 必须在 1 到 200 之间。");
        }

        try
        {
            IQueryable<WorkflowProjectRun> queryable = await _runRepository.GetQueryableAsync();
            if (input.ProjectId != Guid.Empty)
            {
                queryable = queryable.Where(x => x.ProjectId == input.ProjectId);
            }

            if (!string.IsNullOrWhiteSpace(input.Name))
            {
                string keyword = input.Name.Trim();
                queryable = queryable.Where(x => x.Name.Contains(keyword));
            }

            List<WorkflowProjectRun> runs = await AsyncExecuter.ToListAsync(
                queryable
                    .OrderByDescending(x => x.CreationTime)
                    .Skip(input.SkipCount)
                    .Take(input.MaxResultCount)
            );

            return runs.Select(MapRunToStatusDto).ToList();
        }
        catch (Exception)
        {
            throw new UserFriendlyException("查询运行列表失败，请稍后重试。");
        }
    }

    /// <inheritdoc/>
    [HttpGet("runs/{runId:guid}")]
    public async Task<WorkflowProjectRunStatusDto> GetProjectRunStatusAsync(Guid runId)
    {
        WorkflowProjectRun run = await _runRepository.GetAsync(runId);
        return MapRunToStatusDto(run);
    }

    /// <inheritdoc/>
    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task CancelProjectRunAsync(Guid runId)
    {
        WorkflowProjectRun run = await _runRepository.GetAsync(runId);
        run.RequestCancel();
        await _runRepository.UpdateAsync(run, autoSave: true);

        if (run.StartType == WorkflowProjectRunStartType.Cyclic)
        {
            _recurringJobManager.RemoveIfExists(BuildRecurringJobId(run.ProjectId));
        }
    }

    /// <inheritdoc/>
    [HttpPut("runs/{runId:guid}")]
    public async Task<WorkflowProjectRunStatusDto> UpdateProjectRunAsync(
        Guid runId,
        WorkflowProjectRunUpdateInput input
    )
    {
        if (runId == Guid.Empty)
        {
            throw new UserFriendlyException("RunId 不能为空。");
        }

        if (input is null)
        {
            throw new UserFriendlyException("修改参数不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("运行名称不能为空。");
        }

        if (!Enum.IsDefined(typeof(WorkflowProjectRunOnErrorAction), input.OnErrorAction))
        {
            throw new UserFriendlyException("OnErrorAction 参数不合法。");
        }

        try
        {
            WorkflowProjectRun? run = await AsyncExecuter.FirstOrDefaultAsync(
                (await _runRepository.GetQueryableAsync()).Where(x => x.Id == runId)
            );
            if (run is null)
            {
                throw new UserFriendlyException("运行不存在或已被删除。");
            }

            if (run.Status == WorkflowProjectRunStatus.Running)
            {
                throw new UserFriendlyException("执行中的运行不允许修改，请先取消或等待结束。");
            }

            run.UpdateName(input.Name.Trim());
            run.UpdateContinueOnError(
                input.OnErrorAction == WorkflowProjectRunOnErrorAction.ContinueRun
            );

            await _runRepository.UpdateAsync(run, autoSave: true);
            return MapRunToStatusDto(run);
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new UserFriendlyException("修改运行失败，请稍后重试。");
        }
    }

    /// <inheritdoc/>
    [HttpDelete("runs/{runId:guid}")]
    public async Task DeleteProjectRunAsync(Guid runId)
    {
        WorkflowProjectRun run = await _runRepository.GetAsync(runId);
        if (run.Status == WorkflowProjectRunStatus.Running)
        {
            throw new UserFriendlyException("执行中的运行不允许删除，请先取消或等待结束。");
        }

        if (run.StartType == WorkflowProjectRunStartType.Cyclic)
        {
            _recurringJobManager.RemoveIfExists(BuildRecurringJobId(run.ProjectId));
        }

        await _runRepository.DeleteAsync(run, autoSave: true);
    }

    /// <inheritdoc/>
    [HttpPost("executions")]
    public async Task<WorkflowExecutionTriggerResultDto> ExecuteAsync(
        WorkflowExecutionTriggerInput input
    )
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        // 项目级调试互斥：单机一次只能调试一个项目（固定调试任务）。
        if (input.Mode == WorkflowExecutionMode.DebugStep)
        {
            _sessionStore.EnsureExclusiveDebugProject(input.ProjectId);
        }

        WorkflowExecutionBootstrap bootstrap = await PrepareBootstrapAsync(input);
        await PreheatRunDependenciesAsync(input, bootstrap);

        return input.Mode switch
        {
            WorkflowExecutionMode.DebugStep => await StartDebugSessionAsync(input, bootstrap),
            WorkflowExecutionMode.Loop => await RunLoopModeAsync(input, bootstrap),
            _ => await RunOnceAsync(input, bootstrap),
        };
    }

    /// <summary>
    /// 从冻结部署快照的图数据执行单个工作流（供运行 Job 内部调用，不对外暴露 HTTP 端点）。
    /// </summary>
    [RemoteService(false)]
    public async Task<WorkflowExecutionTriggerResultDto> ExecuteFrozenWorkflowAsync(
        Guid projectId,
        Guid runId,
        Guid workflowId,
        string frozenGraphData
    )
    {
        if (projectId == Guid.Empty || workflowId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 与 WorkflowId 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(frozenGraphData))
        {
            throw new UserFriendlyException("冻结图数据不能为空。");
        }

        WorkflowExecutionTriggerInput input = new()
        {
            ProjectId = projectId,
            RunId = runId == Guid.Empty ? null : runId,
            WorkflowId = workflowId,
            Mode = WorkflowExecutionMode.RunOnce,
        };

        WorkflowExecutionBootstrap bootstrap = await PrepareBootstrapAsync(
            input,
            frozenGraphData,
            skipOfflineValidation: true
        );
        return await RunOnceAsync(input, bootstrap);
    }

    /// <inheritdoc/>
    [HttpPost("executions/{executionId:guid}/steps")]
    public async Task<WorkflowExecutionStepResultDto> StepAsync(
        Guid executionId,
        WorkflowExecutionStepInput input
    )
    {
        WorkflowExecutionSession session = _sessionStore.Get(executionId);
        if (session.Mode != WorkflowExecutionMode.DebugStep)
        {
            throw new UserFriendlyException("仅调试模式支持步进。");
        }

        await session.Gate.WaitAsync();
        try
        {
            if (
                session.Status
                is WorkflowExecutionStatus.Completed
                    or WorkflowExecutionStatus.Faulted
                    or WorkflowExecutionStatus.Stopped
            )
            {
                return new WorkflowExecutionStepResultDto
                {
                    ExecutionId = executionId,
                    Status = BuildStatusDto(session, input.IncludeVariables),
                };
            }

            try
            {
                _kernel.ExecuteSteps(session, input.Steps);
                if (session.Status == WorkflowExecutionStatus.Completed)
                {
                    PersistOutputs(session);
                    session.FrozenVariables = session.VariablePool.Snapshot(
                        session.Context,
                        session.OutputStagedKeys
                    );
                    session.VariablePool.Clear(session.Context);
                }
            }
            catch (WorkflowNodeExecutionException ex)
            {
                session.MarkFaulted(ex.NodeId, ex.InnerException?.Message ?? ex.Message);
                session.FrozenVariables = session.VariablePool.Snapshot(session.Context);
                session.VariablePool.Clear(session.Context);
            }

            return new WorkflowExecutionStepResultDto
            {
                ExecutionId = executionId,
                Status = BuildStatusDto(session, input.IncludeVariables),
            };
        }
        finally
        {
            session.Gate.Release();
        }
    }

    /// <inheritdoc/>
    [HttpGet("executions")]
    public async Task<List<WorkflowExecutionStatusDto>> GetDebugSessionsAsync(
        Guid projectId = default,
        Guid runId = default,
        bool includeVariables = false
    )
    {
        IEnumerable<WorkflowExecutionSession> sessions = _sessionStore.GetAll();
        List<Guid> runWorkflowIds = [];

        if (runId != Guid.Empty)
        {
            WorkflowProjectRun run = await _runRepository.GetAsync(runId);
            if (projectId != Guid.Empty && run.ProjectId != projectId)
            {
                throw new UserFriendlyException("RunId 与 ProjectId 不匹配。");
            }

            runWorkflowIds = (DeserializeJson<List<Guid>>(run.WorkflowIdsJson) ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
            sessions = sessions.Where(x => x.ProjectId == run.ProjectId && x.RunId == runId);
        }
        else if (projectId != Guid.Empty)
        {
            sessions = sessions.Where(x => x.ProjectId == projectId);
        }

        List<WorkflowExecutionStatusDto> result = new();
        foreach (WorkflowExecutionSession session in sessions)
        {
            await session.Gate.WaitAsync();
            try
            {
                WorkflowExecutionStatusDto dto = BuildStatusDto(session, includeVariables);
                if (runWorkflowIds.Count > 0)
                {
                    dto.WorkflowOrderNo = Math.Max(
                        runWorkflowIds.IndexOf(session.WorkflowId) + 1,
                        0
                    );
                    dto.CurrentNodeOrderNo =
                        session.ExecutedSteps > 0
                            ? session.ExecutedSteps
                            : (string.IsNullOrWhiteSpace(session.CurrentNodeId) ? 0 : 1);
                }

                result.Add(dto);
            }
            finally
            {
                session.Gate.Release();
            }
        }

        if (runId != Guid.Empty && result.Count > 1)
        {
            WorkflowExecutionStatusDto current = result
                .OrderByDescending(x => GetExecutionStatusPriority(x.Status))
                .ThenByDescending(x => x.WorkflowOrderNo)
                .ThenByDescending(x => x.CurrentNodeOrderNo)
                .First();
            return [current];
        }

        return result;
    }

    /// <inheritdoc/>
    [HttpGet("executions/{executionId:guid}")]
    public async Task<WorkflowExecutionStatusDto> GetStatusAsync(
        Guid executionId,
        bool includeVariables = true
    )
    {
        WorkflowExecutionSession session = _sessionStore.Get(executionId);
        await session.Gate.WaitAsync();
        try
        {
            return BuildStatusDto(session, includeVariables);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    /// <inheritdoc/>
    [HttpDelete("executions/{executionId:guid}")]
    public async Task StopDebugAsync(Guid executionId)
    {
        WorkflowExecutionSession session = _sessionStore.Get(executionId);
        await session.Gate.WaitAsync();
        try
        {
            session.MarkStopped();
            session.VariablePool.Clear(session.Context);
            session.FrozenVariables = [];
        }
        finally
        {
            session.Gate.Release();
        }

        _sessionStore.Remove(executionId);
    }

    private async Task<WorkflowExecutionTriggerResultDto> StartDebugSessionAsync(
        WorkflowExecutionTriggerInput input,
        WorkflowExecutionBootstrap bootstrap
    )
    {
        Dictionary<string, object?> initialVariables = LoadInitialVariables(
            bootstrap.InputBindings
        );

        WorkflowExecutionSession session = CreateSession(
            bootstrap,
            input.Mode,
            input.LoopCount,
            initialVariables
        );
        _sessionStore.Add(session);

        return new WorkflowExecutionTriggerResultDto
        {
            ExecutionId = session.ExecutionId,
            Status = BuildStatusDto(session, includeVariables: true),
        };
    }

    private async Task<WorkflowExecutionTriggerResultDto> RunOnceAsync(
        WorkflowExecutionTriggerInput input,
        WorkflowExecutionBootstrap bootstrap
    )
    {
        Dictionary<string, object?> initialVariables = LoadInitialVariables(
            bootstrap.InputBindings
        );

        WorkflowExecutionSession session = CreateSession(
            bootstrap,
            WorkflowExecutionMode.RunOnce,
            loopCount: 1,
            initialVariables
        );

        try
        {
            _kernel.ExecuteToCompletion(session);
            PersistOutputs(session);
            session.FrozenVariables = session.VariablePool.Snapshot(
                session.Context,
                session.OutputStagedKeys
            );
        }
        catch (WorkflowNodeExecutionException ex)
        {
            session.MarkFaulted(ex.NodeId, ex.InnerException?.Message ?? ex.Message);
            session.FrozenVariables = session.VariablePool.Snapshot(session.Context);
            throw new UserFriendlyException(
                $"工作流执行失败，节点 {ex.NodeId ?? "<unknown>"}：{session.ErrorMessage}"
            );
        }
        finally
        {
            session.VariablePool.Clear(session.Context);
            session.Dispose();
        }

        return new WorkflowExecutionTriggerResultDto
        {
            ExecutionId = session.ExecutionId,
            Status = BuildStatusDto(session, includeVariables: true),
        };
    }

    private async Task<WorkflowExecutionTriggerResultDto> RunLoopModeAsync(
        WorkflowExecutionTriggerInput input,
        WorkflowExecutionBootstrap bootstrap
    )
    {
        if (input.LoopCount <= 0)
        {
            throw new UserFriendlyException("LoopCount 必须大于 0。");
        }

        Dictionary<string, object?> finalVariables = new(StringComparer.Ordinal);
        WorkflowExecutionSession? lastSession = null;

        for (int i = 0; i < input.LoopCount; i++)
        {
            Dictionary<string, object?> initialVariables = LoadInitialVariables(
                bootstrap.InputBindings
            );

            WorkflowExecutionSession session = CreateSession(
                bootstrap,
                WorkflowExecutionMode.Loop,
                input.LoopCount,
                initialVariables
            );

            try
            {
                _kernel.ExecuteToCompletion(session);
                finalVariables = CollectDeclaredVariables(session);
                lastSession = session;
            }
            catch (WorkflowNodeExecutionException ex)
            {
                session.MarkFaulted(ex.NodeId, ex.InnerException?.Message ?? ex.Message);
                session.VariablePool.Clear(session.Context);
                session.Dispose();
                throw new UserFriendlyException(
                    $"循环执行第 {i + 1} 次失败，节点 {ex.NodeId ?? "<unknown>"}：{session.ErrorMessage}"
                );
            }
            finally
            {
                session.VariablePool.Clear(session.Context);
                session.Dispose();
            }
        }

        if (lastSession is null)
        {
            throw new UserFriendlyException("循环执行未产生有效会话。");
        }

        WorkflowExecutionSession resultSession = CreateSession(
            bootstrap,
            WorkflowExecutionMode.Loop,
            input.LoopCount,
            finalVariables
        );
        resultSession.CompletedLoops = input.LoopCount;
        resultSession.MarkCompleted();
        PersistOutputs(resultSession);
        resultSession.FrozenVariables = resultSession.VariablePool.Snapshot(
            resultSession.Context,
            resultSession.OutputStagedKeys
        );
        resultSession.VariablePool.Clear(resultSession.Context);
        resultSession.Dispose();

        return new WorkflowExecutionTriggerResultDto
        {
            ExecutionId = resultSession.ExecutionId,
            Status = BuildStatusDto(resultSession, includeVariables: true),
        };
    }

    private void PersistOutputs(WorkflowExecutionSession session)
    {
        foreach (WorkflowVariableBindingKeyDto binding in session.OutputBindings)
        {
            object? value = session.Context.Get(binding.VariableName);
            _runtimeVariablePool.Write(binding.OwnerWorkflowId, binding.VariableName, value);
            session.OutputStagedKeys[binding.VariableName] =
                $"pool:{binding.OwnerWorkflowId:N}:{binding.VariableName}";
        }
    }

    private async Task<WorkflowExecutionBootstrap> PrepareBootstrapAsync(
        WorkflowExecutionTriggerInput input,
        string? frozenGraphDataOverride = null,
        bool skipOfflineValidation = false
    )
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        WorkflowProjectRun? runForExecution = null;

        Guid resolvedWorkflowId = input.WorkflowId;
        if (resolvedWorkflowId == Guid.Empty)
        {
            if (!input.RunId.HasValue || input.RunId.Value == Guid.Empty)
            {
                throw new UserFriendlyException("WorkflowId 与 RunId 至少传一个。");
            }

            runForExecution = await _runRepository.GetAsync(input.RunId.Value);
            if (runForExecution.ProjectId != input.ProjectId)
            {
                throw new UserFriendlyException("RunId 与 ProjectId 不匹配。");
            }

            List<Guid> runWorkflowIds =
                DeserializeJson<List<Guid>>(runForExecution.WorkflowIdsJson) ?? [];
            runWorkflowIds = runWorkflowIds.Where(x => x != Guid.Empty).Distinct().ToList();

            if (runWorkflowIds.Count == 0)
            {
                throw new UserFriendlyException("运行内不存在可调试工作流。");
            }

            List<WorkflowProjectRunItemResult> runResults =
                DeserializeJson<List<WorkflowProjectRunItemResult>>(runForExecution.ResultsJson)
                ?? [];
            resolvedWorkflowId = ResolveDebugWorkflowIdByRunProgress(runWorkflowIds, runResults);
        }
        else if (input.RunId.HasValue && input.RunId.Value != Guid.Empty)
        {
            runForExecution = await _runRepository.GetAsync(input.RunId.Value);
            if (runForExecution.ProjectId != input.ProjectId)
            {
                throw new UserFriendlyException("RunId 与 ProjectId 不匹配。");
            }

            List<Guid> runWorkflowIds =
                DeserializeJson<List<Guid>>(runForExecution.WorkflowIdsJson) ?? [];
            if (!runWorkflowIds.Contains(resolvedWorkflowId))
            {
                throw new UserFriendlyException("传入的 WorkflowId 不属于该 RunId。");
            }
        }

        input.WorkflowId = resolvedWorkflowId;

        // 冻结执行：优先使用部署快照中的冻结图，独立于 live 工作流实体。
        string graphData;
        string? entityName = null;
        if (frozenGraphDataOverride is not null)
        {
            graphData = frozenGraphDataOverride;
        }
        else
        {
            WorkflowDefinition entity = await _repository.GetAsync(resolvedWorkflowId);
            if (entity.ProjectId != input.ProjectId)
            {
                throw new UserFriendlyException("工作流不存在或不属于该项目。");
            }

            graphData = entity.GraphData;
            entityName = entity.Name;
        }

        RuntimeWorkflowDefinition compiled;
        GraphDataModel graph;
        string workflowName;
        try
        {
            (workflowName, graph) = WorkflowGraphCompiler.ParseContent(graphData);
            compiled = await new WorkflowGraphCompiler(_registry).CompileAsync(graph, workflowName);
        }
        catch (WorkflowCompilationException ex)
        {
            throw new UserFriendlyException("工作流编译失败：" + ex.Message);
        }
        catch (JsonException ex)
        {
            throw new UserFriendlyException("工作流内容 JSON 解析失败：" + ex.Message);
        }

        WorkflowSignature signature = WorkflowSignatureExtractor.Extract(graph);
        List<WorkflowVariableBindingKeyDto> inputBindings = BuildBindingKeys(
            input.InputVariableBindings,
            input.InputVariableKeys is { Count: > 0 } keys ? keys : signature.Inputs,
            resolvedWorkflowId
        );
        List<WorkflowVariableBindingKeyDto> outputBindings = BuildBindingKeys(
            input.OutputVariableBindings,
            input.OutputVariableNames is { Count: > 0 } names ? names : signature.Outputs,
            resolvedWorkflowId
        );

        ValidateBindingKeys(inputBindings, "input");
        ValidateBindingKeys(outputBindings, "output", uniqueByVariableName: true);

        VariableCompileRequestDto compileRequest = await _compileRequestFactory.BuildAsync(
            input.ProjectId,
            resolvedWorkflowId,
            graph,
            VariableDefUseAnalysisMode.Conservative
        );
        if (!skipOfflineValidation)
        {
            VariableCompileResultDto compileResult = await _offlineVariableLibrary.CompileAsync(
                compileRequest
            );
            if (!compileResult.CanPublish)
            {
                string message = string.Join(
                    " | ",
                    compileResult
                        .Diagnostics.Where(x => x.Severity == VariableDiagnosticSeverity.Error)
                        .Select(x => x.Message)
                        .Take(10)
                );

                throw new UserFriendlyException("执行前变量编译校验未通过：" + message);
            }
        }

        IReadOnlyList<string> statementNodeIds =
            WorkflowNodeScheduleBuilder.BuildExecutableNodeOrder(graph);

        return new WorkflowExecutionBootstrap
        {
            ProjectId = input.ProjectId,
            RunId = input.RunId,
            WorkflowId = resolvedWorkflowId,
            WorkflowName = string.IsNullOrWhiteSpace(entityName) ? compiled.Name : entityName,
            RuntimeWorkflow = compiled,
            InputBindings = inputBindings,
            OutputBindings = outputBindings,
            Declarations = compileRequest.Declarations,
            StatementNodeIds = statementNodeIds,
        };
    }

    private WorkflowExecutionSession CreateSession(
        WorkflowExecutionBootstrap bootstrap,
        WorkflowExecutionMode mode,
        int loopCount,
        IReadOnlyDictionary<string, object?> initialVariables
    )
    {
        WorkflowContext context = new();
        WorkflowExecutionVariablePool variablePool = new(bootstrap.Declarations);

        variablePool.Clear(context);
        variablePool.Initialize(context);
        variablePool.ApplyInitialValues(initialVariables);
        foreach ((string key, object? value) in initialVariables)
        {
            context.Set(key, value);
        }

        return new WorkflowExecutionSession
        {
            ExecutionId = GuidGenerator.Create(),
            RunId = bootstrap.RunId,
            ProjectId = bootstrap.ProjectId,
            WorkflowId = bootstrap.WorkflowId,
            WorkflowName = bootstrap.WorkflowName,
            Mode = mode,
            LoopCount = loopCount,
            RuntimeWorkflow = bootstrap.RuntimeWorkflow,
            StatementNodeIds = bootstrap.StatementNodeIds,
            Context = context,
            VariablePool = variablePool,
            OutputBindings = bootstrap.OutputBindings,
        };
    }

    private static Dictionary<string, object?> CollectDeclaredVariables(
        WorkflowExecutionSession session
    )
    {
        Dictionary<string, object?> values = new(StringComparer.Ordinal);
        foreach (
            WorkflowVariableResultDto variable in session.VariablePool.Snapshot(session.Context)
        )
        {
            values[variable.Name] = session.Context.Get(variable.Name);
        }

        return values;
    }

    private Dictionary<string, object?> LoadInitialVariables(
        IReadOnlyList<WorkflowVariableBindingKeyDto> inputBindings
    )
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach (
            WorkflowVariableBindingKeyDto binding in inputBindings.DistinctBy(x =>
                (x.OwnerWorkflowId, x.VariableName)
            )
        )
        {
            if (
                _runtimeVariablePool.TryRead(
                    binding.OwnerWorkflowId,
                    binding.VariableName,
                    out object? value
                )
            )
            {
                result[binding.VariableName] = value;
            }
        }

        return result;
    }

    private WorkflowExecutionStatusDto BuildStatusDto(
        WorkflowExecutionSession session,
        bool includeVariables
    )
    {
        List<WorkflowVariableResultDto> variables = includeVariables
            ? (
                session.Status
                    is WorkflowExecutionStatus.Completed
                        or WorkflowExecutionStatus.Faulted
                        or WorkflowExecutionStatus.Stopped
                    ? session.FrozenVariables
                    : session.VariablePool.Snapshot(session.Context, session.OutputStagedKeys)
            )
            : [];

        return new WorkflowExecutionStatusDto
        {
            ExecutionId = session.ExecutionId,
            RunId = session.RunId,
            ProjectId = session.ProjectId,
            WorkflowId = session.WorkflowId,
            WorkflowName = session.WorkflowName,
            Mode = session.Mode,
            Status = session.Status,
            ExecutedSteps = session.ExecutedSteps,
            TotalSteps = session.RuntimeWorkflow.Statements.Count,
            CurrentNodeId = session.CurrentNodeId,
            WorkflowOrderNo = 0,
            CurrentNodeOrderNo =
                session.ExecutedSteps > 0
                    ? session.ExecutedSteps
                    : (string.IsNullOrWhiteSpace(session.CurrentNodeId) ? 0 : 1),
            FaultNodeId = session.FaultNodeId,
            LoopCount = session.LoopCount,
            CompletedLoops = session.CompletedLoops,
            ErrorMessage = session.ErrorMessage,
            DurationMs = session.DurationMs,
            Variables = variables,
        };
    }

    private async Task PreheatRunDependenciesAsync(
        WorkflowExecutionTriggerInput input,
        WorkflowExecutionBootstrap bootstrap
    )
    {
        if (input.Mode != WorkflowExecutionMode.DebugStep || !input.RunId.HasValue)
        {
            return;
        }

        WorkflowProjectRun run = await _runRepository.GetAsync(input.RunId.Value);
        if (run.ProjectId != input.ProjectId)
        {
            throw new UserFriendlyException("RunId 与 ProjectId 不匹配。");
        }

        List<Guid> runWorkflowIds = (DeserializeJson<List<Guid>>(run.WorkflowIdsJson) ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        int targetIndex = runWorkflowIds.IndexOf(bootstrap.WorkflowId);
        if (targetIndex <= 0)
        {
            return;
        }

        for (int i = 0; i < targetIndex; i++)
        {
            WorkflowExecutionTriggerInput preheatInput = new()
            {
                ProjectId = input.ProjectId,
                WorkflowId = runWorkflowIds[i],
                Mode = WorkflowExecutionMode.RunOnce,
            };

            WorkflowExecutionBootstrap preheatBootstrap = await PrepareBootstrapAsync(preheatInput);
            await RunOnceAsync(preheatInput, preheatBootstrap);
        }
    }

    private static List<WorkflowVariableBindingKeyDto> BuildBindingKeys(
        List<WorkflowVariableBindingKeyDto>? explicitBindings,
        IReadOnlyList<string> fallbackVariableNames,
        Guid fallbackOwnerWorkflowId
    )
    {
        if (explicitBindings is { Count: > 0 })
        {
            return explicitBindings
                .Where(x =>
                    x.OwnerWorkflowId != Guid.Empty && !string.IsNullOrWhiteSpace(x.VariableName)
                )
                .ToList();
        }

        return fallbackVariableNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new WorkflowVariableBindingKeyDto
            {
                OwnerWorkflowId = fallbackOwnerWorkflowId,
                VariableName = x,
            })
            .ToList();
    }

    private static void ValidateBindingKeys(
        List<WorkflowVariableBindingKeyDto> bindings,
        string direction,
        bool uniqueByVariableName = false
    )
    {
        if (bindings.Count == 0)
        {
            return;
        }

        IEnumerable<WorkflowVariableBindingKeyDto> invalid = bindings.Where(x =>
            x.OwnerWorkflowId == Guid.Empty || string.IsNullOrWhiteSpace(x.VariableName)
        );
        if (invalid.Any())
        {
            throw new UserFriendlyException(
                $"变量绑定无效：{direction} 绑定存在空 OwnerWorkflowId 或 VariableName。"
            );
        }

        bool hasDuplicate = uniqueByVariableName
            ? bindings.GroupBy(x => x.VariableName, StringComparer.Ordinal).Any(x => x.Count() > 1)
            : bindings.GroupBy(x => (x.OwnerWorkflowId, x.VariableName)).Any(x => x.Count() > 1);

        if (hasDuplicate)
        {
            string rule = uniqueByVariableName ? "变量名唯一" : "归属+变量名唯一";
            throw new UserFriendlyException($"变量绑定重复：{direction} 绑定需满足 {rule}。");
        }
    }

    private static WorkflowProjectRunStatusDto MapRunToStatusDto(WorkflowProjectRun run)
    {
        List<Guid> workflowIds = DeserializeJson<List<Guid>>(run.WorkflowIdsJson) ?? [];
        List<WorkflowProjectRunItemResult> items =
            DeserializeJson<List<WorkflowProjectRunItemResult>>(run.ResultsJson) ?? [];

        return new WorkflowProjectRunStatusDto
        {
            RunId = run.Id,
            ProjectId = run.ProjectId,
            Name = run.Name,
            HangfireJobId = run.HangfireJobId,
            Status = run.Status,
            StartType = run.StartType,
            CycleIntervalSeconds = run.CycleIntervalSeconds,
            OnErrorAction = run.ContinueOnError
                ? WorkflowProjectRunOnErrorAction.ContinueRun
                : WorkflowProjectRunOnErrorAction.StopRun,
            DeploymentId = run.DeploymentId,
            DeploymentRevision = run.DeploymentRevision,
            WorkflowCount = workflowIds.Count,
            ExecutedCount = run.ExecutedCount,
            SuccessCount = run.SuccessCount,
            FailedCount = run.FailedCount,
            IsCancelRequested = run.IsCancelRequested,
            CreationTime = run.CreationTime,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            ErrorMessage = run.ErrorMessage,
            Items = items
                .Select(x => new WorkflowProjectRunItemDto
                {
                    WorkflowId = x.WorkflowId,
                    Status = x.Status,
                    ExecutionId = x.ExecutionId,
                    ErrorMessage = x.ErrorMessage,
                    StartedAt = x.StartedAt,
                    FinishedAt = x.FinishedAt,
                })
                .ToList(),
        };
    }

    private static WorkflowProjectTaskDto MapTaskDto(WorkflowProjectTask task, string workflowName)
    {
        return new WorkflowProjectTaskDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            WorkflowId = task.WorkflowId,
            WorkflowName = workflowName,
            IsEnabled = task.IsEnabled,
            OrderNo = task.OrderNo,
        };
    }

    private static WorkflowProjectDeploymentDto MapDeploymentDto(
        WorkflowProjectDeployment deployment
    )
    {
        List<WorkflowProjectDeploymentItem> items =
            DeserializeJson<List<WorkflowProjectDeploymentItem>>(deployment.SnapshotJson) ?? [];

        return new WorkflowProjectDeploymentDto
        {
            Id = deployment.Id,
            ProjectId = deployment.ProjectId,
            Revision = deployment.Revision,
            Status = deployment.Status,
            IsCurrentActive = deployment.Status == WorkflowProjectDeploymentStatus.Activated,
            CanActivate =
                deployment.Status
                    is WorkflowProjectDeploymentStatus.Published
                        or WorkflowProjectDeploymentStatus.Archived,
            CanReactivate =
                deployment.Status
                    is WorkflowProjectDeploymentStatus.Published
                        or WorkflowProjectDeploymentStatus.Archived
                        or WorkflowProjectDeploymentStatus.Activated,
            CanRollback =
                deployment.Status
                    is WorkflowProjectDeploymentStatus.Published
                        or WorkflowProjectDeploymentStatus.Archived,
            SnapshotHash = deployment.SnapshotHash,
            ItemCount = items.Count,
            ActivatedAt = deployment.ActivatedAt,
            ActivatedBy = deployment.ActivatedBy,
            CreationTime = deployment.CreationTime,
        };
    }

    private async Task<WorkflowProjectDeploymentDto> SwitchActiveDeploymentAsync(
        WorkflowProjectDeployment deployment,
        string operationName,
        bool allowAlreadyActive
    )
    {
        if (
            deployment.Status
            is not WorkflowProjectDeploymentStatus.Published
                and not WorkflowProjectDeploymentStatus.Archived
                and not WorkflowProjectDeploymentStatus.Activated
        )
        {
            throw new UserFriendlyException($"当前部署状态不支持{operationName}。");
        }

        if (deployment.Status == WorkflowProjectDeploymentStatus.Activated)
        {
            if (!allowAlreadyActive)
            {
                throw new UserFriendlyException("目标部署已处于激活状态，无需回滚。");
            }

            return MapDeploymentDto(deployment);
        }

        List<WorkflowProjectDeployment> activeDeployments = await AsyncExecuter.ToListAsync(
            (await _deploymentRepository.GetQueryableAsync()).Where(x =>
                x.ProjectId == deployment.ProjectId
                && x.Status == WorkflowProjectDeploymentStatus.Activated
            )
        );

        foreach (
            WorkflowProjectDeployment active in activeDeployments.Where(x => x.Id != deployment.Id)
        )
        {
            active.Archive();
            await _deploymentRepository.UpdateAsync(active, autoSave: true);
        }

        deployment.Activate(CurrentUser.Id, Clock.Now);
        await _deploymentRepository.UpdateAsync(deployment, autoSave: true);
        return MapDeploymentDto(deployment);
    }

    private static string ComputeGraphHash(string graphData)
    {
        byte[] bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(graphData));
        return Convert.ToHexString(bytes);
    }

    private static string ComputeSnapshotHash(IEnumerable<WorkflowProjectDeploymentItem> items)
    {
        string json = JsonSerializer.Serialize(
            items.OrderBy(x => x.OrderNo).ThenBy(x => x.WorkflowId)
        );
        byte[] bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    private sealed class WorkflowExecutionBootstrap
    {
        public required Guid ProjectId { get; init; }

        public Guid? RunId { get; init; }

        public required Guid WorkflowId { get; init; }

        public required string WorkflowName { get; init; }

        public required RuntimeWorkflowDefinition RuntimeWorkflow { get; init; }

        public required IReadOnlyList<string> StatementNodeIds { get; init; }

        public required List<VariableDeclarationDto> Declarations { get; init; }

        public required List<WorkflowVariableBindingKeyDto> InputBindings { get; init; }

        public required List<WorkflowVariableBindingKeyDto> OutputBindings { get; init; }
    }

    private static int GetExecutionStatusPriority(WorkflowExecutionStatus status)
    {
        return status switch
        {
            WorkflowExecutionStatus.Running => 5,
            WorkflowExecutionStatus.Pending => 4,
            WorkflowExecutionStatus.Faulted => 3,
            WorkflowExecutionStatus.Completed => 2,
            WorkflowExecutionStatus.Stopped => 1,
            _ => 0,
        };
    }

    private static Guid ResolveDebugWorkflowIdByRunProgress(
        IReadOnlyList<Guid> orderedWorkflowIds,
        IReadOnlyList<WorkflowProjectRunItemResult> results
    )
    {
        Dictionary<Guid, WorkflowProjectRunItemStatus> statusMap = results
            .Where(x => x.WorkflowId != Guid.Empty)
            .GroupBy(x => x.WorkflowId)
            .ToDictionary(x => x.Key, x => x.Last().Status);

        foreach (Guid workflowId in orderedWorkflowIds)
        {
            if (
                statusMap.TryGetValue(workflowId, out WorkflowProjectRunItemStatus status)
                && status == WorkflowProjectRunItemStatus.Running
            )
            {
                return workflowId;
            }
        }

        foreach (Guid workflowId in orderedWorkflowIds)
        {
            if (
                !statusMap.TryGetValue(workflowId, out WorkflowProjectRunItemStatus status)
                || status == WorkflowProjectRunItemStatus.Pending
            )
            {
                return workflowId;
            }
        }

        foreach (Guid workflowId in orderedWorkflowIds)
        {
            if (
                statusMap.TryGetValue(workflowId, out WorkflowProjectRunItemStatus status)
                && status == WorkflowProjectRunItemStatus.Failed
            )
            {
                return workflowId;
            }
        }

        return orderedWorkflowIds[0];
    }
}
