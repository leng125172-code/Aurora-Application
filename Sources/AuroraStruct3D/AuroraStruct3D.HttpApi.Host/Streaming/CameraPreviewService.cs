using System.Collections.Concurrent;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Hubs;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Streaming;

/// <summary>
/// 相机实时推流后台服务，同时实现 <see cref="ICameraStreamingService"/>。
/// 管理每台相机的预览会话：后台高优先级线程持续抓帧，
/// 通过 SignalR (MessagePack) 推帧，同时可选 RTP/MJPEG UDP 副流。
/// </summary>
public class CameraPreviewService : ICameraStreamingService, IHostedService, IDisposable
{
    /// <summary>指标推送间隔（毫秒）</summary>
    private const int MetricsPushIntervalMs = 2000;

    /// <summary>
    /// SignalR 推帧最大帧率（FPS）。
    /// 浏览器受限于显示刷新率（通常 60Hz）且 JS 单线程 DOM 更新有开销，
    /// 30FPS 对人眼预览已足够；120FPS 高速帧流应走 RTP/UDP 副流。
    /// </summary>
    private const int SignalRMaxFps = 30;

    /// <summary>SignalR 推帧最小间隔（毫秒），由 <see cref="SignalRMaxFps"/> 推导</summary>
    private const long SignalRFrameIntervalMs = 1000 / SignalRMaxFps;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<CameraHub, ICameraHub> _hubContext;
    private readonly RtpMjpegServer _rtpServer;
    private readonly ILogger<CameraPreviewService> _logger;

    /// <summary>相机 ID → 预览会话（仅当预览激活时存在）</summary>
    private readonly ConcurrentDictionary<Guid, CameraPreviewSession> _sessions = new();

    private CancellationTokenSource _cts = new();
    private bool _disposed;

    public CameraPreviewService(
        IServiceScopeFactory scopeFactory,
        IHubContext<CameraHub, ICameraHub> hubContext,
        RtpMjpegServer rtpServer,
        ILogger<CameraPreviewService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _rtpServer = rtpServer;
        _logger = logger;
    }

    // ─── IHostedService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.LogInformation("CameraPreviewService 已启动");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();

        // 停止全部预览会话
        foreach (Guid cameraId in _sessions.Keys.ToList())
        {
            await StopSessionAsync(cameraId);
        }

        _logger.LogInformation("CameraPreviewService 已停止");
    }

    // ─── ICameraStreamingService ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartPreviewAsync(Guid cameraId, string? connectionId, bool enableRtp)
    {
        // 若已有会话，先停止
        if (_sessions.ContainsKey(cameraId))
        {
            await StopSessionAsync(cameraId);
        }

        CancellationToken token = _cts.Token;

        Thread thread = new Thread(() => RunPreviewLoop(cameraId, enableRtp, token))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = $"CameraPreview-{cameraId:N}",
        };

        CameraPreviewSession session = new CameraPreviewSession(thread, connectionId, enableRtp);
        _sessions[cameraId] = session;
        thread.Start();

        _logger.LogInformation(
            "相机 {Id} 实时预览已启动，EnableRtp={EnableRtp}",
            cameraId,
            enableRtp
        );
    }

    /// <inheritdoc/>
    public async Task StopPreviewAsync(Guid cameraId)
    {
        await StopSessionAsync(cameraId);
    }

    /// <inheritdoc/>
    public CameraRtpEndpointDto? GetRtpEndpoint(Guid cameraId)
    {
        if (
            !_sessions.TryGetValue(cameraId, out CameraPreviewSession? session)
            || !session.EnableRtp
        )
            return null;

        // 使用本机 IP（由调用方根据请求上下文确定更好，这里返回占位符）
        return _rtpServer.GetEndpoint(cameraId, "0.0.0.0");
    }

    // ─── 推流循环 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 单相机预览线程主体：持续抓帧 → 推送 SignalR → 推送 RTP
    /// </summary>
    private void RunPreviewLoop(Guid cameraId, bool enableRtp, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ITucamCameraService tucamService =
            scope.ServiceProvider.GetRequiredService<ITucamCameraService>();

        // 从相机设备映射获取 DeviceIndex
        if (!TryGetDeviceIndex(scope, cameraId, out int deviceIndex))
        {
            _logger.LogError("相机 {Id} 找不到对应的 DeviceIndex，预览无法启动", cameraId);
            return;
        }

        _logger.LogDebug("相机 {Id} (index={Idx}) 预览线程开始运行", cameraId, deviceIndex);

        try
        {
            tucamService.StartCaptureAsync(deviceIndex).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "相机 {Id} 开始采集失败", cameraId);
            _sessions.TryRemove(cameraId, out _);
            return;
        }

        long metricsLastSent = Environment.TickCount64;
        long signalRLastSent = 0; // 上一次向 SignalR 推帧的时间戳（毫秒）

        try
        {
            while (!cancellationToken.IsCancellationRequested && _sessions.ContainsKey(cameraId))
            {
                byte[] jpegFrame;
                try
                {
                    // 抓帧（最多等待 500ms，若超时跳过本帧继续循环）
                    jpegFrame = tucamService
                        .GrabFrameRawAsync(deviceIndex, 500)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "相机 {Id} 抓帧失败，跳过本帧", cameraId);
                    continue;
                }

                // SignalR 推帧：限速至 SignalRMaxFps，防止浏览器过载
                long now = Environment.TickCount64;
                if (now - signalRLastSent >= SignalRFrameIntervalMs)
                {
                    _ = PushFrameAsync(cameraId, jpegFrame);
                    signalRLastSent = now;
                }

                // RTP 副流推帧（不限速，全速运行，支持 120FPS+）
                if (enableRtp)
                {
                    try
                    {
                        _rtpServer.SendFrame(cameraId, jpegFrame);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "相机 {Id} RTP 推帧失败", cameraId);
                    }
                }

                // 周期性推送实时指标
                if (Environment.TickCount64 - metricsLastSent >= MetricsPushIntervalMs)
                {
                    _ = PushLiveMetricsAsync(cameraId, deviceIndex, tucamService);
                    metricsLastSent = Environment.TickCount64;
                }
            }
        }
        finally
        {
            try
            {
                tucamService.StopCaptureAsync(deviceIndex).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "相机 {Id} 停止采集时发生异常", cameraId);
            }

            if (enableRtp)
            {
                _rtpServer.RemoveSession(cameraId);
            }

            _logger.LogDebug("相机 {Id} 预览线程已退出", cameraId);
        }
    }

    /// <summary>
    /// 通过 SignalR 推送一帧 JPEG（fire-and-forget）
    /// </summary>
    private async Task PushFrameAsync(Guid cameraId, byte[] frame)
    {
        try
        {
            string cameraIdStr = cameraId.ToString();
            await _hubContext.Clients.All.ReceiveCameraFrameAsync(cameraIdStr, frame);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "相机 {Id} SignalR 推帧异常", cameraId);
        }
    }

    /// <summary>
    /// 收集并推送实时运行指标
    /// </summary>
    private async Task PushLiveMetricsAsync(
        Guid cameraId,
        int deviceIndex,
        ITucamCameraService tucamService
    )
    {
        try
        {
            int fpgaTemp = await tucamService.GetDeviceNumericInfoAsync(
                deviceIndex,
                TUCamIdInfo.FpgaTemperature
            );
            double sensorTemp = await tucamService.GetPropertyValueAsync(
                deviceIndex,
                TUCamIdProp.Temperature
            );
            double frameRate = await tucamService.GetPropertyValueAsync(
                deviceIndex,
                TUCamIdProp.FrameRate
            );
            int aeStatus = await tucamService.GetCapabilityValueAsync(
                deviceIndex,
                TUCamIdCapa.AutoExposure
            );
            int bufFrames = await tucamService.GetDeviceNumericInfoAsync(
                deviceIndex,
                TUCamIdInfo.CurrentBufFrames
            );

            CameraLiveMetricsDto metrics = new CameraLiveMetricsDto
            {
                FpgaTemperature = fpgaTemp,
                SensorTemperature = sensorTemp,
                FrameRate = frameRate,
                AeStatus = aeStatus,
                CurrentBufFrames = bufFrames,
            };

            await _hubContext.Clients.All.ReceiveLiveMetricsAsync(cameraId.ToString(), metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "相机 {Id} 实时指标推送失败", cameraId);
        }
    }

    /// <summary>
    /// 停止并清理指定相机的预览会话
    /// </summary>
    private async Task StopSessionAsync(Guid cameraId)
    {
        if (!_sessions.TryRemove(cameraId, out CameraPreviewSession? session))
            return;

        // 等待预览线程退出（最多 2 秒）
        if (session.Thread.IsAlive)
        {
            await Task.Run(() => session.Thread.Join(TimeSpan.FromSeconds(2)));
        }

        _logger.LogInformation("相机 {Id} 实时预览已停止", cameraId);
    }

    /// <summary>
    /// 根据相机 ID 查询对应的 SDK DeviceIndex
    /// </summary>
    private static bool TryGetDeviceIndex(IServiceScope scope, Guid cameraId, out int deviceIndex)
    {
        deviceIndex = 0;
        try
        {
            ICameraDeviceRepository repo =
                scope.ServiceProvider.GetRequiredService<ICameraDeviceRepository>();

            // 同步查询（线程上下文内）
            CameraDevice? camera = repo.GetAsync(cameraId).GetAwaiter().GetResult();
            if (camera == null)
                return false;
            deviceIndex = camera.DeviceIndex;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _cts.Cancel();
        _cts.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 相机预览会话状态
/// </summary>
internal sealed class CameraPreviewSession
{
    public CameraPreviewSession(Thread thread, string? connectionId, bool enableRtp)
    {
        Thread = thread;
        ConnectionId = connectionId;
        EnableRtp = enableRtp;
    }

    /// <summary>推流线程</summary>
    public Thread Thread { get; }

    /// <summary>发起预览的 SignalR 连接 ID（null 表示全组播）</summary>
    public string? ConnectionId { get; }

    /// <summary>是否启用 RTP 副流</summary>
    public bool EnableRtp { get; }
}
