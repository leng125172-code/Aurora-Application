using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 读取运行时变量输入。
/// </summary>
public class ReadVariableInput
{
    /// <summary>项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>执行实例 ID。</summary>
    [Required]
    public Guid InstanceId { get; set; }

    /// <summary>发起读取的工作流 ID（用于权限校验）。</summary>
    [Required]
    public Guid ReaderWorkflowId { get; set; }

    /// <summary>目标变量所属工作流 ID。</summary>
    [Required]
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>目标变量名。</summary>
    [Required]
    [MaxLength(128)]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>未初始化读取策略。</summary>
    public VariableWaitPolicy WaitPolicy { get; set; } = VariableWaitPolicy.NoWait;

    /// <summary>等待超时毫秒（仅 Wait / WaitOrDefault 生效）。</summary>
    public int TimeoutMs { get; set; } = 5000;
}
