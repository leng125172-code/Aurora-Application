using AuroraStruct3D.Workflow.Dtos;
using Hangfire;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Workflow.Runtime.Jobs;

/// <summary>
/// 项目级工作流执行 Hangfire Job。
/// <para>语义：1 任务 = 1 项目 = 多个工作流。</para>
/// </summary>
public class WorkflowProjectExecutionJob : ITransientDependency
{
    private readonly IWorkflowRuntimeAppService _workflowRuntimeAppService;
    private readonly IRepository<WorkflowProjectTask, Guid> _taskRepository;
    private readonly ILogger<WorkflowProjectExecutionJob> _logger;

    /// <summary>
    /// 初始化 Job。
    /// </summary>
    /// <param name="workflowRuntimeAppService">工作流运行时应用服务。</param>
    /// <param name="logger">日志。</param>
    public WorkflowProjectExecutionJob(
        IWorkflowRuntimeAppService workflowRuntimeAppService,
        IRepository<WorkflowProjectTask, Guid> taskRepository,
        ILogger<WorkflowProjectExecutionJob> logger
    )
    {
        _workflowRuntimeAppService = workflowRuntimeAppService;
        _taskRepository = taskRepository;
        _logger = logger;
    }

    /// <summary>
    /// 执行项目任务。
    /// </summary>
    /// <param name="args">任务参数。</param>
    [Queue("workflow-project-task")]
    [AutomaticRetry(Attempts = 2)]
    [UnitOfWork]
    public async Task ExecuteAsync(WorkflowProjectExecutionJobArgs args)
    {
        WorkflowProjectTask task = await _taskRepository.GetAsync(args.TaskId);
        task.MarkRunning(DateTime.UtcNow);

        List<WorkflowProjectTaskItemResult> items = DeserializeItems(task.ResultsJson);
        Dictionary<Guid, WorkflowProjectTaskItemResult> itemMap = items.ToDictionary(
            x => x.WorkflowId,
            x => x
        );
        foreach (Guid workflowId in args.WorkflowIds)
        {
            if (!itemMap.ContainsKey(workflowId))
            {
                itemMap[workflowId] = new WorkflowProjectTaskItemResult
                {
                    WorkflowId = workflowId,
                    Status = WorkflowProjectTaskItemStatus.Pending,
                };
            }
        }

        await SaveProgressAsync(task, itemMap.Values);

        _logger.LogInformation(
            "[WorkflowProjectExecutionJob] Start task {TaskId}, project {ProjectId}, workflowCount={WorkflowCount}",
            args.TaskId,
            args.ProjectId,
            args.WorkflowIds.Count
        );

        foreach (Guid workflowId in args.WorkflowIds)
        {
            task = await _taskRepository.GetAsync(args.TaskId);
            if (task.IsCancelRequested)
            {
                WorkflowProjectTaskItemResult canceledItem = itemMap[workflowId];
                if (canceledItem.Status == WorkflowProjectTaskItemStatus.Pending)
                {
                    canceledItem.Status = WorkflowProjectTaskItemStatus.Canceled;
                    canceledItem.StartedAt = DateTime.UtcNow;
                    canceledItem.FinishedAt = DateTime.UtcNow;
                }

                task.MarkCanceled(DateTime.UtcNow);
                await SaveProgressAsync(task, itemMap.Values);
                _logger.LogInformation(
                    "[WorkflowProjectExecutionJob] task {TaskId} canceled by request",
                    args.TaskId
                );
                return;
            }

            WorkflowProjectTaskItemResult item = itemMap[workflowId];
            if (item.Status == WorkflowProjectTaskItemStatus.Succeeded)
            {
                // 幂等：Hangfire 重试时跳过已成功工作流。
                continue;
            }

            try
            {
                item.Status = WorkflowProjectTaskItemStatus.Running;
                item.StartedAt ??= DateTime.UtcNow;
                item.ErrorMessage = null;
                await SaveProgressAsync(task, itemMap.Values);

                await _workflowRuntimeAppService.ExecuteAsync(
                    new WorkflowExecutionTriggerInput
                    {
                        ProjectId = args.ProjectId,
                        WorkflowId = workflowId,
                        Mode = WorkflowExecutionMode.RunOnce,
                        RuntimeInstanceId = args.RuntimeInstanceId,
                        VariableReadTimeoutMs = args.VariableReadTimeoutMs,
                    }
                );

                item.Status = WorkflowProjectTaskItemStatus.Succeeded;
                item.FinishedAt = DateTime.UtcNow;
                await SaveProgressAsync(task, itemMap.Values);
            }
            catch (Exception ex)
            {
                item.Status = WorkflowProjectTaskItemStatus.Failed;
                item.ErrorMessage = ex.Message;
                item.FinishedAt = DateTime.UtcNow;
                await SaveProgressAsync(task, itemMap.Values, ex.Message);

                _logger.LogError(
                    ex,
                    "[WorkflowProjectExecutionJob] task {TaskId}, workflow {WorkflowId} failed",
                    args.TaskId,
                    workflowId
                );

                if (!args.ContinueOnError)
                {
                    task.MarkFailed(DateTime.UtcNow, ex.Message);
                    await _taskRepository.UpdateAsync(task, autoSave: true);
                    throw;
                }
            }
        }

        task = await _taskRepository.GetAsync(args.TaskId);
        task.MarkSucceeded(DateTime.UtcNow);
        await SaveProgressAsync(task, itemMap.Values);

        _logger.LogInformation(
            "[WorkflowProjectExecutionJob] Finish task {TaskId}, project {ProjectId}",
            args.TaskId,
            args.ProjectId
        );
    }

    private async Task SaveProgressAsync(
        WorkflowProjectTask task,
        IEnumerable<WorkflowProjectTaskItemResult> items,
        string? errorMessage = null
    )
    {
        List<WorkflowProjectTaskItemResult> materialized = items
            .OrderBy(x => x.WorkflowId)
            .ToList();

        int executed = materialized.Count(x =>
            x.Status
                is WorkflowProjectTaskItemStatus.Running
                    or WorkflowProjectTaskItemStatus.Succeeded
                    or WorkflowProjectTaskItemStatus.Failed
                    or WorkflowProjectTaskItemStatus.Canceled
                    or WorkflowProjectTaskItemStatus.Skipped
        );
        int success = materialized.Count(x => x.Status == WorkflowProjectTaskItemStatus.Succeeded);
        int failed = materialized.Count(x => x.Status == WorkflowProjectTaskItemStatus.Failed);

        task.UpdateProgress(materialized, executed, success, failed);
        if (task.Status == WorkflowProjectTaskStatus.Failed)
        {
            task.MarkFailed(DateTime.UtcNow, errorMessage);
        }

        await _taskRepository.UpdateAsync(task, autoSave: true);
    }

    private static List<WorkflowProjectTaskItemResult> DeserializeItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<WorkflowProjectTaskItemResult>>(
                    json
                ) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
