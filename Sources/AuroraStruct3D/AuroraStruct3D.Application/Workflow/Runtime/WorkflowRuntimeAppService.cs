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
    private readonly IOnlineVariablePoolAppService _onlineVariablePool;
    private readonly IOfflineVariableLibraryAppService _offlineVariableLibrary;
    private readonly WorkflowVariableCompileRequestFactory _compileRequestFactory;
    private readonly WorkflowExecutionKernel _kernel;
    private readonly IWorkflowDebugSessionStore _sessionStore;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRepository<WorkflowProjectBinding, Guid> _bindingRepository;
    private readonly IRepository<WorkflowProjectDeployment, Guid> _deploymentRepository;
    private readonly IRepository<WorkflowProjectTask, Guid> _taskRepository;

    /// <summary>
    /// 初始化运行时服务。
    /// </summary>
    public WorkflowRuntimeAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry registry,
        IWorkflowVariableBridge bridge,
        IOnlineVariablePoolAppService onlineVariablePool,
        IOfflineVariableLibraryAppService offlineVariableLibrary,
        WorkflowVariableCompileRequestFactory compileRequestFactory,
        WorkflowExecutionKernel kernel,
        IWorkflowDebugSessionStore sessionStore,
        IBackgroundJobClient backgroundJobClient,
        IRepository<WorkflowProjectBinding, Guid> bindingRepository,
        IRepository<WorkflowProjectDeployment, Guid> deploymentRepository,
        IRepository<WorkflowProjectTask, Guid> taskRepository
    )
    {
        _repository = repository;
        _registry = registry;
        _bridge = bridge;
        _onlineVariablePool = onlineVariablePool;
        _offlineVariableLibrary = offlineVariableLibrary;
        _compileRequestFactory = compileRequestFactory;
        _kernel = kernel;
        _sessionStore = sessionStore;
        _backgroundJobClient = backgroundJobClient;
        _bindingRepository = bindingRepository;
        _deploymentRepository = deploymentRepository;
        _taskRepository = taskRepository;
    }

    /// <inheritdoc/>
    [HttpGet("projects/{projectId:guid}/bindings")]
    public async Task<List<WorkflowProjectBindingDto>> GetProjectBindingsAsync(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.CreationTime)
        );

        List<WorkflowProjectBinding> bindings = await AsyncExecuter.ToListAsync(
            (await _bindingRepository.GetQueryableAsync()).Where(x => x.ProjectId == projectId)
        );

        Dictionary<Guid, WorkflowProjectBinding> bindingMap = bindings.ToDictionary(x =>
            x.WorkflowId
        );

        return workflows
            .Select(x =>
                bindingMap.TryGetValue(x.Id, out WorkflowProjectBinding? binding)
                    ? MapBindingDto(binding, x.Name)
                    : new WorkflowProjectBindingDto
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
            .ToList();
    }

    /// <inheritdoc/>
    [HttpPut("projects/bindings")]
    public async Task<List<WorkflowProjectBindingDto>> UpdateProjectBindingsAsync(
        WorkflowProjectBindingBatchUpdateInput input
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
            throw new UserFriendlyException("至少需要一条绑定配置。");
        }

        List<Guid> workflowIds = input.Items.Select(x => x.WorkflowId).Distinct().ToList();
        if (workflowIds.Any(x => x == Guid.Empty) || workflowIds.Count != input.Items.Count)
        {
            throw new UserFriendlyException("绑定中的 WorkflowId 不能为空且不能重复。");
        }

        IQueryable<WorkflowDefinition> workflowQuery = await _repository.GetQueryableAsync();
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            workflowQuery.Where(x => x.ProjectId == input.ProjectId && workflowIds.Contains(x.Id))
        );
        if (workflows.Count != workflowIds.Count)
        {
            throw new UserFriendlyException("存在不属于当前项目的工作流，无法保存绑定配置。");
        }

        Dictionary<Guid, WorkflowProjectBinding> existingMap = (
            await AsyncExecuter.ToListAsync(
                (await _bindingRepository.GetQueryableAsync()).Where(x =>
                    x.ProjectId == input.ProjectId
                )
            )
        ).ToDictionary(x => x.WorkflowId);

        foreach (WorkflowProjectBindingUpdateItemInput item in input.Items)
        {
            if (existingMap.TryGetValue(item.WorkflowId, out WorkflowProjectBinding? binding))
            {
                binding.SetEnabled(item.IsEnabled);
                binding.SetOrderNo(item.OrderNo);
                await _bindingRepository.UpdateAsync(binding, autoSave: true);
            }
            else
            {
                WorkflowProjectBinding newBinding = WorkflowProjectBinding.Create(
                    GuidGenerator.Create(),
                    input.ProjectId,
                    item.WorkflowId,
                    item.IsEnabled,
                    item.OrderNo
                );
                await _bindingRepository.InsertAsync(newBinding, autoSave: true);
                existingMap[item.WorkflowId] = newBinding;
            }
        }

        return existingMap
            .Values.Where(x => workflowIds.Contains(x.WorkflowId))
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.CreationTime)
            .Select(x => MapBindingDto(x, workflows.First(w => w.Id == x.WorkflowId).Name))
            .ToList();
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

        List<WorkflowProjectBinding> bindings = await AsyncExecuter.ToListAsync(
            (await _bindingRepository.GetQueryableAsync())
                .Where(x => x.ProjectId == projectId && x.IsEnabled)
                .OrderBy(x => x.OrderNo)
                .ThenBy(x => x.CreationTime)
        );

        if (bindings.Count == 0)
        {
            throw new UserFriendlyException("当前项目没有已启用的工作流绑定，无法发布部署快照。");
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

        List<Guid> workflowIds = bindings.Select(x => x.WorkflowId).Distinct().ToList();
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x =>
                x.ProjectId == projectId && workflowIds.Contains(x.Id)
            )
        );

        if (workflows.Count != workflowIds.Count)
        {
            throw new UserFriendlyException("存在已绑定但已不存在的工作流，无法发布部署快照。");
        }

        Dictionary<Guid, WorkflowDefinition> workflowMap = workflows.ToDictionary(x => x.Id);
        List<WorkflowProjectDeploymentItem> items = bindings
            .Select(x => new WorkflowProjectDeploymentItem
            {
                WorkflowId = x.WorkflowId,
                WorkflowName = workflowMap[x.WorkflowId].Name,
                OrderNo = x.OrderNo,
                GraphHash = ComputeGraphHash(workflowMap[x.WorkflowId].GraphData),
            })
            .ToList();

        string snapshotHash = ComputeSnapshotHash(items);
        WorkflowProjectDeployment deployment = WorkflowProjectDeployment.CreatePublished(
            GuidGenerator.Create(),
            projectId,
            nextRevision,
            items,
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
    [HttpPost("tasks")]
    public async Task<WorkflowProjectTaskEnqueueResultDto> EnqueueProjectTaskAsync(
        WorkflowProjectTaskEnqueueInput input
    )
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId 不能为空。");
        }

        if (input.StartType != WorkflowProjectTaskStartType.Immediate)
        {
            throw new UserFriendlyException("当前仅支持 Immediate 触发启动类型。");
        }

        Guid projectId = input.ProjectId;

        (List<Guid> workflowIds, Guid? deploymentId, int? deploymentRevision) =
            await ResolveTaskWorkflowIdsAsync(projectId);

        bool continueOnError = input.OnErrorAction == WorkflowProjectTaskOnErrorAction.ContinueTask;
        Guid runtimeInstanceId = GuidGenerator.Create();
        const int variableReadTimeoutMs = 5000;

        Guid taskId = GuidGenerator.Create();
        // 项目任务固定使用在线变量池，保持跨工作流的数据一致性。
        const bool useOnlineVariablePool = true;
        WorkflowProjectTask task = WorkflowProjectTask.Create(
            taskId,
            projectId,
            input.Name.Trim(),
            input.StartType,
            deploymentId,
            deploymentRevision,
            workflowIds,
            continueOnError,
            useOnlineVariablePool,
            runtimeInstanceId,
            variableReadTimeoutMs
        );
        await _taskRepository.InsertAsync(task, autoSave: true);

        string hangfireJobId = _backgroundJobClient.Enqueue<WorkflowProjectExecutionJob>(job =>
            job.ExecuteAsync(
                new WorkflowProjectExecutionJobArgs
                {
                    TaskId = taskId,
                    ProjectId = projectId,
                    WorkflowIds = workflowIds,
                    UseOnlineVariablePool = useOnlineVariablePool,
                    RuntimeInstanceId = runtimeInstanceId,
                    VariableReadTimeoutMs = variableReadTimeoutMs,
                    ContinueOnError = continueOnError,
                }
            )
        );
        task.SetHangfireJobId(hangfireJobId);
        await _taskRepository.UpdateAsync(task, autoSave: true);

        return new WorkflowProjectTaskEnqueueResultDto
        {
            TaskId = taskId,
            ProjectId = projectId,
            WorkflowCount = workflowIds.Count,
            HangfireJobId = hangfireJobId,
            DeploymentId = deploymentId,
            DeploymentRevision = deploymentRevision,
        };
    }

    private async Task<(
        List<Guid> WorkflowIds,
        Guid? DeploymentId,
        int? DeploymentRevision
    )> ResolveTaskWorkflowIdsAsync(Guid projectId)
    {
        WorkflowProjectDeployment? activeDeployment = await AsyncExecuter.FirstOrDefaultAsync(
            (await _deploymentRepository.GetQueryableAsync())
                .Where(x =>
                    x.ProjectId == projectId
                    && x.Status == WorkflowProjectDeploymentStatus.Activated
                )
                .OrderByDescending(x => x.Revision)
        );

        if (activeDeployment is not null)
        {
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
                throw new UserFriendlyException("当前项目的激活部署快照为空，无法创建任务。");
            }

            List<WorkflowDefinition> existingWorkflows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync()).Where(x =>
                    x.ProjectId == projectId && deploymentWorkflowIds.Contains(x.Id)
                )
            );

            if (existingWorkflows.Count != deploymentWorkflowIds.Count)
            {
                throw new UserFriendlyException(
                    "当前项目的激活部署快照包含不存在的工作流，请重新发布项目后再创建任务。"
                );
            }

            Dictionary<Guid, WorkflowDefinition> workflowMap = existingWorkflows.ToDictionary(x =>
                x.Id
            );
            foreach (WorkflowProjectDeploymentItem item in deploymentItems)
            {
                WorkflowDefinition workflow = workflowMap[item.WorkflowId];
                if (
                    !string.Equals(
                        item.GraphHash,
                        ComputeGraphHash(workflow.GraphData),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new UserFriendlyException(
                        $"工作流 {workflow.Name} 已在发布后发生变化，请重新发布项目后再创建任务。"
                    );
                }
            }

            return (deploymentWorkflowIds, activeDeployment.Id, activeDeployment.Revision);
        }

        IQueryable<WorkflowDefinition> queryable = await _repository.GetQueryableAsync();
        List<WorkflowDefinition> projectWorkflows = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.ProjectId == projectId).OrderBy(x => x.CreationTime)
        );

        if (projectWorkflows.Count == 0)
        {
            throw new UserFriendlyException("当前项目下不存在工作流，无法创建任务。");
        }

        return (projectWorkflows.Select(x => x.Id).Distinct().ToList(), null, null);
    }

    /// <inheritdoc/>
    [HttpGet("tasks")]
    public async Task<List<WorkflowProjectTaskStatusDto>> GetProjectTasksAsync(
        WorkflowProjectTaskListInput input
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
            IQueryable<WorkflowProjectTask> queryable = await _taskRepository.GetQueryableAsync();
            if (input.ProjectId != Guid.Empty)
            {
                queryable = queryable.Where(x => x.ProjectId == input.ProjectId);
            }

            List<WorkflowProjectTask> tasks = await AsyncExecuter.ToListAsync(
                queryable
                    .OrderByDescending(x => x.CreationTime)
                    .Skip(input.SkipCount)
                    .Take(input.MaxResultCount)
            );

            return tasks.Select(MapTaskToStatusDto).ToList();
        }
        catch (Exception)
        {
            throw new UserFriendlyException("查询任务列表失败，请稍后重试。");
        }
    }

    /// <inheritdoc/>
    [HttpGet("tasks/{taskId:guid}")]
    public async Task<WorkflowProjectTaskStatusDto> GetProjectTaskStatusAsync(Guid taskId)
    {
        WorkflowProjectTask task = await _taskRepository.GetAsync(taskId);
        return MapTaskToStatusDto(task);
    }

    /// <inheritdoc/>
    [HttpPost("tasks/{taskId:guid}/cancel")]
    public async Task CancelProjectTaskAsync(Guid taskId)
    {
        WorkflowProjectTask task = await _taskRepository.GetAsync(taskId);
        task.RequestCancel();
        await _taskRepository.UpdateAsync(task, autoSave: true);
    }

    /// <inheritdoc/>
    [HttpPut("tasks/{taskId:guid}")]
    public async Task<WorkflowProjectTaskStatusDto> UpdateProjectTaskAsync(
        Guid taskId,
        WorkflowProjectTaskUpdateInput input
    )
    {
        if (taskId == Guid.Empty)
        {
            throw new UserFriendlyException("TaskId 不能为空。");
        }

        if (input is null)
        {
            throw new UserFriendlyException("修改参数不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("任务名称不能为空。");
        }

        if (!Enum.IsDefined(typeof(WorkflowProjectTaskOnErrorAction), input.OnErrorAction))
        {
            throw new UserFriendlyException("OnErrorAction 参数不合法。");
        }

        try
        {
            WorkflowProjectTask? task = await AsyncExecuter.FirstOrDefaultAsync(
                (await _taskRepository.GetQueryableAsync()).Where(x => x.Id == taskId)
            );
            if (task is null)
            {
                throw new UserFriendlyException("任务不存在或已被删除。");
            }

            if (task.Status == WorkflowProjectTaskStatus.Running)
            {
                throw new UserFriendlyException("执行中的任务不允许修改，请先取消或等待结束。");
            }

            task.UpdateName(input.Name.Trim());
            task.UpdateContinueOnError(
                input.OnErrorAction == WorkflowProjectTaskOnErrorAction.ContinueTask
            );

            await _taskRepository.UpdateAsync(task, autoSave: true);
            return MapTaskToStatusDto(task);
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new UserFriendlyException("修改任务失败，请稍后重试。");
        }
    }

    /// <inheritdoc/>
    [HttpDelete("tasks/{taskId:guid}")]
    public async Task DeleteProjectTaskAsync(Guid taskId)
    {
        WorkflowProjectTask task = await _taskRepository.GetAsync(taskId);
        if (task.Status == WorkflowProjectTaskStatus.Running)
        {
            throw new UserFriendlyException("执行中的任务不允许删除，请先取消或等待结束。");
        }

        await _taskRepository.DeleteAsync(task, autoSave: true);
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

        WorkflowExecutionBootstrap bootstrap = await PrepareBootstrapAsync(input);

        return input.Mode switch
        {
            WorkflowExecutionMode.DebugStep => await StartDebugSessionAsync(input, bootstrap),
            WorkflowExecutionMode.Loop => await RunLoopModeAsync(input, bootstrap),
            _ => await RunOnceAsync(input, bootstrap),
        };
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
                    await PersistOutputsAsync(session, input: null, allowContextRead: true);
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
            input,
            bootstrap.RuntimeInstanceId,
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
        Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
            input,
            bootstrap.RuntimeInstanceId,
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
            await PersistOutputsAsync(session, input, allowContextRead: true);
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
            Dictionary<string, object?> initialVariables = await LoadInitialVariablesAsync(
                input,
                bootstrap.RuntimeInstanceId,
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
        await PersistOutputsAsync(resultSession, input, allowContextRead: true);
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

    private async Task PersistOutputsAsync(
        WorkflowExecutionSession session,
        WorkflowExecutionTriggerInput? input,
        bool allowContextRead
    )
    {
        if (!allowContextRead)
        {
            return;
        }

        bool useOnlinePool = session.RuntimeInstanceId.HasValue;
        foreach (WorkflowVariableBindingKeyDto binding in session.OutputBindings)
        {
            object? value = session.Context.Get(binding.VariableName);
            string valueType = WorkflowValueSerializer.InferValueType(value);

            string stagedKey = useOnlinePool
                ? await SaveToOnlineVariablePoolAsync(
                    session,
                    binding.OwnerWorkflowId,
                    binding.VariableName,
                    value,
                    valueType
                )
                : await _bridge.SaveAsync(binding.VariableName, value);

            session.OutputStagedKeys[binding.VariableName] = stagedKey;
        }
    }

    private async Task<WorkflowExecutionBootstrap> PrepareBootstrapAsync(
        WorkflowExecutionTriggerInput input
    )
    {
        if (input.ProjectId == Guid.Empty || input.WorkflowId == Guid.Empty)
        {
            throw new UserFriendlyException("ProjectId/WorkflowId 不能为空。");
        }

        WorkflowDefinition entity = await _repository.GetAsync(input.WorkflowId);
        if (entity.ProjectId != input.ProjectId)
        {
            throw new UserFriendlyException("工作流不存在或不属于该项目。");
        }

        RuntimeWorkflowDefinition compiled;
        GraphDataModel graph;
        string workflowName;
        try
        {
            (workflowName, graph) = WorkflowGraphCompiler.ParseContent(entity.GraphData);
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
            input.WorkflowId
        );
        List<WorkflowVariableBindingKeyDto> outputBindings = BuildBindingKeys(
            input.OutputVariableBindings,
            input.OutputVariableNames is { Count: > 0 } names ? names : signature.Outputs,
            input.WorkflowId
        );

        ValidateBindingKeys(inputBindings, "input");
        ValidateBindingKeys(outputBindings, "output", uniqueByVariableName: true);

        Guid? runtimeInstanceId = input.RuntimeInstanceId ?? GuidGenerator.Create();

        VariableCompileRequestDto compileRequest = await _compileRequestFactory.BuildAsync(
            input.ProjectId,
            input.WorkflowId,
            graph,
            VariableDefUseAnalysisMode.Conservative
        );
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

        IReadOnlyList<string> statementNodeIds =
            WorkflowNodeScheduleBuilder.BuildExecutableNodeOrder(graph);

        return new WorkflowExecutionBootstrap
        {
            ProjectId = input.ProjectId,
            WorkflowId = input.WorkflowId,
            WorkflowName = string.IsNullOrWhiteSpace(entity.Name) ? compiled.Name : entity.Name,
            RuntimeWorkflow = compiled,
            InputBindings = inputBindings,
            OutputBindings = outputBindings,
            RuntimeInstanceId = runtimeInstanceId,
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
            ProjectId = bootstrap.ProjectId,
            WorkflowId = bootstrap.WorkflowId,
            WorkflowName = bootstrap.WorkflowName,
            Mode = mode,
            LoopCount = loopCount,
            RuntimeInstanceId = bootstrap.RuntimeInstanceId,
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

    private async Task<Dictionary<string, object?>> LoadInitialVariablesAsync(
        WorkflowExecutionTriggerInput input,
        Guid? runtimeInstanceId,
        IReadOnlyList<WorkflowVariableBindingKeyDto> inputBindings
    )
    {
        if (runtimeInstanceId.HasValue)
        {
            return await LoadFromOnlineVariablePoolAsync(
                input,
                runtimeInstanceId.Value,
                inputBindings
            );
        }

        return await _bridge.LoadAsync(inputBindings.Select(x => x.VariableName));
    }

    private async Task<Dictionary<string, object?>> LoadFromOnlineVariablePoolAsync(
        WorkflowExecutionTriggerInput input,
        Guid runtimeInstanceId,
        IReadOnlyList<WorkflowVariableBindingKeyDto> inputBindings
    )
    {
        await _onlineVariablePool.InitializeAsync(
            new InitializeVariablePoolInput
            {
                ProjectId = input.ProjectId,
                InstanceId = runtimeInstanceId,
                SnapshotVersion = 0,
            }
        );

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach (
            WorkflowVariableBindingKeyDto binding in inputBindings.DistinctBy(x =>
                (x.OwnerWorkflowId, x.VariableName)
            )
        )
        {
            ReadVariableResultDto read = await _onlineVariablePool.ReadAsync(
                new ReadVariableInput
                {
                    ProjectId = input.ProjectId,
                    InstanceId = runtimeInstanceId,
                    ReaderWorkflowId = input.WorkflowId,
                    OwnerWorkflowId = binding.OwnerWorkflowId,
                    VariableName = binding.VariableName,
                    WaitPolicy = VariableWaitPolicy.WaitOrDefault,
                    TimeoutMs = Math.Max(input.VariableReadTimeoutMs, 0),
                }
            );

            if (string.IsNullOrWhiteSpace(read.ValueJson))
            {
                result[binding.VariableName] = null;
                continue;
            }

            byte[] bytes = Convert.FromBase64String(read.ValueJson);
            result[binding.VariableName] = WorkflowValueSerializer.Deserialize(
                bytes,
                read.TypeName
            );
        }

        return result;
    }

    private async Task<string> SaveToOnlineVariablePoolAsync(
        WorkflowExecutionSession session,
        Guid ownerWorkflowId,
        string variableName,
        object? value,
        string valueType
    )
    {
        byte[] bytes = WorkflowValueSerializer.Serialize(value, valueType);
        string valueJson = Convert.ToBase64String(bytes);

        await _onlineVariablePool.WriteAsync(
            new WriteVariableInput
            {
                ProjectId = session.ProjectId,
                InstanceId = session.RuntimeInstanceId!.Value,
                WriterWorkflowId = session.WorkflowId,
                OwnerWorkflowId = ownerWorkflowId,
                VariableName = variableName,
                TypeName = valueType,
                ValueJson = valueJson,
            }
        );

        return $"pool:{session.RuntimeInstanceId:N}:{ownerWorkflowId:N}:{variableName}";
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
            ProjectId = session.ProjectId,
            WorkflowId = session.WorkflowId,
            WorkflowName = session.WorkflowName,
            Mode = session.Mode,
            Status = session.Status,
            ExecutedSteps = session.ExecutedSteps,
            TotalSteps = session.RuntimeWorkflow.Statements.Count,
            CurrentNodeId = session.CurrentNodeId,
            FaultNodeId = session.FaultNodeId,
            LoopCount = session.LoopCount,
            CompletedLoops = session.CompletedLoops,
            ErrorMessage = session.ErrorMessage,
            DurationMs = session.DurationMs,
            RuntimeInstanceId = session.RuntimeInstanceId,
            Variables = variables,
        };
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

    private static WorkflowProjectTaskStatusDto MapTaskToStatusDto(WorkflowProjectTask task)
    {
        List<Guid> workflowIds = DeserializeJson<List<Guid>>(task.WorkflowIdsJson) ?? [];
        List<WorkflowProjectTaskItemResult> items =
            DeserializeJson<List<WorkflowProjectTaskItemResult>>(task.ResultsJson) ?? [];

        return new WorkflowProjectTaskStatusDto
        {
            TaskId = task.Id,
            ProjectId = task.ProjectId,
            Name = task.Name,
            HangfireJobId = task.HangfireJobId,
            Status = task.Status,
            StartType = task.StartType,
            OnErrorAction = task.ContinueOnError
                ? WorkflowProjectTaskOnErrorAction.ContinueTask
                : WorkflowProjectTaskOnErrorAction.StopTask,
            DeploymentId = task.DeploymentId,
            DeploymentRevision = task.DeploymentRevision,
            IsLegacyResolution = !task.DeploymentId.HasValue,
            RuntimeInstanceId = task.RuntimeInstanceId,
            WorkflowCount = workflowIds.Count,
            ExecutedCount = task.ExecutedCount,
            SuccessCount = task.SuccessCount,
            FailedCount = task.FailedCount,
            IsCancelRequested = task.IsCancelRequested,
            CreationTime = task.CreationTime,
            StartedAt = task.StartedAt,
            FinishedAt = task.FinishedAt,
            ErrorMessage = task.ErrorMessage,
            Items = items
                .Select(x => new WorkflowProjectTaskItemDto
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

    private static WorkflowProjectBindingDto MapBindingDto(
        WorkflowProjectBinding binding,
        string workflowName
    )
    {
        return new WorkflowProjectBindingDto
        {
            Id = binding.Id,
            ProjectId = binding.ProjectId,
            WorkflowId = binding.WorkflowId,
            WorkflowName = workflowName,
            IsEnabled = binding.IsEnabled,
            OrderNo = binding.OrderNo,
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

        public required Guid WorkflowId { get; init; }

        public required string WorkflowName { get; init; }

        public required RuntimeWorkflowDefinition RuntimeWorkflow { get; init; }

        public required IReadOnlyList<string> StatementNodeIds { get; init; }

        public required List<VariableDeclarationDto> Declarations { get; init; }

        public required List<WorkflowVariableBindingKeyDto> InputBindings { get; init; }

        public required List<WorkflowVariableBindingKeyDto> OutputBindings { get; init; }

        public required Guid? RuntimeInstanceId { get; init; }
    }
}
