namespace AuroraStruct3D.Sessions;

/// <summary>
/// 当前客户端会话抽象，提供浏览器标签页级别的会话 ID。
/// 由 HttpApi.Host 层的 HttpClientSessionAccessor 实现（从 HTTP Header 读取）。
/// </summary>
public interface ICurrentClientSession
{
    /// <summary>
    /// 客户端会话 ID（per-tab UUID）。
    /// 前端通过 HTTP Header <c>X-Client-Session-Id</c> 或 SignalR Query <c>clientSessionId</c> 传入。
    /// 在非 HTTP 上下文（后台任务等）中返回 null。
    /// </summary>
    string? SessionId { get; }
}
