using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
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
    /// 移除会话。
    /// </summary>
    /// <param name="executionId">会话 ID。</param>
    /// <returns>移除结果。</returns>
    bool Remove(Guid executionId);
}

/// <summary>
/// 默认调试会话存储实现。
/// </summary>
public sealed class WorkflowDebugSessionStore : IWorkflowDebugSessionStore, ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, WorkflowExecutionSession> _sessions = new();

    /// <inheritdoc/>
    public void Add(WorkflowExecutionSession session)
    {
        if (!_sessions.TryAdd(session.ExecutionId, session))
        {
            throw new UserFriendlyException($"执行会话已存在：{session.ExecutionId}");
        }
    }

    /// <inheritdoc/>
    public WorkflowExecutionSession Get(Guid executionId)
    {
        if (_sessions.TryGetValue(executionId, out WorkflowExecutionSession? session))
        {
            return session;
        }

        throw new UserFriendlyException($"执行会话不存在：{executionId}");
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
}

/// <summary>
/// 执行内核。
/// </summary>
public sealed class WorkflowExecutionKernel : ITransientDependency
{
    /// <summary>
    /// 执行指定步数。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="steps">步数。</param>
    public void ExecuteSteps(WorkflowExecutionSession session, int steps)
    {
        if (steps <= 0)
        {
            return;
        }

        EnsureCanRun(session);
        session.StartIfNeeded();

        while (steps > 0 && session.StepCursor < session.RuntimeWorkflow.Statements.Count)
        {
            int index = session.StepCursor;
            IWorkflowStatement statement = session.RuntimeWorkflow.Statements[index];
            string? nodeId =
                index < session.StatementNodeIds.Count ? session.StatementNodeIds[index] : null;

            session.CurrentNodeId = nodeId;
            try
            {
                statement.Execute(session.Context);
                session.VariablePool.SyncFromContext(session.Context, nodeId);
                session.StepCursor++;
                session.ExecutedSteps++;
                steps--;
            }
            catch (Exception ex)
            {
                throw new WorkflowNodeExecutionException(
                    nodeId,
                    $"节点执行失败：{nodeId ?? "<unknown>"}",
                    ex
                );
            }
        }

        if (session.StepCursor >= session.RuntimeWorkflow.Statements.Count)
        {
            session.MarkCompleted();
        }
    }

    /// <summary>
    /// 执行到结束。
    /// </summary>
    /// <param name="session">会话。</param>
    public void ExecuteToCompletion(WorkflowExecutionSession session)
    {
        int remaining = session.RuntimeWorkflow.Statements.Count - session.StepCursor;
        ExecuteSteps(session, Math.Max(remaining, 0));
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
/// 执行会话。
/// </summary>
public sealed class WorkflowExecutionSession : IDisposable
{
    private readonly Stopwatch _stopwatch = new();

    /// <summary>执行会话锁。</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; init; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; init; }

    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; init; } = string.Empty;

    /// <summary>执行模式。</summary>
    public WorkflowExecutionMode Mode { get; init; }

    /// <summary>总轮次。</summary>
    public int LoopCount { get; init; } = 1;

    /// <summary>已完成轮次。</summary>
    public int CompletedLoops { get; set; }

    /// <summary>运行时实例 ID。</summary>
    public Guid? RuntimeInstanceId { get; init; }

    /// <summary>执行状态。</summary>
    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Pending;

    /// <summary>运行时工作流。</summary>
    public RuntimeWorkflowDefinition RuntimeWorkflow { get; init; } = null!;

    /// <summary>语句节点顺序。</summary>
    public IReadOnlyList<string> StatementNodeIds { get; init; } = Array.Empty<string>();

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
        }
    }

    /// <summary>
    /// 标记完成。
    /// </summary>
    public void MarkCompleted()
    {
        Status = WorkflowExecutionStatus.Completed;
        _stopwatch.Stop();
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
    }

    /// <summary>
    /// 标记停止。
    /// </summary>
    public void MarkStopped()
    {
        Status = WorkflowExecutionStatus.Stopped;
        _stopwatch.Stop();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
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
