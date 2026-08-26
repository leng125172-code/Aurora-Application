using System.Collections.Concurrent;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Variables;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 工作流运行时单例变量池。
/// <para>生命周期：程序启动时创建（单例）；任务开始前由 Job 调用 <see cref="InitializeAsync"/> 加载默认值；
/// 任务内所有工作流执行完成后由 Job 调用 <see cref="Clear"/> 清空，防止干扰下次运行。</para>
/// <para>同一时刻仅允许一个任务占用该池（由上层任务独占约束保证）。</para>
/// </summary>
public interface IWorkflowRuntimeVariablePool
{
    IDisposable BeginRunScope(Guid runId);
    /// <summary>
    /// 从数据库加载指定项目的变量定义默认值，填充到内存池（幂等：已存在的键不覆盖）。
    /// </summary>
    Task InitializeAsync(Guid projectId);

    /// <summary>
    /// 从冻结的变量定义列表填充内存池（幂等：已存在的键不覆盖），用于部署快照冻结执行。
    /// </summary>
    void InitializeFromFrozen(IEnumerable<WorkflowProjectFrozenVariable> frozenVariables);

    /// <summary>
    /// 尝试读取变量值。
    /// </summary>
    bool TryRead(Guid ownerWorkflowId, string variableName, out object? value);

    /// <summary>
    /// 写入变量值。
    /// </summary>
    void Write(Guid ownerWorkflowId, string variableName, object? value);

    /// <summary>
    /// 清空所有变量值（任务完成、失败或取消后调用）。
    /// </summary>
    void Clear();
}

/// <summary>
/// <inheritdoc cref="IWorkflowRuntimeVariablePool"/> 默认内存实现。
/// </summary>
public sealed class WorkflowRuntimeVariablePool : IWorkflowRuntimeVariablePool, ISingletonDependency
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<
        (Guid RunId, Guid OwnerWorkflowId, string VariableName),
        object?
    > _values = new();
    private readonly AsyncLocal<Guid?> _currentRunId = new();

    /// <summary>
    /// 构造函数。
    /// </summary>
    public WorkflowRuntimeVariablePool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IDisposable BeginRunScope(Guid runId)
    {
        Guid? previous = _currentRunId.Value;
        _currentRunId.Value = runId;
        return new RunScope(() => _currentRunId.Value = previous);
    }

    private Guid CurrentRunId => _currentRunId.Value ?? Guid.Empty;

    /// <inheritdoc/>
    public async Task InitializeAsync(Guid projectId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        IUnitOfWorkManager uowManager =
            scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using IUnitOfWork uow = uowManager.Begin(requiresNew: true, isTransactional: false);

        IRepository<VariableDefinition, Guid> repo = scope.ServiceProvider.GetRequiredService<
            IRepository<VariableDefinition, Guid>
        >();

        List<VariableDefinition> definitions = await repo.GetListAsync(x =>
            x.ProjectId == projectId
        );

        foreach (VariableDefinition definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.DefaultValueJson))
            {
                continue;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(definition.DefaultValueJson);
                object? value = WorkflowValueSerializer.Deserialize(bytes, definition.TypeName);
                _values.TryAdd((CurrentRunId, definition.OwnerWorkflowId, definition.Name), value);
            }
            catch
            {
                // 默认值格式无效时跳过，不阻断执行。
            }
        }

        await uow.CompleteAsync();
    }

    /// <inheritdoc/>
    public void InitializeFromFrozen(IEnumerable<WorkflowProjectFrozenVariable> frozenVariables)
    {
        foreach (WorkflowProjectFrozenVariable definition in frozenVariables)
        {
            if (string.IsNullOrWhiteSpace(definition.DefaultValueJson))
            {
                continue;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(definition.DefaultValueJson);
                object? value = WorkflowValueSerializer.Deserialize(bytes, definition.TypeName);
                _values.TryAdd((CurrentRunId, definition.OwnerWorkflowId, definition.Name), value);
            }
            catch
            {
                // 默认值格式无效时跳过，不阻断执行。
            }
        }
    }

    /// <inheritdoc/>
    public bool TryRead(Guid ownerWorkflowId, string variableName, out object? value) =>
        _values.TryGetValue((CurrentRunId, ownerWorkflowId, variableName), out value);

    /// <inheritdoc/>
    public void Write(Guid ownerWorkflowId, string variableName, object? value) =>
        _values[(CurrentRunId, ownerWorkflowId, variableName)] = value;

    /// <inheritdoc/>
    public void Clear()
    {
        Guid runId = CurrentRunId;
        foreach (var key in _values.Keys.Where(x => x.RunId == runId).ToArray())
            _values.TryRemove(key, out _);
    }

    private sealed class RunScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
