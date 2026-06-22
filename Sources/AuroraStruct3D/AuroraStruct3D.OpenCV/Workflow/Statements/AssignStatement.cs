namespace AuroraStruct3D.OpenCV.Workflow.Statements;

/// <summary>
/// 工作流变量赋值语句，对应 Halcon 中的直接赋值表达式。
/// <para>
/// 翻译示例：
/// <code>
/// Halcon:  RotX := 75
/// 工作流:  new AssignStatement("RotX", new ConstantBinding(75.0))
///
/// Halcon:  RotX := fmod(Row, 360)
/// 工作流:  new AssignStatement("RotX",
///              new ComputedBinding(ctx => (double)ctx.Get("Row")! % 360.0))
/// </code>
/// </para>
/// </summary>
public sealed class AssignStatement : IWorkflowStatement
{
    /// <summary>赋值目标变量名。</summary>
    public string VariableName { get; }

    /// <summary>值来源绑定（常量、变量引用或计算表达式）。</summary>
    public InputBinding Source { get; }

    /// <param name="variableName">赋值目标变量名。</param>
    /// <param name="source">值来源绑定。</param>
    public AssignStatement(string variableName, InputBinding source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(source);

        VariableName = variableName;
        Source = source;
    }

    /// <summary>
    /// 执行赋值：解析 <see cref="Source"/> 的值并写入上下文的 <see cref="VariableName"/> 变量。
    /// </summary>
    public void Execute(IWorkflowContext context)
    {
        object? value = Source.Resolve(context);
        context.Set(VariableName, value);
    }
}

/// <summary>
/// 计算型输入绑定：值由委托在运行期动态计算，支持任意表达式。
/// <para>
/// 用于翻译 Halcon 中的函数调用表达式，如 <c>fmod(Row, 360)</c>、<c>j</c> 等。
/// </para>
/// </summary>
public sealed class ComputedBinding : InputBinding
{
    private readonly Func<IWorkflowContext, object?> _compute;

    /// <param name="compute">计算委托，接受上下文并返回值。</param>
    public ComputedBinding(Func<IWorkflowContext, object?> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);
        _compute = compute;
    }

    /// <inheritdoc/>
    public override object? Resolve(IWorkflowContext context) => _compute(context);
}
