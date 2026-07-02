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
    /// 执行前从在线变量池读取的显式变量绑定键。
    /// <para>启用在线变量池时优先使用；为空则回退到 <see cref="InputVariableKeys"/> 并默认归属当前工作流。</para>
    /// </summary>
    public List<WorkflowVariableBindingKeyDto>? InputVariableBindings { get; set; }

    /// <summary>
    /// 执行后需要写回 Redis 暂存的结果变量名。为空则不写回（Mat/点云仅在响应中报告名称与类型）。
    /// </summary>
    public List<string>? OutputVariableNames { get; set; }

    /// <summary>
    /// 执行后写回在线变量池的显式变量绑定键。
    /// <para>启用在线变量池时优先使用；为空则回退到 <see cref="OutputVariableNames"/> 并默认归属当前工作流。</para>
    /// </summary>
    public List<WorkflowVariableBindingKeyDto>? OutputVariableBindings { get; set; }

    /// <summary>
    /// 是否启用在线变量池执行模式。
    /// <para>
    /// 关闭时保持历史行为（Redis 暂存桥接）；开启时走独立变量模块的在线变量池。
    /// </para>
    /// </summary>
    public bool UseOnlineVariablePool { get; set; }

    /// <summary>
    /// 运行时实例 ID。
    /// <para>启用在线变量池时生效；为空则由服务端自动生成。</para>
    /// </summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>
    /// 在线变量池读取超时毫秒。
    /// </summary>
    public int VariableReadTimeoutMs { get; set; } = 5000;
}
