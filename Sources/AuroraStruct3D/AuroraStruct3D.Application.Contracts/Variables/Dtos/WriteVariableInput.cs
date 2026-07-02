using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 写入运行时变量输入。
/// </summary>
public class WriteVariableInput
{
    /// <summary>项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>执行实例 ID。</summary>
    [Required]
    public Guid InstanceId { get; set; }

    /// <summary>发起写入的工作流 ID。</summary>
    [Required]
    public Guid WriterWorkflowId { get; set; }

    /// <summary>目标变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>目标变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>值类型名。</summary>
    [Required]
    [MaxLength(128)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>要写入的 JSON 值。</summary>
    [Required]
    public string ValueJson { get; set; } = string.Empty;

    /// <summary>期望版本号（用于乐观并发控制）。</summary>
    public long? ExpectedVersion { get; set; }

    /// <summary>最后写入节点标识（可选）。</summary>
    [MaxLength(128)]
    public string? WriterNodeId { get; set; }
}
