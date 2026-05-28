using AuroraStruct3D.Leisai.Dtos;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛 iCL-RS 实时数据推送通知器（由 HttpApi.Host 通过 SignalR Hub 实现）。
/// </summary>
public interface ILeisaiMotorNotifier
{
    /// <summary>推送一次状态快照。</summary>
    Task NotifyStateAsync(LeisaiStateSnapshotDto state);

    /// <summary>推送完整轨迹满动窗口（位置-速度序列）。</summary>
    Task NotifyTraceAsync(LeisaiTraceDto trace);
}
