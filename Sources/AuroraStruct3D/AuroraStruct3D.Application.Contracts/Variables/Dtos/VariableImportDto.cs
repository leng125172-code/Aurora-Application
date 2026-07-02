using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 外部变量导入声明（仅允许读取）。
/// </summary>
public class VariableImportDto
{
    /// <summary>被导入变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>被导入变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>可选别名（用于避免冲突）。</summary>
    [MaxLength(128)]
    public string? Alias { get; set; }
}
