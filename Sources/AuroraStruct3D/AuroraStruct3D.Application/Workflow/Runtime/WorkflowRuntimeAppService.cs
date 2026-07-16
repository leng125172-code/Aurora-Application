using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV.File.Images;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.OperatorFile;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using RuntimeWorkflowDefinition = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 工作流运行时应用服务实现。
/// </summary>
[Authorize]
[Route("api/app/workflow")]
public class WorkflowRuntimeAppService : AuroraStruct3DAppService, IWorkflowRuntimeAppService
{
    private const string ExecutionErrorCodeInvalidInput = "EXECUTION_INVALID_INPUT";
    private const string ExecutionErrorCodeUnhandled = "EXECUTION_UNHANDLED";
    private const string ExecutionErrorCodeFault = "EXECUTION_FAULT";
    private const string ExecutionErrorCodeLoopFault = "EXECUTION_LOOP_FAULT";
    private const string ExecutionErrorCodeStepInvalidInput = "STEP_INVALID_INPUT";

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
    private readonly IBlobContainer<OperatorFileBlobContainer> _operatorFileBlobContainer;
    private readonly IOperatorFileRecordRepository _operatorFileRecordRepository;

    private static readonly HashSet<string> UploadedFileExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".ply",
        ".pcd",
        ".xyz",
        ".txt",
        ".pts",
        ".asc",
        ".bmp",
        ".jpg",
        ".jpeg",
        ".png",
        ".tiff",
        ".tif",
        ".webp",
    };

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
        IRepository<VariableDefinition, Guid> variableDefinitionRepository,
        IBlobContainer<OperatorFileBlobContainer> operatorFileBlobContainer,
        IOperatorFileRecordRepository operatorFileRecordRepository
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
        _operatorFileBlobContainer = operatorFileBlobContainer;
        _operatorFileRecordRepository = operatorFileRecordRepository;
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

        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.CreationTime)
        );

        List<WorkflowProjectTask> tasks = await AsyncExecuter.ToListAsync(
            (await _taskRepository.GetQueryableAsync()).Where(x => x.ProjectId == projectId)
        );

        Dictionary<Guid, WorkflowProjectTask> taskMap = tasks
            .GroupBy(x => x.WorkflowId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreationTime).First());

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
            Items = workflows
                .Select(x =>
                    taskMap.TryGetValue(x.Id, out WorkflowProjectTask? task)
                        ? MapTaskDto(task, x.Name)
                        : new WorkflowProjectTaskDto
                        {
                            Id = Guid.Empty,
                            ProjectId = projectId,
                            WorkflowId = x.Id,
                            WorkflowName = x.Name,
                            IsEnabled = false,
                            OrderNo = 0,
                        }
                )
                .OrderBy(x => x.OrderNo)
                .ThenBy(x => x.WorkflowName)
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
    public async Task<WorkflowProjectDeploymentDto> GetProjectDeploymentsAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        WorkflowProjectDeployment? deployment = await AsyncExecuter.FirstOrDefaultAsync(
            (await _deploymentRepository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId)
                .OrderByDescending(x => x.Revision)
        );

        if (deployment is null)
        {
            throw new UserFriendlyException("当前项目尚未创建部署快照。");
        }

        return MapDeploymentDto(deployment);
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
            (await _variableDefinitionRepository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.OwnerWorkflowId)
                .ThenBy(x => x.Name)
                .ThenBy(x => x.TypeName)
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

        WorkflowProjectDeployment? existingDeployment;
        using (DataFilter.Disable<ISoftDelete>())
        {
            existingDeployment = await AsyncExecuter.FirstOrDefaultAsync(
                (await _deploymentRepository.GetQueryableAsync())
                    .Where(x => x.ProjectId == projectId)
                    .OrderByDescending(x => x.Revision)
            );
        }

        if (existingDeployment is not null)
        {
            bool restored = false;
            if (existingDeployment.IsDeleted)
            {
                existingDeployment.RestoreFromDeleted();
                restored = true;
            }

            bool changed = existingDeployment.UpdatePublishedContent(
                items,
                frozenGraphs,
                frozenVariables,
                snapshotHash
            );
            if (changed || restored)
            {
                await _deploymentRepository.UpdateAsync(existingDeployment, autoSave: true);
            }

            return MapDeploymentDto(existingDeployment);
        }

        WorkflowProjectDeployment deployment = WorkflowProjectDeployment.CreatePublished(
            GuidGenerator.Create(),
            projectId,
            1,
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
    [HttpDelete("deployments/{deploymentId:guid}")]
    public async Task DeleteProjectDeploymentAsync(Guid deploymentId)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new UserFriendlyException("DeploymentId 不能为空。");
        }

        WorkflowProjectDeployment deployment = await _deploymentRepository.GetAsync(deploymentId);
        if (deployment.Status == WorkflowProjectDeploymentStatus.Activated)
        {
            throw new UserFriendlyException("当前激活部署不允许删除，请先激活其他部署。 ");
        }

        bool hasActiveRunReference = await AsyncExecuter.AnyAsync(
            (await _runRepository.GetQueryableAsync()).Where(x =>
                x.DeploymentId == deploymentId
                && (
                    x.Status == WorkflowProjectRunStatus.Queued
                    || x.Status == WorkflowProjectRunStatus.Running
                )
            )
        );
        if (hasActiveRunReference)
        {
            throw new UserFriendlyException("该部署正在被运行中的任务使用，暂不允许删除。");
        }

        await _deploymentRepository.DeleteAsync(deployment, autoSave: true);
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
    [DisableValidation]
    public async Task<WorkflowExecutionTriggerResultDto> ExecuteAsync(
        WorkflowExecutionTriggerInput input
    )
    {
        if (input is null)
        {
            return CreateExecutionTriggerErrorResult("执行参数不能为空。");
        }

        try
        {
            ValidateExecutionInput(input);

            // 项目级调试互斥：单机一次只能调试一个项目（固定调试任务）。
            if (input.Mode == WorkflowExecutionMode.DebugStep)
            {
                _sessionStore.EnsureExclusiveDebugProject(input.ProjectId);
            }

            WorkflowExecutionBootstrap bootstrap = await PrepareBootstrapAsync(input);
            await PreheatRunDependenciesAsync(input, bootstrap);
            bootstrap = await MaterializeUploadedFileReferencesAsync(bootstrap);

            return input.Mode switch
            {
                WorkflowExecutionMode.DebugStep => await StartDebugSessionAsync(input, bootstrap),
                WorkflowExecutionMode.Loop => await RunLoopModeAsync(input, bootstrap),
                _ => await RunOnceAsync(input, bootstrap),
            };
        }
        catch (Exception ex)
        {
            return CreateExecutionTriggerErrorResult(ex.Message, ExecutionErrorCodeUnhandled);
        }
    }

    /// <inheritdoc/>
    [HttpPost("roi-base-image")]
    [DisableValidation]
    public async Task<RoiBaseImageResultDto> GenerateRoiBaseImageAsync(
        GenerateRoiBaseImageInput input
    )
    {
        if (input is null || input.ProjectId == Guid.Empty)
        {
            return RoiBaseImageError("ProjectId 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.RoiNodeId))
        {
            return RoiBaseImageError("RoiNodeId 不能为空。");
        }

        try
        {
            // 1. 取得 graphData：优先当前画布，其次已保存工作流。
            //    如果提供了 WorkflowId，总是加载已保存实体，以便读取已缓存的底图信息。
            WorkflowDefinition? savedEntity = null;
            string graphData = input.GraphData ?? string.Empty;
            if (input.WorkflowId != Guid.Empty)
            {
                savedEntity = await _repository.GetAsync(input.WorkflowId);
                if (savedEntity.ProjectId != input.ProjectId)
                {
                    return RoiBaseImageError("工作流不存在或不属于该项目。");
                }

                // 如果前端没传 GraphData，用已保存的；否则用前端传的（前端画布可能有未保存修改）
                if (string.IsNullOrWhiteSpace(graphData))
                {
                    graphData = savedEntity.GraphData;
                }
            }
            else if (string.IsNullOrWhiteSpace(graphData))
            {
                return RoiBaseImageError("GraphData 与 WorkflowId 至少提供一个。");
            }

            // 2. 解析 + 编译整图。
            string workflowName;
            GraphDataModel graph;
            RuntimeWorkflowDefinition compiled;
            try
            {
                (workflowName, graph) = WorkflowGraphCompiler.ParseContent(graphData);
                compiled = await new WorkflowGraphCompiler(_registry).CompileAsync(
                    graph,
                    workflowName
                );
            }
            catch (WorkflowCompilationException ex)
            {
                return RoiBaseImageError("工作流编译失败：" + ex.Message);
            }
            catch (JsonException ex)
            {
                return RoiBaseImageError("工作流内容 JSON 解析失败：" + ex.Message);
            }

            // 3. 定位 ROI 节点与其 input_mat 绑定变量。
            NodeModel? roiNode = graph.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, input.RoiNodeId, StringComparison.Ordinal)
            );
            if (roiNode is null)
            {
                return RoiBaseImageError($"未找到 ROI 节点：{input.RoiNodeId}。");
            }

            string? sourceVariable = null;
            roiNode.Properties?.InputBindings?.TryGetValue("input_mat", out sourceVariable);
            if (string.IsNullOrWhiteSpace(sourceVariable))
            {
                return RoiBaseImageError("ROI 节点未绑定 input_mat 输入。");
            }

            // 取当前 ROI 节点的 roiJson（保留已有 rois，只替换 baseImage）。
            string? currentRoiJson = null;
            if (
                roiNode.Properties?.Params is { } roiParams
                && roiParams.TryGetValue("roiJson", out JsonElement roiJsonElement)
                && roiJsonElement.ValueKind == JsonValueKind.String
            )
            {
                currentRoiJson = roiJsonElement.GetString();
            }

            // 检查是否已有有效的缓存底图：底图已生成且工作流未变化
            // 优先从已保存实体中查找缓存（前端传的 GraphData 可能不含 baseImage 字段）
            string currentGraphHash = ComputeGraphHash(graphData);
            string? cachedRoiJsonForCheck = currentRoiJson;
            if (savedEntity is not null)
            {
                // 从已保存实体中提取 roiJson，可能包含之前生成的 baseImage
                string? savedRoiJson = ExtractRoiJsonFromGraph(
                    savedEntity.GraphData,
                    input.RoiNodeId
                );
                if (!string.IsNullOrWhiteSpace(savedRoiJson))
                {
                    cachedRoiJsonForCheck = savedRoiJson;
                }
            }

            (string? cachedBlobName, string? cachedRoiJson) = ExtractCachedBaseImage(
                cachedRoiJsonForCheck,
                currentGraphHash
            );
            if (cachedBlobName is not null)
            {
                return new RoiBaseImageResultDto
                {
                    Error = false,
                    BlobName = cachedBlobName,
                    RoiJson = cachedRoiJson,
                    Persisted = savedEntity is not null,
                };
            }

            // 4. 祖先子图：仅保留 ROI 节点真正依赖的上游算子语句，天然跳过并行副作用节点。
            HashSet<string> ancestorSet = new(
                WorkflowNodeScheduleBuilder.BuildAncestorNodeOrder(graph, input.RoiNodeId),
                StringComparer.Ordinal
            );
            IReadOnlyList<string> fullOrder = WorkflowNodeScheduleBuilder.BuildExecutableNodeOrder(
                graph
            );

            List<IWorkflowStatement> ancestorStatements = new();
            List<string> ancestorNodeIds = new();
            for (int i = 0; i < compiled.Statements.Count; i++)
            {
                string? nodeId = i < fullOrder.Count ? fullOrder[i] : null;
                if (nodeId is not null && ancestorSet.Contains(nodeId))
                {
                    ancestorStatements.Add(compiled.Statements[i]);
                    ancestorNodeIds.Add(nodeId);
                }
            }

            if (ancestorStatements.Count == 0)
            {
                return RoiBaseImageError("ROI 节点没有可执行的上游算子，无法生成底图。");
            }

            // 5. 物化上传文件引用（read_point_cloud 等），并准备变量声明与初值。
            HashSet<string> uploadedFileVariableNames = new(StringComparer.Ordinal);
            IReadOnlyList<IWorkflowStatement> materialized =
                await MaterializeUploadedFileStatementsAsync(
                    ancestorStatements,
                    uploadedFileVariableNames
                );

            VariableCompileRequestDto compileRequest = await _compileRequestFactory.BuildAsync(
                input.ProjectId,
                input.WorkflowId,
                graph,
                VariableDefUseAnalysisMode.Conservative
            );
            List<VariableDeclarationDto> declarations =
                await MaterializeUploadedFileDefaultValuesAsync(
                    compileRequest.Declarations,
                    uploadedFileVariableNames
                );

            WorkflowSignature signature = WorkflowSignatureExtractor.Extract(graph);
            List<WorkflowVariableBindingKeyDto> inputBindings = BuildBindingKeys(
                input.InputVariableBindings,
                input.InputVariableKeys is { Count: > 0 } keys ? keys : signature.Inputs,
                input.WorkflowId
            );
            Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
                inputBindings
            );

            // 6. 构建并执行祖先子图会话。
            WorkflowContext context = new();
            WorkflowExecutionVariablePool variablePool = new(declarations);
            variablePool.Clear(context);
            variablePool.Initialize(context);
            variablePool.ApplyInitialValues(initialVariables);
            foreach ((string key, object? value) in initialVariables)
            {
                context.Set(key, value);
            }

            WorkflowExecutionSession session = new()
            {
                ExecutionId = GuidGenerator.Create(),
                ProjectId = input.ProjectId,
                WorkflowId = input.WorkflowId,
                WorkflowName = workflowName,
                Mode = WorkflowExecutionMode.RunOnce,
                LoopCount = 1,
                RuntimeWorkflow = new RuntimeWorkflowDefinition(workflowName, materialized),
                StatementNodeIds = ancestorNodeIds,
                Context = context,
                VariablePool = variablePool,
                OutputBindings = new List<WorkflowVariableBindingKeyDto>(),
            };

            byte[] pngBytes;
            int imageWidth;
            int imageHeight;
            string? mappingJson;
            try
            {
                using IDisposable blobStoreScope = CreateOperatorFileBlobStoreScope();
                _kernel.ExecuteToCompletion(session);

                Mat? baseMat = context.Get<Mat>(sourceVariable);
                if (baseMat is null || baseMat.Empty())
                {
                    return RoiBaseImageError(
                        $"预运行完成但变量 '{sourceVariable}' 不是有效图像，无法生成底图。"
                    );
                }

                imageWidth = baseMat.Width;
                imageHeight = baseMat.Height;
                Cv2.ImEncode(".png", baseMat, out pngBytes);

                mappingJson = ResolveProjectionMappingJson(graph, sourceVariable!, context);
            }
            catch (WorkflowNodeExecutionException ex)
            {
                return RoiBaseImageError(
                    $"预运行到 ROI 上游失败，节点 {ex.NodeId}：{ex.InnerException?.Message ?? ex.Message}",
                    ExecutionErrorCodeFault
                );
            }
            finally
            {
                session.VariablePool.Clear(session.Context);
                session.Dispose();
            }

            // 7. 保存底图 Blob 并组装结果（尺寸以真实图像为权威，图片统一用 blobName 引用）。
            WorkflowOperatorFileBlobStore blobStore = new(_operatorFileBlobContainer);
            string blobName = await blobStore.SaveImagePngAsync("roi-base-image.png", pngBytes);

            RoiBaseImageResultDto result = new() { Error = false, BlobName = blobName };

            // 8. 把底图字段（selectedBlobName + projectionMapping）写回 ROI 节点的 roiJson。
            //    projectionMapping 的 viewLabel/世界边界来自投影算子输出，尺寸以真实图像为权威。
            //    - 已保存工作流：直接持久化写回，前端重载即见字段已填。
            //    - 未保存画布：仅返回更新后的 roiJson，供前端应用到画布节点。
            AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping mapping = ResolveMappingForRoiJson(
                mappingJson,
                imageWidth,
                imageHeight
            );
            result.RoiJson = BuildUpdatedRoiJson(
                currentRoiJson,
                blobName,
                mapping,
                currentGraphHash
            );
            if (savedEntity is not null)
            {
                string? updatedGraph = ApplyRoiJsonToGraph(
                    graphData,
                    input.RoiNodeId,
                    result.RoiJson
                );
                if (updatedGraph is not null)
                {
                    savedEntity.Update(savedEntity.Name, updatedGraph);
                    await _repository.UpdateAsync(savedEntity);
                    result.Persisted = true;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            return RoiBaseImageError(ex.Message, ExecutionErrorCodeUnhandled);
        }
    }

    private static RoiBaseImageResultDto RoiBaseImageError(
        string? message,
        string errorCode = ExecutionErrorCodeInvalidInput
    ) =>
        new()
        {
            Error = true,
            ErrorCode = errorCode,
            Message = message,
        };

    private static string? ResolveProjectionMappingJson(
        GraphDataModel graph,
        string sourceVariable,
        WorkflowContext context
    )
    {
        // 优先读取"产出 ROI 输入图像"的节点显式绑定的 projection_mapping 变量；
        // 否则回退读取算子直接写入上下文的端口键 projection_mapping。
        NodeModel? producer = graph.Nodes.FirstOrDefault(n =>
            n.Properties?.OutputBindings is { } outputs
            && outputs.TryGetValue("output_image", out string? mapped)
            && string.Equals(mapped, sourceVariable, StringComparison.Ordinal)
        );

        if (
            producer?.Properties?.OutputBindings is { } producerOutputs
            && producerOutputs.TryGetValue("projection_mapping", out string? mappingVar)
            && !string.IsNullOrWhiteSpace(mappingVar)
        )
        {
            string? bound = context.Get<string>(mappingVar);
            if (!string.IsNullOrWhiteSpace(bound))
            {
                return bound;
            }
        }

        return context.Get<string>("projection_mapping");
    }

    private static AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping ResolveMappingForRoiJson(
        string? mappingJson,
        int imageWidth,
        int imageHeight
    )
    {
        AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping mapping = new() { ViewLabel = "XY" };

        if (!string.IsNullOrWhiteSpace(mappingJson))
        {
            try
            {
                AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping? parsed =
                    JsonSerializer.Deserialize<AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping>(
                        mappingJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                if (parsed is not null)
                {
                    mapping = parsed;
                }
            }
            catch (JsonException)
            {
                // 保底：映射非法则退化为仅含尺寸的默认映射。
            }
        }

        if (string.IsNullOrWhiteSpace(mapping.ViewLabel))
        {
            mapping.ViewLabel = "XY";
        }

        // 尺寸以真实渲染图像为权威。
        mapping.ImageWidth = imageWidth;
        mapping.ImageHeight = imageHeight;
        return mapping;
    }

    private static readonly JsonSerializerOptions RoiJsonWriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 根据预运行结果重建 ROI 节点的 roiJson：保留原有 rois，整体替换 baseImage
    /// 为单张底图 blobName + projectionMapping（天然去掉旧的 selectedLabel / previewImages）。
    /// </summary>
    private static string BuildUpdatedRoiJson(
        string? currentRoiJson,
        string blobName,
        AuroraStruct3D.OpenCV.RoiOps.RoiProjectionMapping mapping,
        string? graphHash = null
    )
    {
        JsonObject roiObject = new();
        if (!string.IsNullOrWhiteSpace(currentRoiJson))
        {
            try
            {
                if (JsonNode.Parse(currentRoiJson) is JsonObject parsedObject)
                {
                    roiObject = parsedObject;
                }
            }
            catch (JsonException)
            {
                // 保底：currentRoiJson 非法则用空对象重建。
            }
        }

        roiObject["baseImage"] = new JsonObject
        {
            ["selectedBlobName"] = blobName,
            ["projectionMapping"] = new JsonObject
            {
                ["viewLabel"] = string.IsNullOrWhiteSpace(mapping.ViewLabel)
                    ? "XY"
                    : mapping.ViewLabel,
                ["worldMinX"] = mapping.WorldMinX,
                ["worldMaxX"] = mapping.WorldMaxX,
                ["worldMinY"] = mapping.WorldMinY,
                ["worldMaxY"] = mapping.WorldMaxY,
                ["imageWidth"] = mapping.ImageWidth,
                ["imageHeight"] = mapping.ImageHeight,
            },
            ["graphHash"] = graphHash,
        };

        return roiObject.ToJsonString(RoiJsonWriteOptions);
    }

    private static (string? BlobName, string? RoiJson) ExtractCachedBaseImage(
        string? roiJson,
        string currentGraphHash
    )
    {
        if (string.IsNullOrWhiteSpace(roiJson))
        {
            return (null, null);
        }

        try
        {
            if (JsonNode.Parse(roiJson) is not JsonObject roiObject)
            {
                return (null, null);
            }

            if (roiObject["baseImage"] is not JsonObject baseImageObject)
            {
                return (null, null);
            }

            if (
                baseImageObject["selectedBlobName"] is not JsonValue blobNameValue
                || string.IsNullOrWhiteSpace(blobNameValue.ToString())
            )
            {
                return (null, null);
            }

            string cachedBlobName = blobNameValue.ToString()!;
            string? cachedGraphHash = baseImageObject["graphHash"]?.ToString();

            if (string.IsNullOrWhiteSpace(cachedGraphHash))
            {
                return (null, null);
            }

            if (!string.Equals(cachedGraphHash, currentGraphHash, StringComparison.Ordinal))
            {
                return (null, null);
            }

            return (cachedBlobName, roiJson);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// 将更新后的 roiJson 写回 graphData 中指定 ROI 节点的 <c>params.roiJson</c>，
    /// 保留其余结构不变。解析失败或未命中节点时返回 null。
    /// </summary>
    private static string? ApplyRoiJsonToGraph(
        string graphDataJson,
        string roiNodeId,
        string updatedRoiJson
    )
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(graphDataJson);
        }
        catch (JsonException)
        {
            return null;
        }

        JsonArray? nodes = root?["nodes"]?.AsArray();
        if (nodes is null)
        {
            return null;
        }

        foreach (JsonNode? nodeNode in nodes)
        {
            if (nodeNode is null)
            {
                continue;
            }

            if (!string.Equals((string?)nodeNode["id"], roiNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            JsonObject? properties = nodeNode["properties"]?.AsObject();
            if (properties is null)
            {
                properties = new JsonObject();
                nodeNode["properties"] = properties;
            }

            JsonObject? paramsObject = properties["params"]?.AsObject();
            if (paramsObject is null)
            {
                paramsObject = new JsonObject();
                properties["params"] = paramsObject;
            }

            paramsObject["roiJson"] = updatedRoiJson;
            return root!.ToJsonString(RoiJsonWriteOptions);
        }

        return null;
    }

    /// <summary>
    /// 从 graphData JSON 中提取指定 ROI 节点的 <c>params.roiJson</c> 值。
    /// 解析失败或未命中节点时返回 null。
    /// </summary>
    private static string? ExtractRoiJsonFromGraph(string graphDataJson, string roiNodeId)
    {
        try
        {
            if (JsonNode.Parse(graphDataJson) is not JsonObject root)
            {
                return null;
            }

            if (root["nodes"] is not JsonArray nodes)
            {
                return null;
            }

            foreach (JsonNode? nodeNode in nodes)
            {
                if (nodeNode is null)
                {
                    continue;
                }

                if (!string.Equals((string?)nodeNode["id"], roiNodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (
                    nodeNode["properties"] is JsonObject properties
                    && properties["params"] is JsonObject paramsObject
                    && paramsObject["roiJson"] is JsonValue roiJsonValue
                    && roiJsonValue.GetValue<string>() is { } roiJson
                )
                {
                    return roiJson;
                }

                return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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

        // 校验输出变量配置（冻结执行路径绕过实体加载，需在此单独校验）
        WorkflowDefinition entity = await _repository.GetAsync(workflowId);
        ValidateSingleWorkflowOutputConfig(entity);

        WorkflowExecutionBootstrap bootstrap = await PrepareBootstrapAsync(
            input,
            frozenGraphData,
            skipOfflineValidation: true
        );
        bootstrap = await MaterializeUploadedFileReferencesAsync(bootstrap);
        return await RunOnceAsync(input, bootstrap);
    }

    /// <inheritdoc/>
    [HttpPost("executions/{executionId:guid}/steps")]
    [DisableValidation]
    public async Task<WorkflowExecutionStepResultDto> StepAsync(
        Guid executionId,
        WorkflowExecutionStepInput input
    )
    {
        if (input is null)
        {
            return CreateExecutionStepErrorResult(executionId, "步进参数不能为空。");
        }

        try
        {
            ValidateStepInput(executionId, input);

            WorkflowExecutionSession session = _sessionStore.Get(executionId);
            if (session.Mode != WorkflowExecutionMode.DebugStep)
            {
                return CreateExecutionStepErrorResult(executionId, "仅调试模式支持步进。");
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
                        Error = false,
                        Message = session.ErrorMessage,
                        ExecutionId = executionId,
                        Status = BuildStatusDto(session, input.IncludeVariables),
                    };
                }

                try
                {
                    using IDisposable blobStoreScope = CreateOperatorFileBlobStoreScope();
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
                    Error = false,
                    Message = session.ErrorMessage,
                    ExecutionId = executionId,
                    Status = BuildStatusDto(session, input.IncludeVariables),
                };
            }
            finally
            {
                session.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            return CreateExecutionStepErrorResult(executionId, ex.Message);
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
        Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
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
            Error = false,
            Message = session.ErrorMessage,
            ExecutionId = session.ExecutionId,
            ResultImageUrls = ResolveResultImageUrls(session),
            Variables = await ResolveOutputVariables(session),
            Status = BuildStatusDto(session, includeVariables: true),
        };
    }

    private async Task<WorkflowExecutionTriggerResultDto> RunOnceAsync(
        WorkflowExecutionTriggerInput input,
        WorkflowExecutionBootstrap bootstrap
    )
    {
        Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
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
            using IDisposable blobStoreScope = CreateOperatorFileBlobStoreScope();
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
            return await CreateExecutionTriggerFaultResult(session);
        }
        finally
        {
            session.VariablePool.Clear(session.Context);
            session.Dispose();
        }

        return new WorkflowExecutionTriggerResultDto
        {
            Error = false,
            Message = session.ErrorMessage,
            ExecutionId = session.ExecutionId,
            ResultImageUrls = ResolveResultImageUrls(session),
            Variables = await ResolveOutputVariables(session),
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
            Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
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
                using IDisposable blobStoreScope = CreateOperatorFileBlobStoreScope();
                _kernel.ExecuteToCompletion(session);
                finalVariables = CollectDeclaredVariables(session);
                lastSession = session;
            }
            catch (WorkflowNodeExecutionException ex)
            {
                session.MarkFaulted(ex.NodeId, ex.InnerException?.Message ?? ex.Message);
                session.FrozenVariables = session.VariablePool.Snapshot(session.Context);
                return await CreateLoopExecutionFaultResult(session, i + 1);
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
            Error = false,
            Message = resultSession.ErrorMessage,
            ExecutionId = resultSession.ExecutionId,
            ResultImageUrls = ResolveResultImageUrls(resultSession),
            Variables = await ResolveOutputVariables(resultSession),
            Status = BuildStatusDto(resultSession, includeVariables: true),
        };
    }

    /// <summary>
    /// 校验方案内所有工作流是否已配置输出变量。
    /// 任一工作流未配置则抛出异常，阻止执行。
    /// </summary>
    private async Task ValidateOutputConfigForRunAsync(List<Guid> workflowIds)
    {
        foreach (Guid id in workflowIds)
        {
            WorkflowDefinition entity = await _repository.GetAsync(id);
            if (string.IsNullOrWhiteSpace(entity.OutputVariables))
            {
                throw new UserFriendlyException(
                    $"工作流「{entity.Name}」尚未配置输出变量，请先配置后再运行。"
                );
            }
        }
    }

    /// <summary>
    /// 调试模式：校验单个工作流是否已配置输出变量。
    /// </summary>
    private static void ValidateSingleWorkflowOutputConfig(WorkflowDefinition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.OutputVariables))
        {
            throw new UserFriendlyException(
                $"工作流「{entity.Name}」尚未配置输出变量，请先配置后再运行。"
            );
        }
    }

    private static void ValidateExecutionInput(WorkflowExecutionTriggerInput input)
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        if (!Enum.IsDefined(typeof(WorkflowExecutionMode), input.Mode))
        {
            throw new UserFriendlyException("Mode 参数不合法。");
        }

        if (input.LoopCount <= 0)
        {
            throw new UserFriendlyException("LoopCount 必须大于 0。");
        }
    }

    private static void ValidateStepInput(Guid executionId, WorkflowExecutionStepInput input)
    {
        if (executionId == Guid.Empty)
        {
            throw new UserFriendlyException("ExecutionId 不能为空。");
        }

        if (input.Steps <= 0)
        {
            throw new UserFriendlyException("Steps 必须大于 0。");
        }
    }

    private static WorkflowExecutionTriggerResultDto CreateExecutionTriggerErrorResult(
        string? message,
        string errorCode = ExecutionErrorCodeInvalidInput
    )
    {
        return new WorkflowExecutionTriggerResultDto
        {
            Error = true,
            ErrorCode = errorCode,
            Message = message,
        };
    }

    private static WorkflowExecutionStepResultDto CreateExecutionStepErrorResult(
        Guid executionId,
        string? message,
        string errorCode = ExecutionErrorCodeStepInvalidInput
    )
    {
        return new WorkflowExecutionStepResultDto
        {
            Error = true,
            ErrorCode = errorCode,
            Message = message,
            ExecutionId = executionId,
        };
    }

    private async Task<WorkflowExecutionTriggerResultDto> CreateExecutionTriggerFaultResult(
        WorkflowExecutionSession session
    )
    {
        string nodeId = session.FaultNodeId ?? "<unknown>";
        string message = $"工作流执行失败，节点 {nodeId}：{session.ErrorMessage}";
        return new WorkflowExecutionTriggerResultDto
        {
            Error = true,
            ErrorCode = ExecutionErrorCodeFault,
            Message = message,
            ExecutionId = session.ExecutionId,
            ResultImageUrls = ResolveResultImageUrls(session),
            Variables = await ResolveOutputVariables(session),
            Status = BuildStatusDtoStatic(session, includeVariables: true),
        };
    }

    private async Task<WorkflowExecutionTriggerResultDto> CreateLoopExecutionFaultResult(
        WorkflowExecutionSession session,
        int loopIndex
    )
    {
        string nodeId = session.FaultNodeId ?? "<unknown>";
        string message = $"循环执行第 {loopIndex} 次失败，节点 {nodeId}：{session.ErrorMessage}";
        return new WorkflowExecutionTriggerResultDto
        {
            Error = true,
            ErrorCode = ExecutionErrorCodeLoopFault,
            Message = message,
            ExecutionId = session.ExecutionId,
            ResultImageUrls = ResolveResultImageUrls(session),
            Variables = await ResolveOutputVariables(session),
            Status = BuildStatusDtoStatic(session, includeVariables: true),
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

            // 校验方案内所有工作流是否已配置输出变量
            await ValidateOutputConfigForRunAsync(runWorkflowIds);

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

            // 校验单个工作流是否已配置输出变量
            ValidateSingleWorkflowOutputConfig(entity);
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

    private async Task<WorkflowExecutionBootstrap> MaterializeUploadedFileReferencesAsync(
        WorkflowExecutionBootstrap bootstrap
    )
    {
        HashSet<string> uploadedFileVariableNames = new(StringComparer.Ordinal);
        IReadOnlyList<IWorkflowStatement> statements = await MaterializeUploadedFileStatementsAsync(
            bootstrap.RuntimeWorkflow.Statements,
            uploadedFileVariableNames
        );
        List<VariableDeclarationDto> declarations = await MaterializeUploadedFileDefaultValuesAsync(
            bootstrap.Declarations,
            uploadedFileVariableNames
        );

        bool workflowChanged = !ReferenceEquals(statements, bootstrap.RuntimeWorkflow.Statements);
        bool declarationsChanged = !ReferenceEquals(declarations, bootstrap.Declarations);
        if (!workflowChanged && !declarationsChanged)
        {
            return bootstrap;
        }

        return new WorkflowExecutionBootstrap
        {
            ProjectId = bootstrap.ProjectId,
            RunId = bootstrap.RunId,
            WorkflowId = bootstrap.WorkflowId,
            WorkflowName = bootstrap.WorkflowName,
            RuntimeWorkflow = workflowChanged
                ? new RuntimeWorkflowDefinition(bootstrap.RuntimeWorkflow.Name, statements)
                : bootstrap.RuntimeWorkflow,
            StatementNodeIds = bootstrap.StatementNodeIds,
            Declarations = declarations,
            InputBindings = bootstrap.InputBindings,
            OutputBindings = bootstrap.OutputBindings,
        };
    }

    private async Task<IReadOnlyList<IWorkflowStatement>> MaterializeUploadedFileStatementsAsync(
        IReadOnlyList<IWorkflowStatement> statements,
        HashSet<string> uploadedFileVariableNames
    )
    {
        bool changed = false;
        List<IWorkflowStatement> rewritten = new(statements.Count);

        foreach (IWorkflowStatement statement in statements)
        {
            IWorkflowStatement next = await MaterializeUploadedFileStatementAsync(
                statement,
                uploadedFileVariableNames
            );
            changed |= !ReferenceEquals(next, statement);
            rewritten.Add(next);
        }

        return changed ? rewritten.AsReadOnly() : statements;
    }

    private async Task<IWorkflowStatement> MaterializeUploadedFileStatementAsync(
        IWorkflowStatement statement,
        HashSet<string> uploadedFileVariableNames
    )
    {
        switch (statement)
        {
            case OperatorCallStatement operatorCall:
                return await MaterializeUploadedFileOperatorCallAsync(
                    operatorCall,
                    uploadedFileVariableNames
                );
            case ForLoopStatement forLoop:
            {
                IReadOnlyList<IWorkflowStatement> body =
                    await MaterializeUploadedFileStatementsAsync(
                        forLoop.Body,
                        uploadedFileVariableNames
                    );
                return ReferenceEquals(body, forLoop.Body)
                    ? forLoop
                    : new ForLoopStatement(
                        forLoop.VariableName,
                        forLoop.From,
                        forLoop.To,
                        forLoop.Step,
                        body
                    );
            }
            case IfElseStatement ifElse:
            {
                IReadOnlyList<IWorkflowStatement> thenBody =
                    await MaterializeUploadedFileStatementsAsync(
                        ifElse.ThenBody,
                        uploadedFileVariableNames
                    );
                IReadOnlyList<IWorkflowStatement> elseBody =
                    await MaterializeUploadedFileStatementsAsync(
                        ifElse.ElseBody,
                        uploadedFileVariableNames
                    );
                bool unchanged =
                    ReferenceEquals(thenBody, ifElse.ThenBody)
                    && ReferenceEquals(elseBody, ifElse.ElseBody);
                return unchanged
                    ? ifElse
                    : new IfElseStatement(ifElse.Condition, thenBody, elseBody);
            }
            default:
                return statement;
        }
    }

    private async Task<IWorkflowStatement> MaterializeUploadedFileOperatorCallAsync(
        OperatorCallStatement operatorCall,
        HashSet<string> uploadedFileVariableNames
    )
    {
        HashSet<string> uploadPortNames = GetUploadedFileInputPortNames(operatorCall.OperatorType);
        if (uploadPortNames.Count == 0)
        {
            return operatorCall;
        }

        bool changed = false;
        Dictionary<string, InputBinding> rewrittenBindings = new(StringComparer.Ordinal);

        foreach ((string portName, InputBinding binding) in operatorCall.InputBindings)
        {
            if (!uploadPortNames.Contains(portName))
            {
                rewrittenBindings[portName] = binding;
                continue;
            }

            switch (binding)
            {
                case VariableRefBinding variableRefBinding:
                    uploadedFileVariableNames.Add(variableRefBinding.VariableName);
                    rewrittenBindings[portName] = binding;
                    break;
                case ConstantBinding { Value: string rawValue }:
                {
                    object? resolved = await ResolveUploadedFilePathAsync(rawValue);
                    if (
                        resolved is string resolvedText
                        && !string.Equals(resolvedText, rawValue, StringComparison.Ordinal)
                    )
                    {
                        rewrittenBindings[portName] = new ConstantBinding(resolvedText);
                        changed = true;
                    }
                    else
                    {
                        rewrittenBindings[portName] = binding;
                    }

                    break;
                }
                default:
                    rewrittenBindings[portName] = binding;
                    break;
            }
        }

        return !changed
            ? operatorCall
            : new OperatorCallStatement(
                operatorCall.OperatorType,
                operatorCall.ConfigArgBindings,
                rewrittenBindings,
                operatorCall.OutputBindings
            );
    }

    private async Task<List<VariableDeclarationDto>> MaterializeUploadedFileDefaultValuesAsync(
        List<VariableDeclarationDto> declarations,
        HashSet<string> uploadedFileVariableNames
    )
    {
        if (uploadedFileVariableNames.Count == 0)
        {
            return declarations;
        }

        bool changed = false;
        List<VariableDeclarationDto> rewritten = new(declarations.Count);

        foreach (VariableDeclarationDto declaration in declarations)
        {
            if (
                !uploadedFileVariableNames.Contains(declaration.Name)
                || !TryReadDefaultStringValue(declaration.DefaultValueJson, out string defaultValue)
            )
            {
                rewritten.Add(declaration);
                continue;
            }

            object? resolved = await ResolveUploadedFilePathAsync(defaultValue);
            if (
                resolved is not string resolvedText
                || string.Equals(resolvedText, defaultValue, StringComparison.Ordinal)
            )
            {
                rewritten.Add(declaration);
                continue;
            }

            rewritten.Add(
                new VariableDeclarationDto
                {
                    Name = declaration.Name,
                    TypeName = declaration.TypeName,
                    Visibility = declaration.Visibility,
                    Mutability = declaration.Mutability,
                    IsRequiredInit = declaration.IsRequiredInit,
                    DefaultValueJson = JsonSerializer.Serialize(resolvedText),
                }
            );
            changed = true;
        }

        return changed ? rewritten : declarations;
    }

    private static HashSet<string> GetUploadedFileInputPortNames(Type operatorType)
    {
        PropertyInfo? property = operatorType.GetProperty(
            "InputVisionParameters",
            BindingFlags.Public | BindingFlags.Static
        );
        if (property?.GetValue(null) is not List<IVisionParameter> parameters)
        {
            return [];
        }

        return parameters
            .Where(x =>
                x.ParameterName is not null
                && (
                    x.ControlType == PortControlType.ImageUpload
                    || x.ControlType == PortControlType.PointCloudUpload
                )
            )
            .Select(x => x.ParameterName!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryReadDefaultStringValue(string? defaultValueJson, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(defaultValueJson))
        {
            return false;
        }

        try
        {
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(defaultValueJson);
            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
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

    private async Task<Dictionary<string, object?>> LoadInitialVariablesAsync(
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
                if (value is string text)
                {
                    result[binding.VariableName] = await ResolveUploadedFilePathAsync(text);
                }
                else
                {
                    result[binding.VariableName] = value;
                }
            }
        }

        return result;
    }

    private async Task<object?> ResolveUploadedFilePathAsync(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        string trimmed = rawValue.Trim();
        if (File.Exists(trimmed))
        {
            return trimmed;
        }

        List<string> blobNameCandidates = BuildBlobNameCandidates(trimmed);

        foreach (string blobName in blobNameCandidates)
        {
            if (
                blobName.Contains("../", StringComparison.Ordinal)
                || blobName.Contains("..\\", StringComparison.Ordinal)
            )
            {
                continue;
            }

            string extension = Path.GetExtension(blobName);
            if (!UploadedFileExtensions.Contains(extension))
            {
                continue;
            }

            string cacheRoot = Path.Combine(Path.GetTempPath(), "aurora-operator-files-cache");
            Directory.CreateDirectory(cacheRoot);

            string hash = Convert
                .ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(blobName)))
                .ToLowerInvariant();
            string localPath = Path.Combine(cacheRoot, $"{hash}{extension.ToLowerInvariant()}");

            if (File.Exists(localPath))
            {
                return localPath;
            }

            Stream? blobStream = await _operatorFileBlobContainer.GetAsync(blobName);
            if (blobStream is null)
            {
                continue;
            }

            await using (blobStream)
            await using (
                FileStream fileStream = new(
                    localPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous
                )
            )
            {
                await blobStream.CopyToAsync(fileStream);
            }

            await MarkFileAsUsedAsync(blobName);

            return localPath;
        }

        if (blobNameCandidates.Count > 0)
        {
            throw new UserFriendlyException("上传文件不存在或已过期，请重新上传并确认后再执行。");
        }

        return rawValue;
    }

    private async Task MarkFileAsUsedAsync(string blobName)
    {
        try
        {
            OperatorFileRecord? record = await _operatorFileRecordRepository.FindAsync(r =>
                r.BlobName == blobName
            );

            if (record is not null && !record.IsUsed)
            {
                record.MarkAsUsed();
                await _operatorFileRecordRepository.UpdateAsync(record);
            }
        }
        catch (Exception) { }
    }

    private async Task<string?> ResolveExistingBlobNameAsync(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        foreach (string blobName in BuildBlobNameCandidates(candidate))
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                continue;
            }

            Stream? blobStream = await _operatorFileBlobContainer.GetAsync(blobName);
            if (blobStream is null)
            {
                continue;
            }

            await blobStream.DisposeAsync();
            return blobName;
        }

        return null;
    }

    private static List<string> BuildBlobNameCandidates(string rawValue)
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in ExtractBlobNameCandidatesFromText(rawValue))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidates.Add(candidate.Replace('\\', '/').TrimStart('/'));
            }
        }

        return candidates.ToList();
    }

    private IDisposable CreateOperatorFileBlobStoreScope()
    {
        return OperatorFileBlobStoreAmbient.Push(
            new WorkflowOperatorFileBlobStore(_operatorFileBlobContainer)
        );
    }

    private static IEnumerable<string> ExtractBlobNameCandidatesFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        string trimmed = text.Trim();

        // 支持从 URL Query 提取 blobName 参数：...blobName=a1b2.../file.ply
        int blobNameIndex = trimmed.IndexOf("blobName=", StringComparison.OrdinalIgnoreCase);
        if (blobNameIndex >= 0)
        {
            string encoded = trimmed[(blobNameIndex + "blobName=".Length)..];
            int ampIndex = encoded.IndexOf('&');
            if (ampIndex >= 0)
            {
                encoded = encoded[..ampIndex];
            }

            if (!string.IsNullOrWhiteSpace(encoded))
            {
                yield return DecodeUrlComponent(encoded);
            }
        }

        // 支持从绝对路径或 URL Path 提取末尾 blobName：{operatorId}/{fileName}
        string normalized = trimmed.Replace('\\', '/');
        Match tailMatch = Regex.Match(
            normalized,
            @"((?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})/[^/?#]+\.[A-Za-z0-9]+)$",
            RegexOptions.CultureInvariant
        );
        if (tailMatch.Success)
        {
            yield return tailMatch.Groups[1].Value;
        }

        // 支持带容器名前缀：operator-files/a1b2.../file.ply
        int containerIndex = normalized.IndexOf(
            "operator-files/",
            StringComparison.OrdinalIgnoreCase
        );
        if (containerIndex >= 0)
        {
            string afterContainer = normalized[(containerIndex + "operator-files/".Length)..];
            if (!string.IsNullOrWhiteSpace(afterContainer))
            {
                yield return afterContainer;
            }
        }

        // 支持前端直接传 blobName（不带协议，不带容器前缀）。
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            yield return trimmed;
        }
    }

    private static string DecodeUrlComponent(string encoded)
    {
        string decoded = encoded;
        for (int i = 0; i < 2; i++)
        {
            string next = Uri.UnescapeDataString(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
            {
                break;
            }

            decoded = next;
        }

        return decoded;
    }

    private WorkflowExecutionStatusDto BuildStatusDto(
        WorkflowExecutionSession session,
        bool includeVariables
    ) => BuildStatusDtoStatic(session, includeVariables);

    private static List<string> ResolveResultImageUrls(WorkflowExecutionSession session)
    {
        HashSet<string> variableNames = new(StringComparer.Ordinal);
        CollectResultImageUrlVariables(session.RuntimeWorkflow.Statements, variableNames);
        if (variableNames.Count == 0)
        {
            return new List<string>();
        }

        List<WorkflowVariableResultDto> variables;
        if (
            session.Status
            is WorkflowExecutionStatus.Completed
                or WorkflowExecutionStatus.Faulted
                or WorkflowExecutionStatus.Stopped
        )
        {
            variables = session.FrozenVariables;
        }
        else
        {
            variables = session.VariablePool.Snapshot(session.Context, session.OutputStagedKeys);
        }

        Dictionary<string, string> scalarMap = variables
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.ScalarValue)
            )
            .ToDictionary(x => x.Name, x => x.ScalarValue!, StringComparer.Ordinal);

        List<string> result = new();
        foreach (string variableName in variableNames)
        {
            if (
                scalarMap.TryGetValue(variableName, out string? value)
                && !string.IsNullOrWhiteSpace(value)
            )
            {
                result.Add(value);
            }
        }

        return result;
    }

    private async Task<List<WorkflowVariableResultDto>> ResolveOutputVariables(
        WorkflowExecutionSession session
    )
    {
        WorkflowDefinition entity = await _repository.GetAsync(session.WorkflowId);
        List<string>? outputVariableNames = ParseOutputVariablesJson(entity.OutputVariables);

        if (outputVariableNames == null || outputVariableNames.Count == 0)
        {
            return new List<WorkflowVariableResultDto>();
        }

        List<WorkflowVariableResultDto> allVariables = session.Status
            is WorkflowExecutionStatus.Completed
                or WorkflowExecutionStatus.Faulted
                or WorkflowExecutionStatus.Stopped
            ? session.FrozenVariables
            : session.VariablePool.Snapshot(session.Context, session.OutputStagedKeys);

        HashSet<string> targetNames = new(outputVariableNames, StringComparer.Ordinal);
        List<WorkflowVariableResultDto> result = new();

        foreach (WorkflowVariableResultDto variable in allVariables)
        {
            if (!targetNames.Contains(variable.Name))
            {
                continue;
            }

            if (
                variable.Name.StartsWith("abs_diff_", StringComparison.Ordinal)
                && variable.ScalarValue != null
            )
            {
                try
                {
                    JsonNode? jsonNode = JsonNode.Parse(variable.ScalarValue);
                    if (jsonNode != null && jsonNode is JsonObject jsonObj)
                    {
                        string? targetRegion = jsonObj["targetRegion"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(targetRegion))
                        {
                            variable.DisplayName = targetRegion;
                        }
                    }
                }
                catch (JsonException) { }
            }

            result.Add(variable);
        }

        return result;
    }

    private static List<string>? ParseOutputVariablesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectResultImageUrlVariables(
        IReadOnlyList<IWorkflowStatement> statements,
        HashSet<string> variableNames
    )
    {
        foreach (IWorkflowStatement statement in statements)
        {
            switch (statement)
            {
                case OperatorCallStatement operatorCall:
                    if (operatorCall.OperatorType != typeof(save_image_to_blob))
                    {
                        break;
                    }

                    if (
                        operatorCall.OutputBindings.TryGetValue(
                            "download_url",
                            out OutputBinding? outputBinding
                        )
                    )
                    {
                        if (!string.IsNullOrWhiteSpace(outputBinding.VariableName))
                        {
                            variableNames.Add(outputBinding.VariableName);
                        }
                    }

                    break;
                case ForLoopStatement forLoop:
                    CollectResultImageUrlVariables(forLoop.Body, variableNames);
                    break;
                case IfElseStatement ifElse:
                    CollectResultImageUrlVariables(ifElse.ThenBody, variableNames);
                    CollectResultImageUrlVariables(ifElse.ElseBody, variableNames);
                    break;
            }
        }
    }

    private static WorkflowExecutionStatusDto BuildStatusDtoStatic(
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
        // 规范化：剔除 roiJson 中的 baseImage 字段，避免生成的底图信息影响哈希值
        string normalized = NormalizeGraphDataForHash(graphData);
        byte[] bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 规范化 graphData，剔除所有 roiJson 参数中的 <c>baseImage</c> 字段，
    /// 确保哈希值只取决于工作流结构与 ROI 配置，不受已生成底图信息影响。
    /// </summary>
    private static string NormalizeGraphDataForHash(string graphData)
    {
        try
        {
            if (JsonNode.Parse(graphData) is not JsonObject graphObject)
            {
                return graphData;
            }

            if (graphObject["nodes"] is not JsonArray nodes)
            {
                return graphData;
            }

            foreach (JsonNode? node in nodes)
            {
                if (node is not JsonObject nodeObject)
                {
                    continue;
                }

                if (
                    nodeObject["properties"] is not JsonObject properties
                    || properties["params"] is not JsonObject paramsObj
                    || !paramsObj.TryGetPropertyValue("roiJson", out JsonNode? roiJsonNode)
                    || roiJsonNode is not JsonValue roiJsonValue
                    || roiJsonValue.GetValue<string>() is not { } roiJsonStr
                )
                {
                    continue;
                }

                try
                {
                    if (JsonNode.Parse(roiJsonStr) is JsonObject roiObject)
                    {
                        roiObject.Remove("baseImage");
                        paramsObj["roiJson"] = roiObject.ToJsonString(RoiJsonWriteOptions);
                    }
                }
                catch (JsonException)
                {
                    // roiJson 解析失败，保持原样
                }
            }

            return graphObject.ToJsonString(RoiJsonWriteOptions);
        }
        catch (JsonException)
        {
            return graphData;
        }
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
