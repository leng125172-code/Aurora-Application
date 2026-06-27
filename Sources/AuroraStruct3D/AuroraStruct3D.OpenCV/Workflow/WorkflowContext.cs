namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流运行时上下文，持有当前作用域的命名变量字典。
/// <para>
/// 支持嵌套作用域：子作用域读取时向上查找父作用域，
/// 写入时只影响当前作用域（隔离父作用域），
/// 适用于 <c>for</c> 循环、子程序等局部作用域场景。
/// </para>
/// </summary>
public sealed class WorkflowContext : IWorkflowContext, IDisposable
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

        // 覆盖旧值时，若旧值是不再被任何变量引用的原生资源（Mat/点云），及时释放，
        // 避免长流程中间结果堆积非托管内存。
        bool hadOld = _variables.TryGetValue(name, out object? old);
        _variables[name] = value;
        if (hadOld && !ReferenceEquals(old, value))
            DisposeIfOrphaned(old);
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
        if (_variables.Remove(name, out object? old))
            DisposeIfOrphaned(old);
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

    /// <summary>
    /// 释放一个原生资源值（<see cref="Mat"/> / <see cref="MatImg"/> / <see cref="PointCloudData"/>），
    /// 但仅当它不再被本作用域及祖先作用域中的任何其他变量按引用持有时才释放，
    /// 避免端口别名（输入端口与工作流变量指向同一对象）导致的提前释放。
    /// 非原生类型或仍被引用的值不处理。
    /// </summary>
    private void DisposeIfOrphaned(object? value)
    {
        if (value is null)
            return;
        if (value is not (Mat or MatImg or PointCloudData))
            return;

        // 检查当前作用域及祖先作用域是否仍有其他变量引用同一对象（引用相等）
        for (WorkflowContext? scope = this; scope is not null; scope = scope._parent)
        {
            foreach (object? v in scope._variables.Values)
            {
                if (ReferenceEquals(v, value))
                    return; // 仍被引用，不释放
            }
        }

        DisposeNative(value);
    }

    private static void DisposeNative(object? value)
    {
        switch (value)
        {
            case MatImg matImg:
                matImg.DisposeMat();
                break;
            case PointCloudData cloud:
                cloud.DisposePointCloud();
                break;
            case Mat mat:
                mat.Dispose();
                break;
        }
    }

    /// <summary>
    /// 释放本作用域内持有的所有原生资源（Mat / 点云）并清空变量表。
    /// 由调用方在读取完工作流结果后显式调用（或 <c>using</c>），统一回收非托管内存。
    /// </summary>
    public void Dispose()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object? v in _variables.Values)
        {
            if (v is (Mat or MatImg or PointCloudData) && seen.Add(v!))
                DisposeNative(v);
        }
        _variables.Clear();
    }
}
