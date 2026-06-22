using AuroraStruct3D.OpenCV.Workflow;

namespace AuroraStruct3D.OpenCV;

/// <summary>
/// 算子接口，对应 Halcon 中的一条算子调用语句。
/// <para>
/// 每个实现类仅在构造函数中接受<b>算法配置参数</b>（如滤波核大小、读取模式等），
/// 不在构造时绑定工作流变量；变量绑定在工作流图中通过
/// <see cref="Workflow.Statements.OperatorCallStatement"/> 的端口绑定描述完成。
/// </para>
/// <para>
/// 对应关系：
/// <code>
/// Halcon:   read_image(Image, 'combine')
/// 工作流:   OperatorCall(read_image)
///             输入: img_path → 常量 'combine'
///             输出: output_mat → 变量 "Image"
/// C# 算子:  context.Get&lt;string&gt;("img_path")  →  Cv2.ImRead  →  context.Set("output_mat", mat)
/// </code>
/// </para>
/// </summary>
public interface IOperator : IDisposable
{
    /// <summary>
    /// 输入端口定义列表，描述该算子所需的输入参数及其类型。
    /// 由工作流引擎反射读取，用于构建节点连接 UI 和端口类型校验。
    /// 每次访问应返回新实例（含默认 ParameterName），避免多实例共享状态。
    /// </summary>
    static List<IVisionParameter>? InputVisionParameters { get; }

    /// <summary>
    /// 输出端口定义列表，描述该算子产生的输出参数及其类型。
    /// 由工作流引擎反射读取，用于构建节点连接 UI 和端口类型校验。
    /// 每次访问应返回新实例。
    /// </summary>
    static List<IVisionParameter>? OutputVisionParameters { get; }

    /// <summary>
    /// 算子构造函数配置参数定义列表，描述实例化算子时可配置的算法参数（非工作流变量端口）。
    /// <para>
    /// 由工作流引擎反射读取，用于前端节点面板展示配置项（下拉、数字框等）。
    /// 无构造配置项时返回 null 或空列表。
    /// </para>
    /// </summary>
    static List<IConfigParameter>? ConfigParameters { get; }

    /// <summary>
    /// 执行算子逻辑，从 <paramref name="context"/> 读取输入变量，
    /// 运算完成后将结果写回 <paramref name="context"/>。
    /// </summary>
    /// <param name="context">
    /// 工作流运行时上下文，持有当前作用域的所有命名变量。
    /// 读取输入：<c>context.Get&lt;T&gt;("port_name")</c>；
    /// 写入输出：<c>context.Set("port_name", value)</c>。
    /// </param>
    /// <exception cref="ObjectDisposedException">算子已被释放时抛出。</exception>
    /// <exception cref="InvalidOperationException">输入参数非法或执行失败时抛出。</exception>
    void Execute(IWorkflowContext context);
}
