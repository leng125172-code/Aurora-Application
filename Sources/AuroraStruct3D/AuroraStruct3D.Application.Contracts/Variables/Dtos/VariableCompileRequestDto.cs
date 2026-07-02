using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 变量离线编译输入。
/// </summary>
public class VariableCompileRequestDto
{
    /// <summary>项目 ID（隔离边界）。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>当前工作流 ID（声明归属方）。</summary>
    [Required]
    public Guid WorkflowId { get; set; }

    /// <summary>当前工作流声明的变量。</summary>
    public List<VariableDeclarationDto> Declarations { get; set; } = new();

    /// <summary>当前工作流导入的外部变量（只读）。</summary>
    public List<VariableImportDto> Imports { get; set; } = new();

    /// <summary>当前工作流中的读取引用集合。</summary>
    public List<VariableReferenceDto> Reads { get; set; } = new();

    /// <summary>当前工作流中的写入引用集合。</summary>
    public List<VariableReferenceDto> Writes { get; set; } = new();

    /// <summary>
    /// 可选 CFG 入口节点 ID。
    /// <para>为空时将自动选择入度为 0 的节点作为入口。</para>
    /// </summary>
    [MaxLength(256)]
    public string? EntryNodeId { get; set; }

    /// <summary>
    /// 可选控制流边集合。
    /// <para>提供后将按边关系执行路径敏感 Def-Use 分析；为空时回退到序列线性图。</para>
    /// </summary>
    public List<VariableControlFlowEdgeDto> ControlFlowEdges { get; set; } = new();

    /// <summary>
    /// Def-Use 分析模式。
    /// <para>默认值为 Conservative。</para>
    /// </summary>
    public VariableDefUseAnalysisMode DefUseAnalysisMode { get; set; } =
        VariableDefUseAnalysisMode.Conservative;

    /// <summary>
    /// 需要抑制的诊断代码集合（可选）。
    /// <para>匹配 Code（不区分大小写）后将从输出中移除对应诊断。</para>
    /// </summary>
    public List<string> SuppressedDiagnosticCodes { get; set; } = new();
}
