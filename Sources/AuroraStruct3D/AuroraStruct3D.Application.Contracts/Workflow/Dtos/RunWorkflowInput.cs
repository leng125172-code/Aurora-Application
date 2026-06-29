using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 执行工作流输入：定位工作流 + 显式指定变量进出 Redis 暂存。
/// </summary>
public class RunWorkflowInput
{
    /// <summary>所属项目 ID（归属校验）。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID。</summary>
    [Required]
    public Guid WorkflowId { get; set; }

    /// <summary>
    /// 执行前从 Redis 暂存加载为初始变量的变量名（= 暂存 key）。为空则不加载。
    /// </summary>
    public List<string>? InputVariableKeys { get; set; }

    /// <summary>
    /// 执行后需要写回 Redis 暂存的结果变量名。为空则不写回（Mat/点云仅在响应中报告名称与类型）。
    /// </summary>
    public List<string>? OutputVariableNames { get; set; }
}
