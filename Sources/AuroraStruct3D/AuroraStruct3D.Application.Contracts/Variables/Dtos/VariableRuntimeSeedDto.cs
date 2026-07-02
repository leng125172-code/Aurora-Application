using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 运行时实例初始化种子值。
/// </summary>
public class VariableRuntimeSeedDto
{
    /// <summary>变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>类型名。</summary>
    [Required]
    [MaxLength(128)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>值 JSON。</summary>
    [Required]
    public string ValueJson { get; set; } = string.Empty;
}
