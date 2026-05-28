using AuroraStruct3D.Motors.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 电机扫描进度 SignalR Hub 的客户端推送接口。
/// </summary>
public interface IMotorScanHub
{
    /// <summary>
    /// 推送一条电机扫描进度给客户端。
    /// </summary>
    Task ReceiveMotorScanProgressAsync(MotorScanProgressDto progress);
}
