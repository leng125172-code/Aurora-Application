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
    /// 算子构造函数参数绑定（<b>仅算法配置参数</b>），按构造函数参数顺序排列。
    /// <para>
    /// 每个元素为 <see cref="InputBinding"/>：
    /// <list type="bullet">
    ///   <item><see cref="ConstantBinding"/> — 编译期固定的字面量配置；</item>
    ///   <item><see cref="VariableRefBinding"/> / <see cref="ComputedBinding"/> —
    ///   引用工作流变量（<c>{ "$var": ... }</c>），在节点执行时从上下文解析后再实例化算子。</item>
    /// </list>
    /// 解析结果的类型必须与构造函数签名一致（强转由编译器在绑定内完成）。
    /// </para>
    /// </summary>
    public IReadOnlyList<InputBinding> ConfigArgBindings { get; }

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
    /// <param name="constructorArgs">
    /// 算法配置参数列表，按构造函数顺序排列，可为空。每个元素可以是：
    /// 已构造的 <see cref="InputBinding"/>（直接使用，支持 <c>$var</c> 运行期取值），
    /// 或任意字面量值（自动包装为 <see cref="ConstantBinding"/>）。
    /// </param>
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
        // 字面量参数包装为 ConstantBinding；已是 InputBinding 的（如编译器产出的
        // ComputedBinding/$var 引用）直接保留，留待执行期解析。
        ConfigArgBindings = (constructorArgs ?? Array.Empty<object?>())
            .Select(static a => a as InputBinding ?? new ConstantBinding(a))
            .ToList()
            .AsReadOnly();
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

        // ② 解析配置参数：字面量直接取值，$var 绑定此刻从上下文读取上游变量
        //    （上游节点已执行并写入变量，故可读）。然后按顺序作为构造函数实参。
        object?[] configArgs = ConfigArgBindings.Count > 0
            ? ConfigArgBindings.Select(b => b.Resolve(context)).ToArray()
            : Array.Empty<object?>();

        // ③ 反射实例化算子（构造函数参数为算法配置，不含端口变量）
        IOperator op = CreateOperatorInstance(configArgs);

        try
        {
            // ④ 执行算子（算子内部从 context 读取端口变量，写入结果到端口变量）
            op.Execute(context);

            // ⑤ 按输出绑定：将算子写入的端口变量重命名为工作流变量
            //    先以工作流变量名建立引用，再移除端口别名，避免中途被上下文回收。
            //    例：算子写 "output_mat"，OutputBinding 映射为 "ImageTexture1"
            foreach ((string portName, OutputBinding binding) in OutputBindings)
            {
                object? value = context.Get(portName);
                context.Set(binding.VariableName, value);
                if (!string.Equals(binding.VariableName, portName, StringComparison.Ordinal))
                    context.Remove(portName);
            }
        }
        finally
        {
            // ⑥ 释放算子实例（Mat 等非托管资源）
            op.Dispose();

            // ⑦ 清理输入端口的临时变量，避免污染上下文与跨算子串味。
            //    跳过被输出绑定占用为结果变量的名字，避免误删结果。
            foreach (string portName in InputBindings.Keys)
            {
                if (IsOutputTarget(portName))
                    continue;
                context.Remove(portName);
            }
        }
    }

    /// <summary>判断某端口名是否被某个输出绑定用作结果变量名（避免清理时误删结果）。</summary>
    private bool IsOutputTarget(string portName)
    {
        foreach (OutputBinding binding in OutputBindings.Values)
        {
            if (string.Equals(binding.VariableName, portName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private IOperator CreateOperatorInstance(object?[] configArgs)
    {
        try
        {
            object? instance =
                configArgs.Length > 0
                    ? Activator.CreateInstance(OperatorType, configArgs)
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
