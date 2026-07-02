using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 变量引用信息（用于离线静态检查）。
/// </summary>
public class VariableReferenceDto
{
    /// <summary>引用目标变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>引用目标变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>引用位置标识（节点 ID / 表达式路径）。</summary>
    [MaxLength(256)]
    public string? Location { get; set; }

    /// <summary>
    /// 引用顺序号（可选）。
    /// <para>用于离线 Def-Use 分析，值越小表示越早执行。</para>
    /// </summary>
    public int? Sequence { get; set; }

    /// <summary>
    /// 引用点期望类型（可选）。
    /// <para>提供该字段时，离线编译将执行类型一致性检查。</para>
    /// </summary>
    [MaxLength(128)]
    public string? ExpectedTypeName { get; set; }
}
