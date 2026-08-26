using AuroraStruct3D.Cameras.Tucam;
using AuroraStruct3D.Cameras.Tucam.Interop;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 一台参与投影仪硬件触发采集的 TUCam 相机。
/// </summary>
internal interface ITucamTriggeredCameraChannel
{
    int RuntimeIndex { get; }
    Task StartAsync();
    Task StopAsync();
    Task<byte[]> GrabAsync(CancellationToken cancellationToken);
}

internal sealed record TucamTriggeredCamera(
    ITucamCameraService Service,
    int RuntimeIndex,
    int TimeoutMs,
    int ImageRotationAngle
) : ITucamTriggeredCameraChannel
{
    public Task StartAsync() =>
        Service.StartCaptureAsync(RuntimeIndex, TUCamCaptureMode.TriggerStandard);

    public Task StopAsync() => Service.StopCaptureAsync(RuntimeIndex);

    public Task<byte[]> GrabAsync(CancellationToken cancellationToken) =>
        Service.GrabFrameRawAsync(
            RuntimeIndex,
            TimeoutMs,
            maxWidth: 0,
            jpegQuality: 85,
            imageRotationAngle: ImageRotationAngle,
            cancellationToken: cancellationToken
        );
}

/// <summary>
/// 让一到两台相机同时保持 TriggerStandard 采集，并在每次投影仪触发前先并行进入
/// WaitForFrame。该顺序与 RK3588 上通过验证的 TucamFrameConcurrencyProbe 一致。
/// </summary>
internal sealed class TucamHardwareTriggerCaptureSession : IAsyncDisposable
{
    internal const int TriggerArmDelayMs = 50;

    private readonly IReadOnlyList<ITucamTriggeredCameraChannel> _cameras;
    private readonly List<ITucamTriggeredCameraChannel> _started = [];
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private bool _faulted;
    private bool _disposed;

    private TucamHardwareTriggerCaptureSession(
        IReadOnlyList<ITucamTriggeredCameraChannel> cameras
    )
    {
        _cameras = cameras;
    }

    public static async Task<TucamHardwareTriggerCaptureSession> StartAsync(
        IReadOnlyList<ITucamTriggeredCameraChannel> cameras,
        CancellationToken cancellationToken
    )
    {
        if (cameras.Count is < 1 or > 2)
            throw new ArgumentOutOfRangeException(
                nameof(cameras),
                "硬件触发采集会话只支持一台或两台相机。"
            );
        if (cameras.Select(x => x.RuntimeIndex).Distinct().Count() != cameras.Count)
            throw new ArgumentException("硬件触发采集会话不能重复使用同一台相机。", nameof(cameras));

        var session = new TucamHardwareTriggerCaptureSession(cameras);
        try
        {
            // SDK 生命周期调用保持主→从串行；Start 返回后两台相机可以同时等待触发沿。
            foreach (ITucamTriggeredCameraChannel camera in cameras)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await camera.StartAsync();
                session._started.Add(camera);
            }
            return session;
        }
        catch
        {
            await session.StopAllAsync(throwOnError: false);
            session._disposed = true;
            session._captureGate.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 两路 WaitForFrame 均已启动后才调用 triggerAsync，确保一次投影仪硬件沿对应同一对图像。
    /// 返回顺序与 StartAsync 的 cameras 参数一致。
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> CaptureAsync(
        Func<CancellationToken, Task> triggerAsync,
        CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
            throw new InvalidOperationException("硬件触发采集会话已故障，不能继续取帧。");

        await _captureGate.WaitAsync(cancellationToken);
        Task<byte[]>[] waiters = [];
        try
        {
            // GrabFrameRawAsync 内部是同步 SDK 等待，必须放在线程池线程中，才能在其阻塞时
            // 继续向投影仪发送 T/N。每台相机拥有独立 handle 和 frame buffer。
            waiters = _cameras
                .Select(camera =>
                    Task.Run(
                        () =>
                            camera.GrabAsync(cancellationToken),
                        CancellationToken.None
                    )
                )
                .ToArray();

            // 实机 A/B 测试采用 50ms，确保两个 WaitForFrame 都已进入 native 阻塞等待。
            await Task.Delay(TriggerArmDelayMs, cancellationToken);
            await triggerAsync(cancellationToken);
            return await Task.WhenAll(waiters);
        }
        catch
        {
            _faulted = true;
            // 投影仪命令失败或任一路取帧失败时立即 AbortWait/Stop，避免另一等待线程
            // 一直阻塞到 8~30 秒超时；随后观察所有任务，防止未观察异常。
            await StopAllAsync(throwOnError: false);
            if (waiters.Length > 0)
            {
                try
                {
                    await Task.WhenAll(waiters);
                }
                catch
                {
                    // 原始异常由外层 catch 保留。
                }
            }
            throw;
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _captureGate.WaitAsync();
        try
        {
            await StopAllAsync(throwOnError: true);
        }
        finally
        {
            _captureGate.Release();
            _captureGate.Dispose();
        }
    }

    private async Task StopAllAsync(bool throwOnError)
    {
        List<Exception>? errors = null;
        for (int i = _started.Count - 1; i >= 0; i--)
        {
            ITucamTriggeredCameraChannel camera = _started[i];
            try
            {
                await camera.StopAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }
        _started.Clear();

        if (throwOnError && errors is { Count: > 0 })
            throw new AggregateException("停止硬件触发相机会话失败。", errors);
    }
}
