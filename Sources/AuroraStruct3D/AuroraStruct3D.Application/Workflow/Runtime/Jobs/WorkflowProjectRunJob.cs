using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Workflow.Runtime.Jobs;

/// <summary>
/// 项目级工作流运行 Hangfire Job。
/// <para>语义：1 运行 = 1 项目 = 多个工作流，全部从激活部署快照的冻结图与冻结变量执行。</para>
/// </summary>
public class WorkflowProjectRunJob : ITransientDependency
{
    private readonly WorkflowRuntimeAppService _runtimeAppService;
    private readonly IRepository<WorkflowProjectRun, Guid> _runRepository;
    private readonly IRepository<WorkflowProjectDeployment, Guid> _deploymentRepository;
    private readonly IWorkflowRuntimeVariablePool _runtimeVariablePool;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<WorkflowProjectRunJob> _logger;

    /// <summary>
    /// 初始化 Job。
    /// </summary>
    public WorkflowProjectRunJob(
        WorkflowRuntimeAppService runtimeAppService,
        IRepository<WorkflowProjectRun, Guid> runRepository,
        IRepository<WorkflowProjectDeployment, Guid> deploymentRepository,
        IWorkflowRuntimeVariablePool runtimeVariablePool,
        IAsyncQueryableExecuter asyncExecuter,
        IGuidGenerator guidGenerator,
        ILogger<WorkflowProjectRunJob> logger
    )
    {
        _runtimeAppService = runtimeAppService;
        _runRepository = runRepository;
        _deploymentRepository = deploymentRepository;
        _runtimeVariablePool = runtimeVariablePool;
        _asyncExecuter = asyncExecuter;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    /// <summary>
    /// 执行指定运行实例（立即触发或周期首次触发）。
    /// </summary>
    /// <param name="args">运行参数。</param>
    [Queue("workflow-project-task")]
    [AutomaticRetry(Attempts = 2)]
    [UnitOfWork]
    public async Task ExecuteAsync(WorkflowProjectRunJobArgs args)
    {
        WorkflowProjectDeployment deployment = await _deploymentRepository.GetAsync(
            args.DeploymentId
        );
        await RunProjectAsync(args.RunId, deployment, args.WorkflowIds, args.ContinueOnError);
    }

    /// <summary>
    /// 周期触发：每次按激活部署快照创建并执行一个新的运行实例。
    /// </summary>
    /// <param name="args">周期运行参数。</param>
    [Queue("workflow-project-task")]
    [AutomaticRetry(Attempts = 0)]
    [UnitOfWork]
    public async Task ExecuteRecurringAsync(WorkflowProjectRunRecurringArgs args)
    {
        // 项目独占：已有进行中的运行时跳过本次触发。
        bool hasActiveRun = await _asyncExecuter.AnyAsync(
            (await _runRepository.GetQueryableAsync()).Where(x =>
                x.ProjectId == args.ProjectId
                && (
                    x.Status == WorkflowProjectRunStatus.Queued
                    || x.Status == WorkflowProjectRunStatus.Running
                )
            )
        );
        if (hasActiveRun)
        {
            _logger.LogInformation(
                "[WorkflowProjectRunJob] Recurring skip: project {ProjectId} already has an active run",
                args.ProjectId
            );
            return;
        }

        // 始终以当前激活部署为准（若已停用则本次周期不产生运行）。
        WorkflowProjectDeployment? deployment = await _asyncExecuter.FirstOrDefaultAsync(
            (await _deploymentRepository.GetQueryableAsync())
                .Where(x =>
                    x.ProjectId == args.ProjectId
                    && x.Status == WorkflowProjectDeploymentStatus.Activated
                )
                .OrderByDescending(x => x.Revision)
        );
        if (deployment is null)
        {
            _logger.LogInformation(
                "[WorkflowProjectRunJob] Recurring skip: project {ProjectId} has no activated deployment",
                args.ProjectId
            );
            return;
        }

        List<WorkflowProjectDeploymentItem> items =
            Deserialize<List<WorkflowProjectDeploymentItem>>(deployment.SnapshotJson) ?? [];
        List<Guid> workflowIds = items
            .OrderBy(x => x.OrderNo)
            .Select(x => x.WorkflowId)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (workflowIds.Count == 0)
        {
            return;
        }

        Guid runId = _guidGenerator.Create();
        string name = $"{args.NamePrefix} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        WorkflowProjectRun run = WorkflowProjectRun.Create(
            runId,
            args.ProjectId,
            name,
            WorkflowProjectRunStartType.Cyclic,
            null,
            deployment.Id,
            deployment.Revision,
            workflowIds,
            args.ContinueOnError
        );
        await _runRepository.InsertAsync(run, autoSave: true);

        await RunProjectAsync(runId, deployment, workflowIds, args.ContinueOnError);
    }

    private async Task RunProjectAsync(
        Guid runId,
        WorkflowProjectDeployment deployment,
        List<Guid> workflowIds,
        bool continueOnError
    )
    {
        WorkflowProjectRun run = await _runRepository.GetAsync(runId);
        run.MarkRunning(DateTime.UtcNow);
        await _runRepository.UpdateAsync(run, autoSave: true);

        // 从冻结变量定义初始化单例内存变量池。
        _runtimeVariablePool.Clear();
        List<WorkflowProjectFrozenVariable> frozenVariables =
            Deserialize<List<WorkflowProjectFrozenVariable>>(deployment.FrozenVariablesJson) ?? [];
        _runtimeVariablePool.InitializeFromFrozen(frozenVariables);

        // 冻结图字典：WorkflowId -> GraphData。
        Dictionary<Guid, string> frozenGraphs = (
            Deserialize<List<WorkflowProjectFrozenGraph>>(deployment.FrozenGraphsJson) ?? []
        )
            .Where(x => x.WorkflowId != Guid.Empty)
            .GroupBy(x => x.WorkflowId)
            .ToDictionary(x => x.Key, x => x.Last().GraphData);

        List<WorkflowProjectRunItemResult> items = DeserializeItems(run.ResultsJson);
        Dictionary<Guid, WorkflowProjectRunItemResult> itemMap = items.ToDictionary(
            x => x.WorkflowId,
            x => x
        );
        foreach (Guid workflowId in workflowIds)
        {
            if (!itemMap.ContainsKey(workflowId))
            {
                itemMap[workflowId] = new WorkflowProjectRunItemResult
                {
                    WorkflowId = workflowId,
                    Status = WorkflowProjectRunItemStatus.Pending,
                };
            }
        }

        await SaveProgressAsync(run, itemMap.Values);

        _logger.LogInformation(
            "[WorkflowProjectRunJob] Start run {RunId}, project {ProjectId}, deployment rev {Revision}, workflowCount={WorkflowCount}",
            runId,
            run.ProjectId,
            deployment.Revision,
            workflowIds.Count
        );

        foreach (Guid workflowId in workflowIds)
        {
            run = await _runRepository.GetAsync(runId);
            if (run.IsCancelRequested)
            {
                WorkflowProjectRunItemResult canceledItem = itemMap[workflowId];
                if (canceledItem.Status == WorkflowProjectRunItemStatus.Pending)
                {
                    canceledItem.Status = WorkflowProjectRunItemStatus.Canceled;
                    canceledItem.StartedAt = DateTime.UtcNow;
                    canceledItem.FinishedAt = DateTime.UtcNow;
                }

                run.MarkCanceled(DateTime.UtcNow);
                await SaveProgressAsync(run, itemMap.Values);
                _runtimeVariablePool.Clear();
                _logger.LogInformation(
                    "[WorkflowProjectRunJob] run {RunId} canceled by request",
                    runId
                );
                return;
            }

            WorkflowProjectRunItemResult item = itemMap[workflowId];
            if (item.Status == WorkflowProjectRunItemStatus.Succeeded)
            {
                // 幂等：Hangfire 重试时跳过已成功工作流。
                continue;
            }

            if (
                !frozenGraphs.TryGetValue(workflowId, out string? graphData)
                || string.IsNullOrWhiteSpace(graphData)
            )
            {
                item.Status = WorkflowProjectRunItemStatus.Failed;
                item.ErrorMessage = "部署快照缺少该工作流的冻结图数据。";
                item.FinishedAt = DateTime.UtcNow;
                await SaveProgressAsync(run, itemMap.Values, item.ErrorMessage);

                if (!continueOnError)
                {
                    run.MarkFailed(DateTime.UtcNow, item.ErrorMessage);
                    await _runRepository.UpdateAsync(run, autoSave: true);
                    _runtimeVariablePool.Clear();
                    throw new InvalidOperationException(item.ErrorMessage);
                }

                continue;
            }

            try
            {
                item.Status = WorkflowProjectRunItemStatus.Running;
                item.StartedAt ??= DateTime.UtcNow;
                item.ErrorMessage = null;
                await SaveProgressAsync(run, itemMap.Values);

                Dtos.WorkflowExecutionTriggerResultDto result =
                    await _runtimeAppService.ExecuteFrozenWorkflowAsync(
                        run.ProjectId,
                        runId,
                        workflowId,
                        graphData
                    );

                item.ExecutionId = result.ExecutionId;
                item.Status = WorkflowProjectRunItemStatus.Succeeded;
                item.FinishedAt = DateTime.UtcNow;
                await SaveProgressAsync(run, itemMap.Values);
            }
            catch (Exception ex)
            {
                item.Status = WorkflowProjectRunItemStatus.Failed;
                item.ErrorMessage = ex.Message;
                item.FinishedAt = DateTime.UtcNow;
                await SaveProgressAsync(run, itemMap.Values, ex.Message);

                _logger.LogError(
                    ex,
                    "[WorkflowProjectRunJob] run {RunId}, workflow {WorkflowId} failed",
                    runId,
                    workflowId
                );

                if (!continueOnError)
                {
                    run.MarkFailed(DateTime.UtcNow, ex.Message);
                    await _runRepository.UpdateAsync(run, autoSave: true);
                    _runtimeVariablePool.Clear();
                    throw;
                }
            }
        }

        run = await _runRepository.GetAsync(runId);
        run.MarkSucceeded(DateTime.UtcNow);
        await SaveProgressAsync(run, itemMap.Values);
        _runtimeVariablePool.Clear();

        _logger.LogInformation(
            "[WorkflowProjectRunJob] Finish run {RunId}, project {ProjectId}",
            runId,
            run.ProjectId
        );
    }

    private async Task SaveProgressAsync(
        WorkflowProjectRun run,
        IEnumerable<WorkflowProjectRunItemResult> items,
        string? errorMessage = null
    )
    {
        List<WorkflowProjectRunItemResult> materialized = items.OrderBy(x => x.WorkflowId).ToList();

        int executed = materialized.Count(x =>
            x.Status
                is WorkflowProjectRunItemStatus.Running
                    or WorkflowProjectRunItemStatus.Succeeded
                    or WorkflowProjectRunItemStatus.Failed
                    or WorkflowProjectRunItemStatus.Canceled
                    or WorkflowProjectRunItemStatus.Skipped
        );
        int success = materialized.Count(x => x.Status == WorkflowProjectRunItemStatus.Succeeded);
        int failed = materialized.Count(x => x.Status == WorkflowProjectRunItemStatus.Failed);

        run.UpdateProgress(materialized, executed, success, failed);
        if (run.Status == WorkflowProjectRunStatus.Failed)
        {
            run.MarkFailed(DateTime.UtcNow, errorMessage);
        }

        await _runRepository.UpdateAsync(run, autoSave: true);
    }

    private static List<WorkflowProjectRunItemResult> DeserializeItems(string json) =>
        Deserialize<List<WorkflowProjectRunItemResult>>(json) ?? [];

    private static T? Deserialize<T>(string? json)
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
}
