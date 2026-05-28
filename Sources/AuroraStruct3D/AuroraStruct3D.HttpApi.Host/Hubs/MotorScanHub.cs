using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 电机扫描进度推送 SignalR Hub。
/// 允许匿名访问，连接后被动接收 <c>ReceiveMotorScanProgressAsync</c> 推送。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap] // 禁止 ABP 自动注册，在 Module 中显式 MapHub
public class MotorScanHub : AbpHub<IMotorScanHub> { }
