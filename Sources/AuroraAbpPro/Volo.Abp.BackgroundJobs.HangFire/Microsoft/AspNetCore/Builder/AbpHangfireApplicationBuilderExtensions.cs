using Hangfire;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// 提供在 ABP 中注册 Hangfire REST API 路由的扩展方法。
/// </summary>
public static class AbpHangfireApplicationBuilderExtensions
{
    /// <summary>
    /// 在指定路径前缀下注册所有 Hangfire 监控 REST API 端点。
    /// 替代原 UseAbpHangfireDashboard，改为 Minimal API 风格的 REST 接口。
    /// </summary>
    /// <param name="endpoints">ASP.NET Core 端点路由构建器</param>
    /// <param name="pathPrefix">API 路径前缀，默认 /api/hangfire</param>
    public static IEndpointRouteBuilder UseAbpHangfireApi(
        this IEndpointRouteBuilder endpoints,
        string pathPrefix = "/api/hangfire"
    )
    {
        return endpoints.MapHangfireApi(pathPrefix);
    }
}
