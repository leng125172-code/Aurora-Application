using AuroraStruct3D.Leisai.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 雷赛 iCL-RS 电机操作台 SignalR Hub 客户端推送接口。
/// </summary>
public interface ILeisaiMotorHub
{
    /// <summary>推送一次雷赛实时状态快照。</summary>
    Task ReceiveLeisaiMotorStateAsync(LeisaiStateSnapshotDto state);

    /// <summary>推送完整轨迹满动窗口（位置-速度-时间 3D 图表用）。</summary>
    Task ReceiveLeisaiTraceAsync(LeisaiTraceDto trace);
}
