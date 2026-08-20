namespace AuroraStruct3D.OpenCV.Workflow.Statements;

// ── 输入端口绑定 ──────────────────────────────────────────────────────────────

/// <summary>
/// 算子输入端口绑定的抽象基类。
/// <para>
/// 对应 Halcon 中一条算子调用的输入参数，可以是：
/// <list type="bullet">
///   <item><see cref="ConstantBinding"/> — 直接量（字符串、数字、枚举等）</item>
///   <item><see cref="VariableRefBinding"/> — 对工作流变量的引用</item>
/// </list>
/// </para>
/// </summary>
public abstract class InputBinding
{
    /// <summary>
    /// 从上下文解析该绑定的实际值。
    /// </summary>
    public abstract object? Resolve(IWorkflowContext context);
}

/// <summary>
/// 常量输入绑定：端口值在工作流定义时固定为一个常量。
/// <para>对应 Halcon：<c>read_image(Image, 'combine')</c> 中的 <c>'combine'</c>。</para>
/// </summary>
public sealed class ConstantBinding : InputBinding
{
    /// <summary>常量值，可为字符串、数值、枚举或 null。</summary>
    public object? Value { get; }

    /// <param name="value">常量值。</param>
    public ConstantBinding(object? value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public override object? Resolve(IWorkflowContext context) => Value;
}

/// <summary>
/// 变量引用输入绑定：端口值在运行期从上下文中读取指定名称的变量。
/// <para>
/// 对应 Halcon：<c>texture_laws(Image, ImageTexture1, 'ee', 5, 7)</c>
/// 中的 <c>Image</c>（引用前一算子输出的变量）。
/// </para>
/// </summary>
public sealed class VariableRefBinding : InputBinding
{
    private readonly WorkflowValueAccessor _accessor;
    /// <summary>引用的工作流变量名，区分大小写。</summary>
    public string VariableName { get; }

    /// <param name="variableName">引用的变量名。</param>
    public VariableRefBinding(string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        VariableName = variableName;
        _accessor = WorkflowValueAccessor.Compile(variableName);
    }

    /// <inheritdoc/>
    public override object? Resolve(IWorkflowContext context) => _accessor.Resolve(context);
}

// ── 输出端口绑定 ──────────────────────────────────────────────────────────────

/// <summary>
/// 算子输出端口绑定：执行完毕后将输出值写入上下文中指定名称的变量。
/// <para>
/// 对应 Halcon：<c>read_image(Image, 'combine')</c> 中的 <c>Image</c>（输出变量）。
/// 算子调用完成后，<c>Image</c> 变量即可被后续算子引用。
/// </para>
/// </summary>
public sealed class OutputBinding
{
    /// <summary>结果写入的工作流变量名，区分大小写。</summary>
    public string VariableName { get; }

    /// <param name="variableName">结果写入的变量名。</param>
    public OutputBinding(string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        VariableName = variableName;
    }
}
