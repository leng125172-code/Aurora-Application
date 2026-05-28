using AuroraStruct3D.Cameras;
using AuroraStruct3D.Sessions;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Sessions;

/// <summary>
/// 设备独占操作会话管理 AppService 实现。
/// 提供 Acquire / ForceAcquire / Release / Get 接口供前端调用。
/// </summary>
public class DeviceSessionAppService : ApplicationService, IDeviceSessionAppService
{
    private readonly IDeviceOperationSessionManager _sessionManager;
    private readonly ICurrentClientSession _currentClientSession;
    private readonly ICameraStreamingService? _cameraStreamingService;

    public DeviceSessionAppService(
        IDeviceOperationSessionManager sessionManager,
        ICurrentClientSession currentClientSession,
        ICameraStreamingService? cameraStreamingService = null
    )
    {
        _sessionManager = sessionManager;
        _currentClientSession = currentClientSession;
        _cameraStreamingService = cameraStreamingService;
    }

    /// <inheritdoc/>
    public Task<List<DeviceSessionDto>> GetAllAsync()
    {
        return Task.FromResult(_sessionManager.GetAllSessions().ToList());
    }

    /// <inheritdoc/>
    public Task<DeviceSessionDto?> GetAsync(Guid id)
    {
        return Task.FromResult(_sessionManager.GetSession(id));
    }

    /// <inheritdoc/>
    public Task<DeviceSessionDto> AcquireAsync(Guid deviceId, DeviceType deviceType)
    {
        (string clientSessionId, string? userId, string userName) = GetCallerInfo();
        DeviceSessionDto result = _sessionManager.TryAcquire(
            deviceId,
            deviceType,
            clientSessionId,
            userId,
            userName,
            force: false
        );
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task<DeviceSessionDto> ForceAcquireAsync(Guid deviceId, DeviceType deviceType)
    {
        // 若为相机且当前有预览会话，先停止预览（预览停止后会自动 Release 会话）
        if (deviceType == DeviceType.Camera && _cameraStreamingService != null)
        {
            if (_cameraStreamingService.IsPreviewActive(deviceId))
            {
                await _cameraStreamingService.StopPreviewAsync(deviceId);
                // 等待会话被 StopSessionAsync 释放（已在 StopPreviewAsync 内同步完成）
            }
        }

        (string clientSessionId, string? userId, string userName) = GetCallerInfo();
        DeviceSessionDto result = _sessionManager.TryAcquire(
            deviceId,
            deviceType,
            clientSessionId,
            userId,
            userName,
            force: true
        );
        return result;
    }

    /// <inheritdoc/>
    public Task ReleaseAsync(Guid deviceId)
    {
        string? clientSessionId = _currentClientSession.SessionId;
        if (clientSessionId != null)
        {
            _sessionManager.Release(deviceId, clientSessionId);
        }
        return Task.CompletedTask;
    }

    // ─── 私有辅助 ─────────────────────────────────────────────────────────

    /// <summary>获取当前调用方的 (clientSessionId, userId, userName)</summary>
    private (string clientSessionId, string? userId, string userName) GetCallerInfo()
    {
        string? clientSessionId = _currentClientSession.SessionId;
        if (string.IsNullOrWhiteSpace(clientSessionId))
        {
            throw new UserFriendlyException(
                "请求缺少客户端会话标识（X-Client-Session-Id），请检查前端配置。"
            );
        }

        string? userId = CurrentUser.Id?.ToString();
        string userName = CurrentUser.Name ?? CurrentUser.UserName ?? clientSessionId[..8] + "...";
        return (clientSessionId, userId, userName);
    }
}
