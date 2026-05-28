using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 瓴控 KTECH 电机操作台实时数据推送 SignalR Hub。
/// 允许匿名访问；连接后被动接收 State / UpgradeProgress 推送，无客户端调用方法。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap]
public class KtechMotorHub : AbpHub<IKtechMotorHub> { }
