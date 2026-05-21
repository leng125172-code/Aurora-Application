using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 切换运行模式请求输入
/// </summary>
public class SwitchModeInput
{
    /// <summary>目标运行模式</summary>
    [Required]
    public DeviceRunMode NewMode { get; set; }

    /// <summary>切换原因（可选）</summary>
    public string? Reason { get; set; }
}
