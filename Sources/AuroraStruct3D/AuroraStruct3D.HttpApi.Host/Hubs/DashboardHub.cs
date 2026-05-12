using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 仪表盘实时数据 SignalR Hub。
/// 客户端连接后，后台服务会周期性推送 CAP 和 Hangfire 统计数据。
/// 允许匿名访问，依赖各 Dashboard 自身的鉴权逻辑。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap] // 阻止 ABP 自动注册，避免与模块中显式 MapHub 产生路由冲突
public class DashboardHub : AbpHub { }
