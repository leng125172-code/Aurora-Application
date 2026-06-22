namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流运行时上下文，持有当前作用域的命名变量字典。
/// <para>
/// 支持嵌套作用域：子作用域读取时向上查找父作用域，
/// 写入时只影响当前作用域（隔离父作用域），
/// 适用于 <c>for</c> 循环、子程序等局部作用域场景。
/// </para>
/// </summary>
public sealed class WorkflowContext : IWorkflowContext
{
    private readonly Dictionary<string, object?> _variables;
    private readonly WorkflowContext? _parent;

    /// <summary>
    /// 创建根作用域上下文（无父作用域）。
    /// </summary>
    public WorkflowContext()
    {
        _variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        _parent = null;
    }

    /// <summary>
    /// 创建子作用域上下文（内部使用，通过 <see cref="CreateChildScope"/> 创建）。
    /// </summary>
    private WorkflowContext(WorkflowContext parent)
    {
        _variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        _parent = parent;
    }

    /// <inheritdoc/>
    public void Set(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        _variables[name] = value;
    }

    /// <inheritdoc/>
    public object? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // 当前作用域优先，未找到则向上查找父作用域
        if (_variables.TryGetValue(name, out object? value))
            return value;

        return _parent?.Get(name);
    }

    /// <inheritdoc/>
    public T? Get<T>(string name)
    {
        object? raw = Get(name);
        if (raw is null)
            return default;

        if (raw is T typed)
            return typed;

        // 尝试 Convert（处理数值类型宽化，如 double → int）
        try
        {
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc/>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.ContainsKey(name) || (_parent?.Contains(name) ?? false);
    }

    /// <inheritdoc/>
    public void Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _variables.Remove(name);
    }

    /// <inheritdoc/>
    public IEnumerable<string> VariableNames
    {
        get
        {
            IEnumerable<string> names = _variables.Keys;
            if (_parent is not null)
                names = names.Concat(_parent.VariableNames);
            return names.Distinct(StringComparer.Ordinal);
        }
    }

    /// <inheritdoc/>
    public IWorkflowContext CreateChildScope() => new WorkflowContext(this);
}
