using AuroraStruct3D.Sessions;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Sessions;

/// <summary>
/// 从 HTTP 请求中读取 per-tab 客户端会话 ID。
/// 优先读取请求头 <c>X-Client-Session-Id</c>，其次读取 Query 参数 <c>clientSessionId</c>（供 SignalR 使用）。
/// </summary>
/// <remarks>
/// 必须显式声明 <see cref="ExposeServicesAttribute"/>，因为类名不满足 ABP 约定
/// （约定：类名需以接口名去掉 "I" 结尾，如 CurrentClientSession：ICurrentClientSession），
/// 否则 ABP 不会将此类注册为 ICurrentClientSession，导致 DI 解析失败。
/// </remarks>
[ExposeServices(typeof(ICurrentClientSession))]
public sealed class HttpClientSessionAccessor : ICurrentClientSession, ITransientDependency
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpClientSessionAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string? SessionId
    {
        get
        {
            HttpContext? ctx = _httpContextAccessor.HttpContext;
            if (ctx is null)
            {
                return null;
            }

            // 优先读取 HTTP Header（REST API 请求）
            string? fromHeader = ctx.Request.Headers["X-Client-Session-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fromHeader))
            {
                return fromHeader;
            }

            // 其次读取 Query 参数（SignalR WebSocket 握手）
            string? fromQuery = ctx.Request.Query["clientSessionId"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(fromQuery) ? null : fromQuery;
        }
    }
}
