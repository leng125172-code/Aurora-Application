using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 变量绑定键：显式声明变量归属工作流与变量名。
/// </summary>
public class WorkflowVariableBindingKeyDto
{
    /// <summary>变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;
}
