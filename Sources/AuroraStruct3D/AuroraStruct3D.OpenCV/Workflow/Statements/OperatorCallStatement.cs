using System.Reflection;

namespace AuroraStruct3D.OpenCV.Workflow.Statements;

/// <summary>
/// 工作流算子调用语句，对应 Halcon 中的一条算子调用。
/// <para>
/// 翻译示例：
/// <code>
/// Halcon:
///   texture_laws(Image, ImageTexture1, 'ee', 5, 7)
///
/// 工作流（C# 对象）:
///   new OperatorCallStatement(typeof(texture_laws), ctorArgs: ["ee", 5, 7])
///   {
///       InputBindings  = { ["image"]  = new VariableRefBinding("Image") },
///       OutputBindings = { ["image_texture"] = new OutputBinding("ImageTexture1") }
///   }
/// </code>
/// </para>
/// <para>
/// 执行流程：
/// <list type="number">
///   <item>按 <see cref="InputBindings"/> 将输入变量/常量值写入工作流上下文的端口变量</item>
///   <item>反射实例化算子（<see cref="OperatorType"/>）</item>
///   <item>调用 <see cref="IOperator.Execute(IWorkflowContext)"/></item>
///   <item>按 <see cref="OutputBindings"/> 将算子写入上下文的端口变量重命名为工作流变量</item>
///   <item>Dispose 算子</item>
/// </list>
/// </para>
/// </summary>
public sealed class OperatorCallStatement : IWorkflowStatement
{
    /// <summary>算子的 CLR 类型，必须实现 <see cref="IOperator"/>。</summary>
    public Type OperatorType { get; }

    /// <summary>
    /// 算子构造函数参数（<b>仅算法配置参数</b>，不含工作流变量）。
    /// 按构造函数参数顺序排列，类型必须与构造函数签名一致。
    /// </summary>
    public IReadOnlyList<object?> ConstructorArgs { get; }

    /// <summary>
    /// 输入端口绑定字典，Key 为算子端口变量名（<c>ParameterName</c>）。
    /// Value 为 <see cref="ConstantBinding"/> 或 <see cref="VariableRefBinding"/>。
    /// </summary>
    public IReadOnlyDictionary<string, InputBinding> InputBindings { get; }

    /// <summary>
    /// 输出端口绑定字典，Key 为算子端口变量名（<c>ParameterName</c>），
    /// Value 指定结果写入上下文的工作流变量名。
    /// </summary>
    public IReadOnlyDictionary<string, OutputBinding> OutputBindings { get; }

    /// <param name="operatorType">算子 CLR 类型。</param>
    /// <param name="constructorArgs">算法配置参数列表，可为空。</param>
    /// <param name="inputBindings">输入端口绑定，可为 null（无输入端口时）。</param>
    /// <param name="outputBindings">输出端口绑定，可为 null（无输出端口时）。</param>
    public OperatorCallStatement(
        Type operatorType,
        IEnumerable<object?>? constructorArgs = null,
        IReadOnlyDictionary<string, InputBinding>? inputBindings = null,
        IReadOnlyDictionary<string, OutputBinding>? outputBindings = null
    )
    {
        ArgumentNullException.ThrowIfNull(operatorType);

        if (!typeof(IOperator).IsAssignableFrom(operatorType))
        {
            throw new ArgumentException(
                $"类型 {operatorType.FullName} 未实现 {nameof(IOperator)}",
                nameof(operatorType)
            );
        }

        OperatorType = operatorType;
        ConstructorArgs =
            constructorArgs?.ToList().AsReadOnly() ?? Array.Empty<object?>().AsReadOnly();
        InputBindings = inputBindings ?? new Dictionary<string, InputBinding>().AsReadOnly();
        OutputBindings = outputBindings ?? new Dictionary<string, OutputBinding>().AsReadOnly();
    }

    /// <summary>
    /// 执行算子调用：绑定输入变量 → 实例化算子 → Execute → 重命名输出变量 → Dispose。
    /// </summary>
    public void Execute(IWorkflowContext context)
    {
        // ① 将输入绑定解析后写入上下文的端口变量（端口变量名 = ParameterName）
        foreach ((string portName, InputBinding binding) in InputBindings)
        {
            context.Set(portName, binding.Resolve(context));
        }

        // ② 反射实例化算子（构造函数参数为算法配置，不含端口变量）
        IOperator op = CreateOperatorInstance();

        try
        {
            // ③ 执行算子（算子内部从 context 读取端口变量，写入结果到端口变量）
            op.Execute(context);

            // ④ 按输出绑定：将算子写入的端口变量重命名为工作流变量
            //    例：算子写 "output_mat"，OutputBinding 映射为 "ImageTexture1"
            foreach ((string portName, OutputBinding binding) in OutputBindings)
            {
                object? value = context.Get(portName);
                context.Remove(portName);
                context.Set(binding.VariableName, value);
            }
        }
        finally
        {
            // ⑤ 释放非托管资源（Mat 等）
            op.Dispose();
        }
    }

    private IOperator CreateOperatorInstance()
    {
        try
        {
            object? instance =
                ConstructorArgs.Count > 0
                    ? Activator.CreateInstance(OperatorType, ConstructorArgs.ToArray())
                    : Activator.CreateInstance(OperatorType);

            return instance as IOperator
                ?? throw new InvalidOperationException(
                    $"无法创建算子实例：{OperatorType.FullName}"
                );
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                $"算子 {OperatorType.Name} 构造函数抛出异常：{ex.InnerException?.Message}",
                ex.InnerException ?? ex
            );
        }
    }
}
