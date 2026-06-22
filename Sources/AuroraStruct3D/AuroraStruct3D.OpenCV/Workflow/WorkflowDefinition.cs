using AuroraStruct3D.OpenCV.Workflow.Statements;

namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流定义，对应一个完整的 Halcon 程序（.hdev 文件）。
/// <para>
/// 持有顶层语句列表，每条语句为 <see cref="IWorkflowStatement"/> 的实现，
/// 包括算子调用、for 循环、if/else 分支、变量赋值等。
/// 通过 <see cref="WorkflowExecutor"/> 执行。
/// </para>
/// <para>
/// 示例（对应 Halcon 片段）：
/// <code>
/// read_image(Image, 'combine')
/// texture_laws(Image, ImageTexture1, 'ee', 5, 7)
/// for i := 1 to 10 by 1
///     ...
/// endfor
/// </code>
/// 翻译为：
/// <code>
/// new WorkflowDefinition("TextureAnalysis",
/// [
///     new OperatorCallStatement(typeof(read_image), ...),
///     new OperatorCallStatement(typeof(texture_laws), ...),
///     new ForLoopStatement("i", 1, 10, 1, [...]),
/// ])
/// </code>
/// </para>
/// </summary>
public sealed class WorkflowDefinition
{
    /// <summary>工作流名称，对应 Halcon 程序文件名。</summary>
    public string Name { get; }

    /// <summary>
    /// 顶层语句列表，按 Halcon 程序中的语句顺序排列，顺序不可改变。
    /// </summary>
    public IReadOnlyList<IWorkflowStatement> Statements { get; }

    /// <param name="name">工作流名称。</param>
    /// <param name="statements">顶层语句列表，顺序与 Halcon 程序一致。</param>
    public WorkflowDefinition(string name, IEnumerable<IWorkflowStatement> statements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(statements);

        Name = name;
        Statements = statements.ToList().AsReadOnly();
    }
}
