using System.Collections.Concurrent;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Hubs;
using AuroraStruct3D.Sessions;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Streaming;

/// <summary>
/// 相机实时推流后台服务，同时实现 <see cref="ICameraStreamingService"/>。
/// 管理每台相机的预览会话：后台高优先级线程持续抓帧，
/// 通过 <see cref="CameraFrameBufferService"/> 写入帧缓冲，
/// 前端通过 /api/streaming/cameras/{id}/preview 的 HTTP MJPEG 流直连播放；
/// 同时可选 RTP/MJPEG UDP 副流。
/// </summary>
public class CameraPreviewService : ICameraStreamingService, IHostedService, IDisposable
{
    /// <summary>指标推送间隔（毫秒）——设为 500ms 以便对焦微调时得到及时评分反馈</summary>
    private const int MetricsPushIntervalMs = 500;

    /// <summary>
    /// 预览帧最大宽度，原始分辨率用于快照。
    /// 预览用中等分辨率即可：降低带宽和内存占用，保持流畅。
    /// </summary>
    private const int PreviewMaxWidth = 960;

    /// <summary>
    /// GrabFrameRawAsync 的 jpegQuality 参数已无实际意义（当前返回 BMP 无压缩），
    /// 但为保持兼容传 0 即可。
    /// </summary>
    private const int PreviewJpegQualityUnused = 0;

    /// <summary>断线自动停止后同步数据库状态的最长等待时间（秒）</summary>
    private const int AutoStopStatusSyncTimeoutSeconds = 5;

    /// <summary>
    /// Software 触发模式下预览自动触发节拍（FPS）。
    /// 30FPS 与页面预览目标一致；实际帧率仍受曝光时间和相机吞吐限制。
    /// </summary>
    private const int SoftwareTriggerPreviewFps = 30;

    /// <summary>Software 触发模式下两次软件触发的最小间隔（毫秒）</summary>
    private const int SoftwareTriggerIntervalMs = 1000 / SoftwareTriggerPreviewFps;

    /// <summary>Standard 外触发模式下单次 WaitForFrame 超时（毫秒），等待硬件触发到来</summary>
    private const int ExternalTriggerWaitTimeoutMs = 8000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<CameraHub, ICameraHub> _hubContext;
    private readonly RtpMjpegServer _rtpServer;
    private readonly CameraFrameBufferService _frameBuffer;
    private readonly ILogger<CameraPreviewService> _logger;
    private readonly IDeviceOperationSessionManager _sessionManager;

    /// <summary>相机 ID → 预览会话（仅当预览激活时存在）</summary>
    private readonly ConcurrentDictionary<Guid, CameraPreviewSession> _sessions = new();

    /// <summary>
    /// 相机 ID → 宽限期定时停止任务的取消令牌。
    /// 用于在浏览器刷新、网络抖动等场景下保留 30s 续约窗口；
    /// 客户端在窗口内重新订阅时调用 <see cref="ReattachPreviewAsync"/> 取消该停止。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pendingStops = new();

    /// <summary>
    /// Per-camera 互斥锁，防止同一台相机的 Start/Stop 操作并发执行导致
    /// TUCAM_Buf_Alloc 返回 Excluded。不同相机使用不同的 key，互不阻塞，
    /// 两台相机可同时进行采集。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _cameraLocks = new();

    /// <summary>客户端断开后保留预览的宽限期（毫秒）</summary>
    private const int DisconnectGracePeriodMs = 30_000;

    private CancellationTokenSource _cts = new();
    private bool _disposed;

    public CameraPreviewService(
        IServiceScopeFactory scopeFactory,
        IHubContext<CameraHub, ICameraHub> hubContext,
        RtpMjpegServer rtpServer,
        CameraFrameBufferService frameBuffer,
        ILogger<CameraPreviewService> logger,
        IDeviceOperationSessionManager sessionManager
    )
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _rtpServer = rtpServer;
        _frameBuffer = frameBuffer;
        _logger = logger;
        _sessionManager = sessionManager;
    }

    // ─── IHostedService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.LogInformation("CameraPreviewService 已启动（HTTP MJPEG 流模式）");
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

    /// <summary>获取或创建指定相机的互斥信号量</summary>
    private SemaphoreSlim GetCameraLock(Guid cameraId) =>
        _cameraLocks.GetOrAdd(cameraId, _ => new SemaphoreSlim(1, 1));

    /// <inheritdoc/>
    public async Task StartPreviewAsync(
        Guid cameraId,
        string? connectionId,
        bool enableRtp,
        int imageRotationAngle,
        string? clientSessionId = null,
        bool forceSession = false
    )
    {
        // per-camera 互斥：防止并发 Start/Stop 造成 TUCAM_Buf_Alloc: Excluded。
        // 不同相机使用各自的信号量，两台相机可同时采集。
        SemaphoreSlim cameraLock = GetCameraLock(cameraId);
        await cameraLock.WaitAsync();
        try
        {
            await StartPreviewCoreAsync(
                cameraId,
                connectionId,
                enableRtp,
                imageRotationAngle,
                clientSessionId,
                forceSession
            );
        }
        finally
        {
            cameraLock.Release();
        }
    }

    /// <summary>真正执行预览启动的内部方法，调用方需已持有 per-camera 互斥锁</summary>
    private async Task StartPreviewCoreAsync(
        Guid cameraId,
        string? connectionId,
        bool enableRtp,
        int imageRotationAngle,
        string? clientSessionId,
        bool forceSession
    )
    {
        // 获取或续期设备独占会话（预览会话为无限期，直至预览停止才释放）
        if (!string.IsNullOrWhiteSpace(clientSessionId))
        {
            _sessionManager.TryAcquire(
                cameraId,
                DeviceType.Camera,
                clientSessionId,
                userId: null,
                userName: "PreviewSession",
                force: forceSession,
                neverExpire: true
            );
        }
        // 若已有会话，先停止（此处已在锁内，不会与其他 Start 并发）
        if (_sessions.ContainsKey(cameraId))
        {
            await StopSessionAsync(cameraId);
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ITucamCameraService tucamService =
            scope.ServiceProvider.GetRequiredService<ITucamCameraService>();

        if (!TryGetDeviceIndex(scope, cameraId, out int deviceIndex))
        {
            throw new InvalidOperationException(
                $"相机 {cameraId} 找不到对应的 DeviceIndex，预览无法启动"
            );
        }

        bool ownsCapture = !tucamService.IsCapturing(deviceIndex);
        try
        {
            await tucamService.StartCaptureAsync(deviceIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "相机 {Id} 开始采集失败，预览未启动", cameraId);
            throw;
        }

        CancellationToken token = _cts.Token;

        Thread thread = new Thread(() =>
            RunPreviewLoop(cameraId, deviceIndex, connectionId, enableRtp, ownsCapture, token)
        )
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = $"CameraPreview-{cameraId:N}",
        };

        CameraPreviewSession session = new CameraPreviewSession(
            thread,
            connectionId,
            enableRtp,
            deviceIndex,
            ownsCapture,
            imageRotationAngle
        );
        _sessions[cameraId] = session;
        try
        {
            thread.Start();
        }
        catch
        {
            _sessions.TryRemove(cameraId, out _);
            _frameBuffer.ClearCamera(cameraId);
            if (ownsCapture)
            {
                await tucamService.StopCaptureAsync(deviceIndex);
            }
            throw;
        }

        _logger.LogInformation(
            "相机 {Id} 实时预览已启动（HTTP MJPEG 流模式），EnableRtp={EnableRtp}, ConnectionId={ConnectionId}",
            cameraId,
            enableRtp,
            connectionId ?? "广播"
        );
    }

    /// <inheritdoc/>
    public async Task StopPreviewAsync(Guid cameraId)
    {
        // per-camera 互斥：防止 Stop 与并发 Start 交错
        SemaphoreSlim cameraLock = GetCameraLock(cameraId);
        await cameraLock.WaitAsync();
        try
        {
            await StopSessionAsync(cameraId);
        }
        finally
        {
            cameraLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task UpdatePreviewRotationAsync(Guid cameraId, int imageRotationAngle)
    {
        if (_sessions.TryGetValue(cameraId, out CameraPreviewSession? session))
        {
            session.SetImageRotationAngle(imageRotationAngle);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 客户端连接断开时调用：进入 30s 宽限期，期间若客户端通过 <see cref="ReattachPreviewAsync"/>
    /// 重新订阅则取消停止，否则到期后自动释放该会话。
    /// 用于浏览器刷新、关闭标签页、网络抖动等场景。
    /// </summary>
    /// <param name="connectionId">SignalR 连接 ID</param>
    public Task StopPreviewByConnectionAsync(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.CompletedTask;
        }

        List<Guid> cameraIds = _sessions
            .Where(pair =>
                string.Equals(pair.Value.ConnectionId, connectionId, StringComparison.Ordinal)
            )
            .Select(pair => pair.Key)
            .ToList();

        foreach (Guid cameraId in cameraIds)
        {
            ScheduleDelayedStop(cameraId, connectionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 客户端在宽限期内重新订阅相机预览：取消挂起的延迟停止，并将会话连接 ID 续约为新连接。
    /// 由 <c>GetCameraSnapshotStateAsync</c> 在前端调用时间接触发。
    /// </summary>
    /// <param name="cameraId">相机 ID</param>
    /// <param name="newConnectionId">新的 SignalR 连接 ID；为空表示切换为全组播模式</param>
    /// <returns>true 表示有挂起的停止被取消（即续约成功）；false 表示无需取消</returns>
    public bool ReattachPreviewAsync(Guid cameraId, string? newConnectionId)
    {
        bool canceled = false;
        if (_pendingStops.TryRemove(cameraId, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略：可能已被释放
            }
            try
            {
                cts.Dispose();
            }
            catch
            {
                // 忽略
            }
            canceled = true;
        }

        if (_sessions.TryGetValue(cameraId, out CameraPreviewSession? session))
        {
            session.ReassignConnectionId(newConnectionId);
        }

        if (canceled)
        {
            _logger.LogInformation(
                "相机 {Id} 预览会话续约成功，新 ConnectionId={ConnectionId}",
                cameraId,
                newConnectionId ?? "广播"
            );
        }

        return canceled;
    }

    /// <summary>
    /// 安排相机预览在 30s 宽限期后自动停止；若到期前已调用 <see cref="ReattachPreviewAsync"/>，则取消停止。
    /// </summary>
    private void ScheduleDelayedStop(Guid cameraId, string staleConnectionId)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        _pendingStops.AddOrUpdate(
            cameraId,
            cts,
            (_, oldCts) =>
            {
                try
                {
                    oldCts.Cancel();
                }
                catch
                {
                    // 忽略
                }
                try
                {
                    oldCts.Dispose();
                }
                catch
                {
                    // 忽略
                }
                return cts;
            }
        );

        _logger.LogInformation(
            "相机 {Id} 已进入 {GraceMs}ms 宽限期，客户端可在期间重连恢复预览（旧 ConnectionId={ConnectionId}）",
            cameraId,
            DisconnectGracePeriodMs,
            staleConnectionId
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DisconnectGracePeriodMs, cts.Token);

                // 宽限期到期，若会话仍指向旧连接（未被续约）则真正停止
                if (
                    _sessions.TryGetValue(cameraId, out CameraPreviewSession? session)
                    && string.Equals(
                        session.ConnectionId,
                        staleConnectionId,
                        StringComparison.Ordinal
                    )
                )
                {
                    CameraPreviewSession? stoppedSession = await StopSessionAsync(cameraId);
                    if (stoppedSession is not null)
                    {
                        await UpdateCameraStatusAfterAutoStopAsync(
                            cameraId,
                            stoppedSession.DeviceIndex,
                            staleConnectionId
                        );
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端在宽限期内重连，已取消
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "相机 {Id} 宽限期到期清理异常", cameraId);
            }
            finally
            {
                _pendingStops.TryRemove(
                    new KeyValuePair<Guid, CancellationTokenSource>(cameraId, cts)
                );
                try
                {
                    cts.Dispose();
                }
                catch
                {
                    // 忽略
                }
            }
        });
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

    /// <inheritdoc/>
    public bool IsPreviewActive(Guid cameraId)
    {
        return _sessions.ContainsKey(cameraId);
    }

    /// <inheritdoc/>
    public async Task NotifyGenICamNodesChangedAsync(
        Guid cameraId,
        List<GenICamNodeChangeDto> changes
    )
    {
        if (changes is null || changes.Count == 0)
        {
            return;
        }

        try
        {
            await _hubContext.Clients.All.OnGenICamNodesChangedAsync(cameraId.ToString(), changes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "相机 {Id} OnGenICamNodesChangedAsync 推送失败（共 {Count} 项）",
                cameraId,
                changes.Count
            );
        }
    }

    /// <inheritdoc/>
    public async Task NotifyGenICamNodeMapReloadedAsync(Guid cameraId, DateTime enumeratedAt)
    {
        try
        {
            await _hubContext.Clients.All.OnGenICamNodeMapReloadedAsync(
                cameraId.ToString(),
                enumeratedAt
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "相机 {Id} OnGenICamNodeMapReloadedAsync 推送失败", cameraId);
        }
    }

    // ─── 推流循环 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 单相机预览线程主体：持续抓帧 → 写入 HTTP MJPEG 帧缓冲 → 推送 RTP
    /// </summary>
    private void RunPreviewLoop(
        Guid cameraId,
        int deviceIndex,
        string? connectionId,
        bool enableRtp,
        bool ownsCapture,
        CancellationToken cancellationToken
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ITucamCameraService tucamService =
            scope.ServiceProvider.GetRequiredService<ITucamCameraService>();

        _logger.LogInformation("相机 {Id} (index={Idx}) 预览线程开始运行", cameraId, deviceIndex);

        long triggerMode = 0;
        try
        {
            triggerMode = tucamService
                .GetGenICamIntAsync(deviceIndex, "TriggerMode")
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "相机 {Id} 读取 TriggerMode 失败，按 FreeRunning 模式预览",
                cameraId
            );
        }

        _logger.LogInformation(
            "相机 {Id} 预览线程采用 TriggerMode={Mode} 策略",
            cameraId,
            triggerMode switch
            {
                1 => "Standard(外触发)",
                2 => "Software(软触发)",
                _ => "FreeRunning(自由运行)",
            }
        );

        int waitTimeoutMs = triggerMode == 1 ? ExternalTriggerWaitTimeoutMs : 2000;

        long metricsLastSent = Environment.TickCount64;
        long softwareTriggerLastSent = 0;
        int consecutiveTimeouts = 0;

        const int DrainThrottleMs = 20;
        /// <summary>
        /// 预热帧数量：会话启动阶段无条件抓帧发布，避免 HTTP 订阅者到达时
        /// 尚未发布任何帧 → HasFrame=false → 订阅者等待超时。
        /// 设为 3 以应对软件触发的前几帧曝光不稳定的情况。
        /// </summary>
        const int WarmupFrames = 3;
        int warmupRemaining = WarmupFrames;

        try
        {
            while (!cancellationToken.IsCancellationRequested && _sessions.ContainsKey(cameraId))
            {
                if (!_sessions.TryGetValue(cameraId, out CameraPreviewSession? currentSession))
                {
                    break;
                }

                if (triggerMode == 2)
                {
                    long nowTrig = Environment.TickCount64;
                    if (nowTrig - softwareTriggerLastSent >= SoftwareTriggerIntervalMs)
                    {
                        try
                        {
                            tucamService
                                .DoSoftwareTriggerAsync(deviceIndex)
                                .GetAwaiter()
                                .GetResult();
                            softwareTriggerLastSent = nowTrig;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "相机 {Id} 预览线程软件触发失败，本轮跳过",
                                cameraId
                            );
                        }
                    }
                }

                // 有 HTTP MJPEG 订阅者 或 RTP 副流开启 或 仍在预热阶段 才真实抓帧；
                // 否则 Drain 丢帧以防止 SDK 缓存队列积压。
                // 预热 3 帧：保证 HTTP MJPEG 端点在 HasFrame 查询时总能命中，订阅者无需等待。
                bool hasHttpSubscribers = _frameBuffer.GetSubscriberCount(cameraId) > 0;
                bool needPushFrame = hasHttpSubscribers || enableRtp || warmupRemaining > 0;

                if (needPushFrame)
                {
                    byte[] bmpFrame;
                    try
                    {
                        bmpFrame = tucamService
                            .GrabFrameRawAsync(
                                deviceIndex,
                                waitTimeoutMs,
                                PreviewMaxWidth,
                                PreviewJpegQualityUnused,
                                currentSession.ImageRotationAngle
                            )
                            .GetAwaiter()
                            .GetResult();
                        consecutiveTimeouts = 0;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (InvalidOperationException ex)
                        when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        if (triggerMode == 1)
                        {
                            continue;
                        }

                        consecutiveTimeouts++;
                        if (consecutiveTimeouts == 1)
                        {
                            _logger.LogInformation(
                                "相机 {Id} (index={Idx}) WaitForFrame 首次超时（TriggerMode={Mode}），可能 SDK 采集线程未启动或 USB 带宽不足；详情：{Msg}",
                                cameraId,
                                deviceIndex,
                                triggerMode,
                                ex.Message
                            );
                        }
                        else if (consecutiveTimeouts % 2 == 0)
                        {
                            _logger.LogWarning(
                                "相机 {Id} (index={Idx}) 已连续 {Count} 次 WaitForFrame 超时（TriggerMode={Mode}），SDK 采集线程可能已退出，建议停止预览后重新启动",
                                cameraId,
                                deviceIndex,
                                consecutiveTimeouts,
                                triggerMode
                            );
                        }
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "相机 {Id} 抓帧失败，跳过本帧", cameraId);
                        continue;
                    }

                    // 写入 HTTP MJPEG 帧缓冲：有订阅者或预热阶段都写一次，
                    // 避免订阅者刚到达时 HasFrame=false 被迫等待。
                    if (hasHttpSubscribers || warmupRemaining > 0)
                    {
                        _frameBuffer.PublishFrame(cameraId, bmpFrame);
                    }
                    // 消费 1 个预热配额
                    if (warmupRemaining > 0)
                    {
                        warmupRemaining--;
                    }

                    if (enableRtp)
                    {
                        try
                        {
                            // RTP 协议定义为 JPEG（RFC 2435），此处仍发送 JPEG；
                            // 若未来 RTP 也需要 BMP，需单独实现 RTP/BMP 封装。
                            // 当前 GrabFrameRawAsync 返回 BMP，直接发送到 RTP 订阅者时不做转码以避免性能损失；
                            // 实际 RTP 订阅者（VLC 等）需要 JPEG，因此这里的 RTP 通道仅在调用方显式启用
                            // 并接受 BMP 格式时使用。
                            _rtpServer.SendFrame(cameraId, bmpFrame);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "相机 {Id} RTP 推帧失败", cameraId);
                        }
                    }
                }
                else
                {
                    long iterStart = Environment.TickCount64;
                    int drainTimeoutMs = Math.Min(waitTimeoutMs, DrainThrottleMs - 2);
                    try
                    {
                        bool drained = tucamService
                            .DrainFrameAsync(deviceIndex, drainTimeoutMs)
                            .GetAwaiter()
                            .GetResult();
                        if (drained)
                        {
                            consecutiveTimeouts = 0;
                        }
                        else if (triggerMode != 1)
                        {
                            consecutiveTimeouts++;
                            if (consecutiveTimeouts % 25 == 0)
                            {
                                _logger.LogWarning(
                                    "相机 {Id} (index={Idx}) drain 已连续 {Count} 次超时（TriggerMode={Mode}），SDK 采集线程可能已退出",
                                    cameraId,
                                    deviceIndex,
                                    consecutiveTimeouts,
                                    triggerMode
                                );
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "相机 {Id} drain 帧失败，跳过", cameraId);
                    }

                    long elapsed = Environment.TickCount64 - iterStart;
                    int sleepMs = (int)Math.Max(0, DrainThrottleMs - elapsed);
                    if (sleepMs > 0)
                    {
                        Thread.Sleep(sleepMs);
                    }
                }

                if (Environment.TickCount64 - metricsLastSent >= MetricsPushIntervalMs)
                {
                    PushLiveMetricsAsync(cameraId, deviceIndex, tucamService, connectionId)
                        .GetAwaiter()
                        .GetResult();
                    metricsLastSent = Environment.TickCount64;
                }
            }
        }
        finally
        {
            if (ownsCapture)
            {
                try
                {
                    tucamService.StopCaptureAsync(deviceIndex).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "相机 {Id} 停止采集时发生异常", cameraId);
                }
            }

            if (enableRtp)
            {
                _rtpServer.RemoveSession(cameraId);
            }

            // 清理帧缓冲：立即释放订阅者持有的大对象引用
            _frameBuffer.ClearCamera(cameraId);

            _logger.LogDebug("相机 {Id} 预览线程已退出", cameraId);
        }
    }

    /// <summary>
    /// 收集并推送实时运行指标（仍通过 SignalR，因为低频且轻量）
    /// </summary>
    private async Task PushLiveMetricsAsync(
        Guid cameraId,
        int deviceIndex,
        ITucamCameraService tucamService,
        string? connectionId
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
                FocusScore = 0,
                ApertureScore = 0,
                ApertureHint = 0,
            };

            string cameraIdStr = cameraId.ToString();
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                await _hubContext.Clients.All.ReceiveLiveMetricsAsync(cameraIdStr, metrics);
            }
            else
            {
                await _hubContext
                    .Clients.Client(connectionId)
                    .ReceiveLiveMetricsAsync(cameraIdStr, metrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "相机 {Id} 实时指标推送失败", cameraId);
        }
    }

    /// <summary>
    /// 停止并清理指定相机的预览会话
    /// </summary>
    private async Task<CameraPreviewSession?> StopSessionAsync(Guid cameraId)
    {
        if (!_sessions.TryRemove(cameraId, out CameraPreviewSession? session))
            return null;

        // 先主动打断采集等待，避免 Standard 外触发模式下线程仍阻塞在 WaitForFrame。
        if (session.Thread.IsAlive && session.OwnsCapture)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ITucamCameraService tucamService =
                    scope.ServiceProvider.GetRequiredService<ITucamCameraService>();

                if (tucamService.IsCapturing(session.DeviceIndex))
                {
                    await tucamService.StopCaptureAsync(session.DeviceIndex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "相机 {Id} StopSession 主动停止采集失败", cameraId);
            }
        }

        bool joined = true;
        if (session.Thread.IsAlive)
        {
            joined = await Task.Run(() => session.Thread.Join(TimeSpan.FromSeconds(5)));
        }

        // 清理帧缓冲
        _frameBuffer.ClearCamera(cameraId);

        // 系统强制释放：null 表示不检查 clientSessionId（无论谁持有都释放）
        _sessionManager.Release(cameraId, clientSessionId: null);

        if (joined)
        {
            _logger.LogInformation("相机 {Id} 实时预览已停止", cameraId);
        }
        else
        {
            _logger.LogWarning("相机 {Id} 预览线程在超时时间内未退出，会话已移除", cameraId);
        }

        return session;
    }

    /// <summary>
    /// 断线自动停止预览后，同步数据库状态并广播给前端。
    /// </summary>
    private async Task UpdateCameraStatusAfterAutoStopAsync(
        Guid cameraId,
        int deviceIndex,
        string connectionId
    )
    {
        if (
            _sessions.TryGetValue(cameraId, out CameraPreviewSession? currentSession)
            && !string.Equals(currentSession.ConnectionId, connectionId, StringComparison.Ordinal)
        )
        {
            return;
        }

        try
        {
            using CancellationTokenSource syncTimeoutCts = new(
                TimeSpan.FromSeconds(AutoStopStatusSyncTimeoutSeconds)
            );
            CancellationToken syncCancellationToken = syncTimeoutCts.Token;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IUnitOfWorkManager unitOfWorkManager =
                scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            ICameraDeviceRepository repository =
                scope.ServiceProvider.GetRequiredService<ICameraDeviceRepository>();
            ITucamCameraService tucamService =
                scope.ServiceProvider.GetRequiredService<ITucamCameraService>();

            using IUnitOfWork unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
            CameraDevice camera = await repository.GetAsync(
                cameraId,
                cancellationToken: syncCancellationToken
            );
            CameraStatus status =
                tucamService.IsCapturing(deviceIndex) ? CameraStatus.Capturing
                : tucamService.IsCameraOpen(deviceIndex) ? CameraStatus.Ready
                : CameraStatus.Closed;

            camera.SetStatus(status);
            await repository.UpdateAsync(
                camera,
                autoSave: false,
                cancellationToken: syncCancellationToken
            );
            await unitOfWork.CompleteAsync(syncCancellationToken);

            await _hubContext.Clients.All.ReceiveCameraStateAsync(
                new CameraStateDto
                {
                    CameraId = cameraId,
                    Status = (int)status,
                    StatusText = status.ToString(),
                    IsCapturing = status == CameraStatus.Capturing,
                    IsXmlLoaded = tucamService.GetCachedNodeMap(deviceIndex) != null,
                }
            );
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "相机 {Id} 断线自动停止预览后同步状态超时，已跳过本次状态落库",
                cameraId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "相机 {Id} 断线自动停止预览后同步状态失败", cameraId);
        }
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
    public CameraPreviewSession(
        Thread thread,
        string? connectionId,
        bool enableRtp,
        int deviceIndex,
        bool ownsCapture,
        int imageRotationAngle
    )
    {
        Thread = thread;
        ConnectionId = connectionId;
        EnableRtp = enableRtp;
        DeviceIndex = deviceIndex;
        OwnsCapture = ownsCapture;
        ImageRotationAngle = imageRotationAngle;
    }

    /// <summary>推流线程</summary>
    public Thread Thread { get; }

    /// <summary>发起预览的 SignalR 连接 ID（null 表示全组播）——用于状态推送和宽限期判断，不再用于按帧推送</summary>
    public string? ConnectionId { get; private set; }

    /// <summary>
    /// 在 SignalR 客户端重连后重新绑定连接 ID，用于宽限期内的会话续约。
    /// </summary>
    public void ReassignConnectionId(string? newConnectionId)
    {
        ConnectionId = newConnectionId;
    }

    /// <summary>是否启用 RTP 副流</summary>
    public bool EnableRtp { get; }

    /// <summary>SDK 设备索引</summary>
    public int DeviceIndex { get; }

    /// <summary>本预览会话是否负责停止采集</summary>
    public bool OwnsCapture { get; }

    /// <summary>图像顺时针旋转角度（度）</summary>
    public int ImageRotationAngle { get; private set; }

    /// <summary>
    /// 更新图像顺时针旋转角度。
    /// </summary>
    /// <param name="imageRotationAngle">旋转角度，仅支持 0、90、180、270 度。</param>
    public void SetImageRotationAngle(int imageRotationAngle)
    {
        ImageRotationAngle = imageRotationAngle;
    }
}
