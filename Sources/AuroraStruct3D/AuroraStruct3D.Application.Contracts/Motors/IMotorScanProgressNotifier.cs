using AuroraStruct3D.Motors.Dtos;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机扫描进度通知器。
/// 由 HttpApi.Host 通过 SignalR Hub 实现，用于将扫描过程实时推送到前端。
/// 默认情况下若未注册具体实现，应注入空实现以兼容单元测试场景。
/// </summary>
public interface IMotorScanProgressNotifier
{
    /// <summary>
    /// 推送一条扫描进度。
    /// </summary>
    Task NotifyAsync(MotorScanProgressDto progress);
}
