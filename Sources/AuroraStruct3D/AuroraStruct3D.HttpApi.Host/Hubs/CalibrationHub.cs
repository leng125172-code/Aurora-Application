using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 标定计算进度实时推送 SignalR Hub。
/// 客户端连接后可实时收到各阶段进度（内参/外参/结构光/存储）及最终完成通知。
/// 路由：/signalr-hubs/calibration
/// </summary>
[Authorize]
[DisableAutoHubMap] // 禁止 ABP 自动注册，在 Module 中显式 MapHub
public class CalibrationHub : AbpHub<ICalibrationHub> { }
