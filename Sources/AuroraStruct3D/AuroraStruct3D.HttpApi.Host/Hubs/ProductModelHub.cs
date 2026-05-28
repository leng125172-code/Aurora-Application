using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 产品数模转换进度推送 SignalR Hub。
/// 客户端连接后可实时收到数模转换开始/完成通知。
/// </summary>
[Authorize]
[DisableAutoHubMap] // 禁止 ABP 自动注册，在 Module 中显式 MapHub
public class ProductModelHub : AbpHub<IProductModelHub> { }
