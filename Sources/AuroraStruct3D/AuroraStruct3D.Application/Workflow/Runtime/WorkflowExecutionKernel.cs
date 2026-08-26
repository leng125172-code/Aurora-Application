using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Options;
using RuntimeWorkflowDefinition = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 工作流调试会话存储。
/// </summary>
public interface IWorkflowDebugSessionStore
{
    /// <summary>
    /// 新增会话。
    /// </summary>
    /// <param name="session">会话。</param>
    void Add(WorkflowExecutionSession session);

    /// <summary>
    /// 获取会话。
    /// </summary>
    /// <param name="executionId">会话 ID。</param>
    /// <returns>会话。</returns>
    WorkflowExecutionSession Get(Guid executionId);

    /// <summary>
    /// 获取全部调试会话快照。
    /// </summary>
    /// <returns>调试会话列表。</returns>
    IReadOnlyList<WorkflowExecutionSession> GetAll();

    /// <summary>
    /// 移除会话。
    /// </summary>
    /// <param name="executionId">会话 ID。</param>
    /// <returns>移除结果。</returns>
    bool Remove(Guid executionId);

    /// <summary>
    /// 获取当前正在调试的项目 ID（无活动调试会话时为 null）。
    /// </summary>
    Guid? GetActiveDebugProjectId();

    /// <summary>
    /// 校验项目级调试互斥：若存在其他项目的活动调试会话则抛出异常（一次只能调试一个项目）。
    /// </summary>
    /// <param name="projectId">拟调试的项目 ID。</param>
    void EnsureExclusiveDebugProject(Guid projectId);
}

/// <summary>
/// 默认调试会话存储实现。
/// </summary>
public sealed class WorkflowDebugSessionStore : IWorkflowDebugSessionStore, ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, WorkflowExecutionSession> _sessions = new();
    private readonly object _debugLock = new();
    private readonly WorkflowRuntimeSafetyOptions _options;

    public WorkflowDebugSessionStore()
        : this(Options.Create(new WorkflowRuntimeSafetyOptions())) { }

    public WorkflowDebugSessionStore(IOptions<WorkflowRuntimeSafetyOptions> options) =>
        _options = options.Value;

    /// <inheritdoc/>
    public void Add(WorkflowExecutionSession session)
    {
        PurgeExpired();
        lock (_debugLock)
        {
            if (session.Mode == WorkflowExecutionMode.DebugStep)
            {
                EnsureExclusiveDebugProject(session.ProjectId);
                int projectSessions = _sessions.Values.Count(x =>
                    x.ProjectId == session.ProjectId && IsActiveDebug(x));
                if (projectSessions >= _options.MaxDebugSessionsPerProject)
                    throw new UserFriendlyException(
                        $"[WORKFLOW_PROJECT_QUOTA] 项目活动调试会话已达到上限 {_options.MaxDebugSessionsPerProject}。"
                    );
            }

            if (!_sessions.TryAdd(session.ExecutionId, session))
            {
                throw new UserFriendlyException($"执行会话已存在：{session.ExecutionId}");
            }
        }
    }

    /// <inheritdoc/>
    public WorkflowExecutionSession Get(Guid executionId)
    {
        PurgeExpired();
        if (_sessions.TryGetValue(executionId, out WorkflowExecutionSession? session))
        {
            return session;
        }

        throw new UserFriendlyException($"执行会话不存在：{executionId}");
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowExecutionSession> GetAll()
    {
        PurgeExpired();
        return _sessions.Values.Where(x => x.Mode == WorkflowExecutionMode.DebugStep).ToList();
    }

    /// <inheritdoc/>
    public bool Remove(Guid executionId)
    {
        if (_sessions.TryRemove(executionId, out WorkflowExecutionSession? session))
        {
            session.Dispose();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public Guid? GetActiveDebugProjectId()
    {
        return _sessions
            .Values.Where(IsActiveDebug)
            .Select(x => (Guid?)x.ProjectId)
            .FirstOrDefault();
    }

    /// <inheritdoc/>
    public void EnsureExclusiveDebugProject(Guid projectId)
    {
        WorkflowExecutionSession? other = _sessions.Values.FirstOrDefault(x =>
            IsActiveDebug(x) && x.ProjectId != projectId
        );
        if (other is not null)
        {
            throw new UserFriendlyException(
                "当前主机已有其他项目正在调试，一次只能调试一个项目，请先停止当前调试会话后再开始。"
            );
        }
    }

    private static bool IsActiveDebug(WorkflowExecutionSession session) =>
        session.Mode == WorkflowExecutionMode.DebugStep
        && session.Status is WorkflowExecutionStatus.Pending or WorkflowExecutionStatus.Running;

    private void PurgeExpired()
    {
        DateTime cutoff = DateTime.UtcNow - _options.DebugSessionTtl;
        foreach (WorkflowExecutionSession session in _sessions.Values
                     .Where(x => x.LastAccessAt < cutoff && x.Gate.CurrentCount > 0).ToList())
            Remove(session.ExecutionId);
    }
}

/// <summary>
/// 执行内核。
/// </summary>
public sealed class WorkflowExecutionKernel : ITransientDependency
{
    private readonly WorkflowRuntimeSafetyOptions _options;

    public WorkflowExecutionKernel()
        : this(Options.Create(new WorkflowRuntimeSafetyOptions())) { }

    public WorkflowExecutionKernel(IOptions<WorkflowRuntimeSafetyOptions> options) =>
        _options = options.Value;

    /// <summary>
    /// 执行指定步数。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="steps">步数。</param>
    public void ExecuteSteps(
        WorkflowExecutionSession session,
        int steps,
        bool markCompleted = true
    )
        => ExecuteStepsAsync(session, steps, markCompleted).GetAwaiter().GetResult();

    /// <summary>
    /// 异步执行指定步数。异步算子会收到会话停止、调用方取消和节点超时组成的联合令牌。
    /// </summary>
    public async Task ExecuteStepsAsync(
        WorkflowExecutionSession session,
        int steps,
        bool markCompleted = true,
        CancellationToken cancellationToken = default
    )
    {
        if (steps <= 0)
        {
            return;
        }

        EnsureCanRun(session);
        session.StartIfNeeded();
        using CancellationTokenSource executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                session.CancellationToken,
                cancellationToken
            );

        while (steps > 0 && session.StepCursor < session.RuntimeWorkflow.Statements.Count)
        {
            if (session.DurationMs > _options.WorkflowTimeout.TotalMilliseconds)
                throw new UserFriendlyException(
                    $"[WORKFLOW_TIMEOUT] 工作流执行超过 {_options.WorkflowTimeout.TotalSeconds:0} 秒。"
                );
            session.Touch();
            if (session.StopRequested)
            {
                session.StopRequested = false;
                session.MarkStopped();
                session.ProgressChanged?.Invoke("session-stopped", session);
                return;
            }
            if (session.ConsumePauseRequest())
            {
                session.Trace.Add(new WorkflowTraceEventDto
                {
                    Step = session.ExecutedSteps,
                    NodeId = session.CurrentNodeId,
                    StartedAt = DateTime.UtcNow,
                    EventType = "paused",
                });
                return;
            }
            int index = session.StepCursor;
            IWorkflowStatement statement = session.RuntimeWorkflow.Statements[index];
            string? nodeId =
                index < session.StatementNodeIds.Count ? session.StatementNodeIds[index] : null;

            if (session.LastPausedNodeId == nodeId)
            {
                session.LastPausedNodeId = null;
            }
            session.CurrentNodeId = nodeId;
            session.CurrentNodeStartedAt = DateTime.UtcNow;
            session.ProgressChanged?.Invoke("node-started", session);
            DateTime startedAt = DateTime.UtcNow;
            Stopwatch statementTimer = Stopwatch.StartNew();
            try
            {
                TimeSpan workflowRemaining = _options.WorkflowTimeout
                    - TimeSpan.FromMilliseconds(session.DurationMs);
                if (workflowRemaining <= TimeSpan.Zero)
                    throw new TimeoutException(
                        $"[WORKFLOW_TIMEOUT] 工作流执行超过 {_options.WorkflowTimeout.TotalSeconds:0} 秒。"
                    );
                TimeSpan requestedNodeTimeout =
                    statement is IWorkflowStatementTimeoutProvider timeoutProvider
                    && timeoutProvider.GetRequestedTimeout(session.Context) is { } requested
                        ? requested
                        : _options.NodeTimeout;
                TimeSpan effectiveNodeTimeout =
                    requestedNodeTimeout <= workflowRemaining
                        ? requestedNodeTimeout
                        : workflowRemaining;
                using CancellationTokenSource nodeCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        executionCancellation.Token
                    );
                nodeCancellation.CancelAfter(effectiveNodeTimeout);
                try
                {
                    await statement.ExecuteAsync(
                        session.Context,
                        nodeCancellation.Token
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (session.StopRequested || session.CancellationToken.IsCancellationRequested)
                {
                    session.Trace.Add(
                        new WorkflowTraceEventDto
                        {
                            Step = session.ExecutedSteps + 1,
                            NodeId = nodeId,
                            StatementId = string.IsNullOrWhiteSpace(nodeId)
                                ? null
                                : WorkflowSyntaxParser.GetNodeStatementId(nodeId),
                            StartedAt = startedAt,
                            DurationMs = statementTimer.ElapsedMilliseconds,
                            Status = "stopped",
                            EventType = "cancelled",
                        }
                    );
                    session.MarkStopped();
                    session.ProgressChanged?.Invoke("session-stopped", session);
                    return;
                }
                catch (OperationCanceledException ex)
                    when (
                        nodeCancellation.IsCancellationRequested
                        && !executionCancellation.IsCancellationRequested
                    )
                {
                    throw new TimeoutException(
                        $"[WORKFLOW_NODE_TIMEOUT] 节点执行超过 {effectiveNodeTimeout.TotalMilliseconds:0}ms，已请求取消。",
                        ex
                    );
                }
                session.VariablePool.SyncFromContext(session.Context, nodeId);
                session.StepCursor++;
                session.ExecutedSteps++;
                steps--;
                session.Trace.Add(
                    new WorkflowTraceEventDto
                    {
                        Step = session.ExecutedSteps,
                        NodeId = nodeId,
                        StatementId = string.IsNullOrWhiteSpace(nodeId)
                            ? null
                            : WorkflowSyntaxParser.GetNodeStatementId(nodeId),
                        StartedAt = startedAt,
                        DurationMs = statementTimer.ElapsedMilliseconds,
                    }
                );
                session.LastNodeDurationMs = statementTimer.ElapsedMilliseconds;
                session.ProgressChanged?.Invoke("node-completed", session);
            }
            catch (Exception ex)
            {
                session.Trace.Add(
                    new WorkflowTraceEventDto
                    {
                        Step = session.ExecutedSteps + 1,
                        NodeId = nodeId,
                        StatementId = string.IsNullOrWhiteSpace(nodeId)
                            ? null
                            : WorkflowSyntaxParser.GetNodeStatementId(nodeId),
                        StartedAt = startedAt,
                        DurationMs = statementTimer.ElapsedMilliseconds,
                        Status = "faulted",
                        Error = ex.Message,
                    }
                );
                throw new WorkflowNodeExecutionException(
                    nodeId,
                    $"节点执行失败：{nodeId ?? "<unknown>"}",
                    ex
                );
            }
        }

        if (markCompleted && session.StepCursor >= session.RuntimeWorkflow.Statements.Count)
        {
            session.MarkCompleted();
        }
    }

    /// <summary>
    /// 执行到结束。
    /// </summary>
    /// <param name="session">会话。</param>
    public void ExecuteToCompletion(WorkflowExecutionSession session)
        => ExecuteToCompletionAsync(session).GetAwaiter().GetResult();

    /// <summary>异步执行到结束。</summary>
    public Task ExecuteToCompletionAsync(
        WorkflowExecutionSession session,
        CancellationToken cancellationToken = default
    )
    {
        int remaining = session.RuntimeWorkflow.Statements.Count - session.StepCursor;
        return ExecuteStepsAsync(
            session,
            Math.Max(remaining, 0),
            cancellationToken: cancellationToken
        );
    }

    public void Continue(
        WorkflowExecutionSession session,
        string? runToNodeId = null,
        bool markCompleted = true
    )
        => ContinueAsync(session, runToNodeId, markCompleted).GetAwaiter().GetResult();

    /// <summary>异步继续执行，保留断点、运行到节点和暂停语义。</summary>
    public async Task ContinueAsync(
        WorkflowExecutionSession session,
        string? runToNodeId = null,
        bool markCompleted = true,
        CancellationToken cancellationToken = default
    )
    {
        EnsureCanRun(session);
        while (session.StepCursor < session.RuntimeWorkflow.Statements.Count)
        {
            string? nextNodeId = session.StepCursor < session.StatementNodeIds.Count
                ? session.StatementNodeIds[session.StepCursor]
                : null;
            bool shouldPause = !string.IsNullOrWhiteSpace(runToNodeId) && nextNodeId == runToNodeId;
            bool resumesPausedNode = session.LastPausedNodeId == nextNodeId;
            if (
                !resumesPausedNode
                && nextNodeId is not null
                && session.Breakpoints.TryGetValue(
                    nextNodeId,
                    out WorkflowBreakpointState? breakpoint
                )
            )
            {
                breakpoint.CurrentHitCount++;
                bool hitCountReached = !breakpoint.Definition.HitCount.HasValue
                    || breakpoint.CurrentHitCount >= breakpoint.Definition.HitCount.Value;
                bool conditionMatched = hitCountReached
                    && WorkflowDebugConditionEvaluator.Evaluate(breakpoint.Definition.Condition, session.Context);
                if (conditionMatched && !string.IsNullOrWhiteSpace(breakpoint.Definition.LogMessage))
                {
                    session.Trace.Add(new WorkflowTraceEventDto
                    {
                        Step = session.ExecutedSteps,
                        NodeId = nextNodeId,
                        StatementId = WorkflowSyntaxParser.GetNodeStatementId(nextNodeId),
                        StartedAt = DateTime.UtcNow,
                        EventType = "logpoint",
                        OutputSummary = breakpoint.Definition.LogMessage,
                    });
                }
                else if (conditionMatched)
                    shouldPause = true;
            }
            if (shouldPause && session.LastPausedNodeId != nextNodeId)
            {
                session.CurrentNodeId = nextNodeId;
                session.LastPausedNodeId = nextNodeId;
                session.ProgressChanged?.Invoke(
                    string.IsNullOrWhiteSpace(runToNodeId) ? "breakpoint-hit" : "paused",
                    session
                );
                return;
            }

            int cursorBeforeStep = session.StepCursor;
            await ExecuteStepsAsync(
                session,
                1,
                markCompleted,
                cancellationToken
            ).ConfigureAwait(false);
            if (
                session.Status != WorkflowExecutionStatus.Running
                || session.StepCursor == cursorBeforeStep
            )
                return;
        }
    }

    private static void EnsureCanRun(WorkflowExecutionSession session)
    {
        if (session.Status is WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Stopped)
        {
            throw new UserFriendlyException("当前会话已终止，不能继续执行。");
        }

        if (session.Status == WorkflowExecutionStatus.Completed)
        {
            throw new UserFriendlyException("当前会话已完成，不能继续执行。");
        }
    }
}

public sealed class WorkflowRuntimeSafetyOptions
{
    public TimeSpan WorkflowTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxLoopCount { get; set; } = 10_000;
    public long MaxOutputBytes { get; set; } = 16 * 1024 * 1024;
    public TimeSpan DebugSessionTtl { get; set; } = TimeSpan.FromHours(2);
    public int MaxDebugSessionsPerProject { get; set; } = 8;
}

/// <summary>
/// 节点执行异常。
/// </summary>
public sealed class WorkflowNodeExecutionException : Exception
{
    /// <summary>
    /// 失败节点 ID。
    /// </summary>
    public string? NodeId { get; }

    /// <summary>
    /// 创建节点执行异常。
    /// </summary>
    /// <param name="nodeId">节点 ID。</param>
    /// <param name="message">消息。</param>
    /// <param name="innerException">内部异常。</param>
    public WorkflowNodeExecutionException(string? nodeId, string message, Exception innerException)
        : base(message, innerException)
    {
        NodeId = nodeId;
    }
}

/// <summary>
/// 当前进程内正式运行会话的取消注册表。HTTP 取消请求可借此立即中断正在等待硬件的异步算子；
/// 数据库中的 CancelRequested 仍是跨进程和工作流边界的最终一致性保障。
/// </summary>
public static class WorkflowRunCancellationRegistry
{
    private static readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, WorkflowExecutionSession>
    > ActiveRuns = new();

    internal static void Register(WorkflowExecutionSession session)
    {
        if (!session.RunId.HasValue || session.RunId.Value == Guid.Empty)
            return;
        ActiveRuns
            .GetOrAdd(session.RunId.Value, _ => new ConcurrentDictionary<Guid, WorkflowExecutionSession>())
            [session.ExecutionId] = session;
    }

    internal static void Unregister(WorkflowExecutionSession session)
    {
        if (!session.RunId.HasValue || session.RunId.Value == Guid.Empty)
            return;
        if (!ActiveRuns.TryGetValue(session.RunId.Value, out var sessions))
            return;
        sessions.TryRemove(session.ExecutionId, out _);
        if (sessions.IsEmpty)
            ActiveRuns.TryRemove(
                new KeyValuePair<Guid, ConcurrentDictionary<Guid, WorkflowExecutionSession>>(
                    session.RunId.Value,
                    sessions
                )
            );
    }

    /// <summary>向指定正式运行在当前进程中的全部活动会话发送停止请求。</summary>
    public static int Cancel(Guid runId)
    {
        if (!ActiveRuns.TryGetValue(runId, out var sessions))
            return 0;
        WorkflowExecutionSession[] snapshot = sessions.Values.ToArray();
        foreach (WorkflowExecutionSession session in snapshot)
            session.RequestStop();
        return snapshot.Length;
    }
}

/// <summary>
/// 执行会话。
/// </summary>
public sealed class WorkflowExecutionSession : IDisposable
{
    private readonly Stopwatch _stopwatch = new();
    private readonly object _commandStateLock = new();
    private long _executionCommandId;
    private long _pauseRequestedCommandId;
    private long _statusVersion;
    private readonly CancellationTokenSource _cancellation = new();
    private int _registeredForRunCancellation;
    private int _disposed;

    /// <summary>执行会话锁。</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime LastAccessAt { get; private set; } = DateTime.UtcNow;
    public volatile bool StopRequested;

    /// <summary>传递给异步语句的会话级取消令牌。</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>运行 ID（运行调试场景下有值）。</summary>
    public Guid? RunId { get; init; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; init; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; init; }

    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; init; } = string.Empty;

    /// <summary>执行模式。</summary>
    public WorkflowExecutionMode Mode { get; init; }

    /// <summary>创建该会话的客户端显示主题；后台调试继续执行时仍须保留。</summary>
    public WorkflowDisplayTheme DisplayTheme { get; set; } = WorkflowDisplayTheme.Light;

    /// <summary>总轮次。</summary>
    public int LoopCount { get; init; } = 1;

    /// <summary>已完成轮次。</summary>
    public int CompletedLoops { get; set; }

    /// <summary>执行状态。</summary>
    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Pending;

    /// <summary>运行时工作流。</summary>
    public RuntimeWorkflowDefinition RuntimeWorkflow { get; init; } = null!;

    /// <summary>语句节点顺序。</summary>
    public IReadOnlyList<string> StatementNodeIds { get; init; } = Array.Empty<string>();

    /// <summary>节点 ID 到画布显示名称的映射。</summary>
    public IReadOnlyDictionary<string, string> NodeDisplayNames { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, WorkflowOutputAccessor> OutputAccessors { get; init; } =
        new Dictionary<string, WorkflowOutputAccessor>(StringComparer.Ordinal);

    public IReadOnlyList<string> ConfiguredOutputNames { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> OutputDisplayNames { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>执行上下文。</summary>
    public WorkflowContext Context { get; init; } = null!;

    /// <summary>变量工作池。</summary>
    public WorkflowExecutionVariablePool VariablePool { get; init; } = null!;

    /// <summary>输出绑定。</summary>
    public IReadOnlyList<WorkflowVariableBindingKeyDto> OutputBindings { get; init; } =
        Array.Empty<WorkflowVariableBindingKeyDto>();

    /// <summary>当前游标。</summary>
    public int StepCursor { get; set; }

    /// <summary>已执行步数。</summary>
    public int ExecutedSteps { get; set; }

    /// <summary>当前节点 ID。</summary>
    public string? CurrentNodeId { get; set; }

    public HashSet<string> BreakpointNodeIds { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, WorkflowBreakpointState> Breakpoints { get; } = new(StringComparer.Ordinal);

    public string? LastPausedNodeId { get; set; }

    public List<WorkflowTraceEventDto> Trace { get; } = [];

    /// <summary>调试命令当前是否正在执行节点。</summary>
    public volatile bool IsExecuting;

    /// <summary>开始一次继续、步进或运行到节点命令。</summary>
    public void BeginExecutionCommand()
    {
        lock (_commandStateLock)
        {
            _executionCommandId++;
            _pauseRequestedCommandId = 0;
            IsExecuting = true;
        }
    }

    /// <summary>结束当前调试命令，并清除该命令尚未消费的暂停请求。</summary>
    public void EndExecutionCommand()
    {
        lock (_commandStateLock)
        {
            IsExecuting = false;
            _pauseRequestedCommandId = 0;
        }
    }

    /// <summary>仅为当前正在执行的命令登记暂停请求。</summary>
    public bool RequestPause()
    {
        lock (_commandStateLock)
        {
            if (!IsExecuting)
            {
                return false;
            }

            _pauseRequestedCommandId = _executionCommandId;
            return true;
        }
    }

    /// <summary>由执行内核消费属于当前命令的暂停请求。</summary>
    public bool ConsumePauseRequest()
    {
        lock (_commandStateLock)
        {
            if (
                _pauseRequestedCommandId == 0
                || _pauseRequestedCommandId != _executionCommandId
            )
            {
                return false;
            }

            _pauseRequestedCommandId = 0;
            return true;
        }
    }

    /// <summary>当前节点开始时间。</summary>
    public DateTime? CurrentNodeStartedAt { get; set; }

    /// <summary>最近完成节点耗时。</summary>
    public long LastNodeDurationMs { get; set; }

    /// <summary>调试节点进度回调。</summary>
    public Action<string, WorkflowExecutionSession>? ProgressChanged { get; set; }

    /// <summary>故障节点 ID。</summary>
    public string? FaultNodeId { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>冻结变量快照。</summary>
    public List<WorkflowVariableResultDto> FrozenVariables { get; set; } = new();

    /// <summary>输出持久化键。</summary>
    public Dictionary<string, string> OutputStagedKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前耗时。
    /// </summary>
    public long DurationMs => _stopwatch.ElapsedMilliseconds;

    /// <summary>
    /// 标记启动。
    /// </summary>
    public void StartIfNeeded()
    {
        if (Status == WorkflowExecutionStatus.Pending)
        {
            Status = WorkflowExecutionStatus.Running;
            _stopwatch.Start();
            if (Interlocked.Exchange(ref _registeredForRunCancellation, 1) == 0)
                WorkflowRunCancellationRegistry.Register(this);
        }
    }

    /// <summary>请求停止，并立即唤醒支持取消的异步节点。</summary>
    public void RequestStop()
    {
        StopRequested = true;
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 会话已结束；停止请求保持幂等。
        }
    }

    public void Touch() => LastAccessAt = DateTime.UtcNow;

    /// <summary>生成会话内单调递增的状态快照版本。</summary>
    public long NextStatusVersion() => Interlocked.Increment(ref _statusVersion);

    /// <summary>
    /// 标记完成。
    /// </summary>
    public void MarkCompleted()
    {
        Status = WorkflowExecutionStatus.Completed;
        _stopwatch.Stop();
        UnregisterRunCancellation();
    }

    /// <summary>
    /// 标记故障。
    /// </summary>
    /// <param name="nodeId">故障节点。</param>
    /// <param name="message">错误消息。</param>
    public void MarkFaulted(string? nodeId, string message)
    {
        FaultNodeId = nodeId;
        ErrorMessage = message;
        Status = WorkflowExecutionStatus.Faulted;
        _stopwatch.Stop();
        UnregisterRunCancellation();
    }

    /// <summary>
    /// 标记停止。
    /// </summary>
    public void MarkStopped()
    {
        Status = WorkflowExecutionStatus.Stopped;
        _stopwatch.Stop();
        UnregisterRunCancellation();
    }

    private void UnregisterRunCancellation()
    {
        if (Interlocked.Exchange(ref _registeredForRunCancellation, 0) != 0)
            WorkflowRunCancellationRegistry.Unregister(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        UnregisterRunCancellation();
        _cancellation.Dispose();
        Gate.Dispose();
        Context.Dispose();
    }
}

/// <summary>
/// 执行变量工作池。
/// </summary>
public sealed class WorkflowExecutionVariablePool
{
    private readonly Dictionary<string, VariableSlot> _slots;

    /// <summary>
    /// 使用变量声明初始化工作池。
    /// </summary>
    /// <param name="declarations">变量声明。</param>
    public WorkflowExecutionVariablePool(IEnumerable<VariableDeclarationDto> declarations)
    {
        _slots = declarations
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(
                x => x.Name,
                x => new VariableSlot
                {
                    Name = x.Name,
                    DeclaredType = WorkflowExecutionTypeNormalizer.NormalizeDeclaredType(
                        x.TypeName
                    ),
                    Mutability = x.Mutability,
                    DefaultValue = ParseDefaultValue(x.DefaultValueJson, x.TypeName),
                },
                StringComparer.Ordinal
            );
    }

    /// <summary>
    /// 按声明创建并初始化上下文变量。
    /// </summary>
    /// <param name="context">执行上下文。</param>
    public void Initialize(WorkflowContext context)
    {
        foreach (VariableSlot slot in _slots.Values)
        {
            context.Set(slot.Name, slot.DefaultValue);
            slot.CurrentValue = slot.DefaultValue;
            slot.HasAssigned = slot.DefaultValue is not null;
        }
    }

    /// <summary>
    /// 应用输入初值。
    /// </summary>
    /// <param name="initialVariables">输入变量。</param>
    public void ApplyInitialValues(IReadOnlyDictionary<string, object?> initialVariables)
    {
        foreach ((string name, object? value) in initialVariables)
        {
            if (_slots.TryGetValue(name, out VariableSlot? slot))
            {
                Assign(slot, value, "<input>");
            }
        }
    }

    /// <summary>
    /// 从执行上下文同步变量并做类型与可变性校验。
    /// </summary>
    /// <param name="context">执行上下文。</param>
    /// <param name="nodeId">当前节点 ID。</param>
    public void SyncFromContext(WorkflowContext context, string? nodeId)
    {
        foreach (VariableSlot slot in _slots.Values)
        {
            if (!context.Contains(slot.Name))
            {
                continue;
            }

            object? current = context.Get(slot.Name);
            if (IsSameValue(slot.CurrentValue, current))
            {
                continue;
            }

            Assign(slot, current, nodeId ?? "<unknown>");
        }
    }

    /// <summary>
    /// 清空工作池并清理上下文变量。
    /// </summary>
    /// <param name="context">执行上下文。</param>
    public void Clear(WorkflowContext context)
    {
        foreach (VariableSlot slot in _slots.Values)
        {
            if (context.Contains(slot.Name))
            {
                context.Remove(slot.Name);
            }

            slot.CurrentValue = null;
            slot.HasAssigned = false;
        }
    }

    /// <summary>
    /// 输出变量快照。
    /// </summary>
    /// <param name="context">执行上下文。</param>
    /// <param name="stagedKeys">输出持久化键。</param>
    /// <returns>变量快照。</returns>
    public List<WorkflowVariableResultDto> Snapshot(
        WorkflowContext context,
        IReadOnlyDictionary<string, string>? stagedKeys = null
    )
    {
        List<WorkflowVariableResultDto> variables = new();
        foreach (VariableSlot slot in _slots.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            object? value = context.Contains(slot.Name)
                ? context.Get(slot.Name)
                : slot.CurrentValue;
            string runtimeType = WorkflowValueSerializer.InferValueType(value);

            variables.Add(
                new WorkflowVariableResultDto
                {
                    Name = slot.Name,
                    ValueType = runtimeType,
                    ScalarValue = WorkflowValueTypes.IsScalar(runtimeType)
                        ? WorkflowValueSerializer.ScalarToString(value)
                        : null,
                    StagedKey = stagedKeys?.GetValueOrDefault(slot.Name),
                }
            );
        }

        return variables;
    }

    private static object? ParseDefaultValue(string? defaultValueJson, string typeName)
    {
        if (string.IsNullOrWhiteSpace(defaultValueJson))
        {
            return null;
        }

        try
        {
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(defaultValueJson);
            return AuroraStruct3D.OpenCV.Workflow.Compilation.ValueCoercion.Coerce(
                element,
                typeName
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameValue(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (
            left is string or bool or int or long or double
            || right is string or bool or int or long or double
        )
        {
            return Equals(left, right);
        }

        return false;
    }

    private static void Assign(VariableSlot slot, object? value, string nodeId)
    {
        string runtimeType = WorkflowExecutionTypeNormalizer.NormalizeRuntimeValueType(value);
        if (!WorkflowExecutionTypeNormalizer.IsCompatible(slot.DeclaredType, runtimeType))
        {
            throw new UserFriendlyException(
                $"节点 {nodeId} 写入变量 {slot.Name} 类型不匹配，期望 {slot.DeclaredType}，实际 {runtimeType}。"
            );
        }

        if (slot.Mutability == VariableMutability.Readonly && slot.HasAssigned)
        {
            throw new UserFriendlyException(
                $"节点 {nodeId} 违反可变性约束：变量 {slot.Name} 为 Readonly。"
            );
        }

        if (slot.Mutability == VariableMutability.ConstInit && slot.HasAssigned)
        {
            throw new UserFriendlyException(
                $"节点 {nodeId} 违反可变性约束：变量 {slot.Name} 为 ConstInit，仅允许初始化一次。"
            );
        }

        slot.CurrentValue = value;
        slot.HasAssigned = value is not null;
    }

    private sealed class VariableSlot
    {
        public string Name { get; init; } = string.Empty;

        public string DeclaredType { get; init; } = string.Empty;

        public VariableMutability Mutability { get; init; }

        public object? DefaultValue { get; init; }

        public object? CurrentValue { get; set; }

        public bool HasAssigned { get; set; }
    }
}

/// <summary>
/// 节点调度顺序构造器。
/// </summary>
public static class WorkflowNodeScheduleBuilder
{
    /// <summary>
    /// 生成可执行节点顺序（跳过 start/end）。
    /// </summary>
    /// <param name="graph">图模型。</param>
    /// <returns>节点 ID 顺序。</returns>
    public static IReadOnlyList<string> BuildExecutableNodeOrder(GraphDataModel graph)
    {
        List<NodeModel> ordered = TopologicalOrder(graph);
        return ordered
            .Where(x =>
                x.Type is not ("start-node" or "end-node") && !string.IsNullOrWhiteSpace(x.Id)
            )
            .Select(x => x.Id)
            .ToList();
    }

    /// <summary>
    /// 生成目标节点的祖先执行顺序：仅包含该节点真正依赖的上游算子（拓扑序，跳过 start/end
    /// 与目标节点自身）。用于“只执行需要的算子”的预运行场景，天然跳过并行分支上的副作用节点。
    /// </summary>
    /// <param name="graph">图模型。</param>
    /// <param name="targetNodeId">目标节点 ID。</param>
    /// <returns>祖先节点 ID 顺序（拓扑序）。目标节点无上游依赖时返回空列表。</returns>
    public static IReadOnlyList<string> BuildAncestorNodeOrder(
        GraphDataModel graph,
        string targetNodeId
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (string.IsNullOrWhiteSpace(targetNodeId))
        {
            throw new UserFriendlyException("目标节点 ID 不能为空。");
        }

        // 反向邻接：目标 ← 直接前驱集合。
        Dictionary<string, List<string>> predecessors = new(StringComparer.Ordinal);
        foreach (EdgeModel edge in graph.Edges)
        {
            if (edge.SourceNodeId is null || edge.TargetNodeId is null)
            {
                continue;
            }

            if (!predecessors.TryGetValue(edge.TargetNodeId, out List<string>? list))
            {
                list = [];
                predecessors[edge.TargetNodeId] = list;
            }

            list.Add(edge.SourceNodeId);
        }

        // 从目标节点反向遍历，收集全部祖先。
        HashSet<string> ancestors = new(StringComparer.Ordinal);
        Queue<string> pending = new();
        pending.Enqueue(targetNodeId);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (!predecessors.TryGetValue(current, out List<string>? preds))
            {
                continue;
            }

            foreach (string pred in preds)
            {
                if (ancestors.Add(pred))
                {
                    pending.Enqueue(pred);
                }
            }
        }

        // 按全局拓扑序过滤祖先集合，排除 start/end 与目标节点自身。
        List<NodeModel> ordered = TopologicalOrder(graph);
        return ordered
            .Where(x =>
                ancestors.Contains(x.Id)
                && !string.Equals(x.Id, targetNodeId, StringComparison.Ordinal)
                && x.Type is not ("start-node" or "end-node")
                && !string.IsNullOrWhiteSpace(x.Id)
            )
            .Select(x => x.Id)
            .ToList();
    }

    private static List<NodeModel> TopologicalOrder(GraphDataModel graph)
    {
        List<NodeModel> nodes = graph.Nodes;
        if (nodes.Count == 0)
        {
            return [];
        }

        Dictionary<string, int> indexById = new(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!indexById.TryAdd(nodes[i].Id, i))
            {
                throw new UserFriendlyException($"图中存在重复节点 ID：{nodes[i].Id}");
            }
        }

        int[] indegree = new int[nodes.Count];
        List<int>[] adjacency = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            adjacency[i] = [];
        }

        foreach (EdgeModel edge in graph.Edges)
        {
            if (edge.SourceNodeId is null || edge.TargetNodeId is null)
            {
                continue;
            }

            if (
                !indexById.TryGetValue(edge.SourceNodeId, out int from)
                || !indexById.TryGetValue(edge.TargetNodeId, out int to)
            )
            {
                continue;
            }

            adjacency[from].Add(to);
            indegree[to]++;
        }

        SortedSet<int> available = [];
        for (int i = 0; i < nodes.Count; i++)
        {
            if (indegree[i] == 0)
            {
                available.Add(i);
            }
        }

        List<NodeModel> ordered = new(nodes.Count);
        while (available.Count > 0)
        {
            int index = available.Min;
            available.Remove(index);
            ordered.Add(nodes[index]);

            foreach (int next in adjacency[index])
            {
                if (--indegree[next] == 0)
                {
                    available.Add(next);
                }
            }
        }

        if (ordered.Count != nodes.Count)
        {
            throw new UserFriendlyException("图中存在环，无法执行。");
        }

        return ordered;
    }
}
