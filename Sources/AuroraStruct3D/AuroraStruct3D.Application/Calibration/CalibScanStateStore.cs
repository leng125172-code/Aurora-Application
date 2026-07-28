using System.Collections.Concurrent;
using AuroraStruct3D.Calibration.Dtos;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描会话状态存储（进程内）。
/// </summary>
public class CalibScanStateStore
{
    private readonly ConcurrentDictionary<Guid, CalibScanSessionState> _sessions = new();
    private readonly ConcurrentDictionary<Guid, ScanLoopHandle> _scanLoops = new();

    /// <summary>
    /// 获取指定项目会话状态。
    /// </summary>
    public CalibScanSessionState? TryGet(Guid calibProjectId)
    {
        return _sessions.TryGetValue(calibProjectId, out CalibScanSessionState? session)
            ? session
            : null;
    }

    /// <summary>
    /// 启动或覆盖会话状态。
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="scanMode">扫描模式</param>
    /// <param name="totalFrameCount">每轮总帧数（已含横竖两个方向）</param>
    public CalibScanSessionState Start(Guid calibProjectId, CalibScanMode scanMode, int totalFrameCount)
    {
        DateTime now = DateTime.UtcNow;
        CalibScanSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ => new CalibScanSessionState
            {
                CalibProjectId = calibProjectId,
                ScanMode = scanMode,
                State = CalibScanRunState.Running,
                IsRunning = true,
                StartedAt = now,
                LastUpdatedAt = now,
                ErrorMessage = null,
                LatestMetrics = BuildInitialMetrics(now, totalFrameCount),
                PatternCount = totalFrameCount, // 每轮总帧数（已含横竖两个方向）
            },
            (_, old) =>
            {
                old.ScanMode = scanMode;
                old.State = CalibScanRunState.Running;
                old.IsRunning = true;
                old.StartedAt ??= now;
                old.LastUpdatedAt = now;
                old.ErrorMessage = null;
                old.PatternCount = totalFrameCount;
                old.CurrentRoundIndex = 0;
                old.CurrentFrameIndexInRound = 0;
                // 每次启动都必须建立一份新的指标快照。保留上一次会话的
                // LatestMetrics 会导致前端持续显示旧的 0/0 或上一轮帧号。
                old.LatestMetrics = BuildInitialMetrics(now, totalFrameCount);
                return old;
            }
        );

        return session;
    }

    /// <summary>
    /// 停止指定项目会话；若不存在则返回 null。
    /// </summary>
    public async Task<CalibScanSessionState?> StopAsync(Guid calibProjectId)
    {
        if (!_sessions.TryGetValue(calibProjectId, out CalibScanSessionState? session))
        {
            return null;
        }

        if (_scanLoops.TryRemove(calibProjectId, out ScanLoopHandle? handle))
        {
            try
            {
                await handle.CancellationTokenSource.CancelAsync();
                await handle.Completion;
            }
            catch (OperationCanceledException)
            {
                // 扫描循环按约定响应取消。
            }
            catch
            {
                // 循环体异常由应用服务记录；停止清理仍继续。
            }
            finally
            {
                handle.CancellationTokenSource.Dispose();
            }
        }

        session.State = CalibScanRunState.Idle;
        session.IsRunning = false;
        session.LastUpdatedAt = DateTime.UtcNow;
        session.ErrorMessage = null;
        return session;
    }

    /// <summary>
    /// 标记会话失败。
    /// </summary>
    public CalibScanSessionState Fail(Guid calibProjectId, string errorMessage)
    {
        DateTime now = DateTime.UtcNow;
        CalibScanSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ => new CalibScanSessionState
            {
                CalibProjectId = calibProjectId,
                ScanMode = CalibScanMode.TwoCamera0Light,
                State = CalibScanRunState.Failed,
                IsRunning = false,
                LastUpdatedAt = now,
                ErrorMessage = errorMessage,
                LatestMetrics = BuildInitialMetrics(now, 0),
            },
            (_, old) =>
            {
                old.State = CalibScanRunState.Failed;
                old.IsRunning = false;
                old.LastUpdatedAt = now;
                old.ErrorMessage = errorMessage;
                return old;
            }
        );

        return session;
    }

    /// <summary>
    /// 更新实时指标。
    /// </summary>
    public CalibScanSessionState? UpdateMetrics(Guid calibProjectId, CalibScanMetricsDto metrics)
    {
        if (!_sessions.TryGetValue(calibProjectId, out CalibScanSessionState? session))
        {
            return null;
        }

        session.LatestMetrics = metrics;
        session.CurrentRoundIndex = metrics.RoundIndex;
        session.CurrentFrameIndexInRound = metrics.FrameIndexInRound;
        session.LastUpdatedAt = DateTime.UtcNow;
        return session;
    }

    /// <summary>
    /// 启动指定项目的扫描循环。
    /// 循环体内部自行推送 frame/metrics，节奏由投影仪 T/N 指令和相机抓拍耗时自然控制，
    /// 不再由 store 调度固定延迟。
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="scanLoopAsync">
    /// 扫描循环体回调，传入 CancellationToken，循环体应自行处理取消。
    /// 循环体返回时若会话仍在运行，则继续下一轮调用。
    /// </param>
    public void StartScanLoop(Guid calibProjectId, Func<CancellationToken, Task> scanLoopAsync)
    {
        if (_scanLoops.TryRemove(calibProjectId, out ScanLoopHandle? oldHandle))
        {
            try
            {
                oldHandle.CancellationTokenSource.Cancel();
            }
            catch
            {
                // 忽略
            }
        }

        CancellationTokenSource cts = new();
        Task completion;

        // Task.Run 默认会捕获当前 HTTP 请求的 ExecutionContext，其中包含 ABP
        // Ambient UnitOfWork。请求结束后该工作单元及 DbContext 会被释放，后台
        // 扫描若继续继承它，后续仓储查询就会访问 disposed DbContext。
        // 后台循环拥有独立 DI Scope，各应用服务调用应自行创建工作单元。
        using (ExecutionContext.SuppressFlow())
        {
            completion = Task.Run(async () =>
            {
                try
                {
                    await scanLoopAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略
                }
                catch (Exception)
                {
                    // 循环体异常由调用方自行记录日志并标记会话失败
                }
                finally
                {
                    if (
                        _scanLoops.TryGetValue(calibProjectId, out ScanLoopHandle? current)
                        && ReferenceEquals(current.CancellationTokenSource, cts)
                    )
                    {
                        _scanLoops.TryRemove(calibProjectId, out _);
                    }
                }
            });
        }
        _scanLoops[calibProjectId] = new ScanLoopHandle(cts, completion);
    }

    private static CalibScanMetricsDto BuildInitialMetrics(DateTime now, int patternCount)
    {
        return new CalibScanMetricsDto
        {
            Fps = 0,
            DepthValidRate = 0,
            Confidence = 0,
            FrameIndex = 0,
            Timestamp = now,
            RoundIndex = 0,
            FrameIndexInRound = 0,
            PatternCount = patternCount,
            IsCrosshairDetected = false,
        };
    }

    private sealed record ScanLoopHandle(
        CancellationTokenSource CancellationTokenSource,
        Task Completion
    );
}

/// <summary>
/// Step6 会话内部状态对象。
/// </summary>
public class CalibScanSessionState
{
    public Guid CalibProjectId { get; set; }

    public CalibScanMode ScanMode { get; set; }

    public CalibScanRunState State { get; set; }

    public bool IsRunning { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime LastUpdatedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public CalibScanMetricsDto? LatestMetrics { get; set; }

    /// <summary>每轮总帧数（来自 Step3 CalibProjectorParam.PatternCount）</summary>
    public int PatternCount { get; set; }

    /// <summary>当前轮次序号</summary>
    public long CurrentRoundIndex { get; set; }

    /// <summary>当前轮内帧序号</summary>
    public int CurrentFrameIndexInRound { get; set; }
}
