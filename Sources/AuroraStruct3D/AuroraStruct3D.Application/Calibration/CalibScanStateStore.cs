using System.Collections.Concurrent;
using AuroraStruct3D.Calibration.Dtos;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描会话状态存储（进程内）。
/// </summary>
public class CalibScanStateStore
{
    private readonly ConcurrentDictionary<Guid, CalibScanSessionState> _sessions = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _metricLoops = new();

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
    public CalibScanSessionState Start(Guid calibProjectId, CalibScanMode scanMode)
    {
        DateTime now = DateTime.UtcNow;
        CalibScanSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ =>
                new CalibScanSessionState
                {
                    CalibProjectId = calibProjectId,
                    ScanMode = scanMode,
                    State = CalibScanRunState.Running,
                    IsRunning = true,
                    StartedAt = now,
                    LastUpdatedAt = now,
                    ErrorMessage = null,
                    LatestMetrics = BuildInitialMetrics(now),
                },
            (_, old) =>
            {
                old.ScanMode = scanMode;
                old.State = CalibScanRunState.Running;
                old.IsRunning = true;
                old.StartedAt ??= now;
                old.LastUpdatedAt = now;
                old.ErrorMessage = null;
                old.LatestMetrics ??= BuildInitialMetrics(now);
                return old;
            }
        );

        return session;
    }

    /// <summary>
    /// 停止指定项目会话；若不存在则返回 null。
    /// </summary>
    public CalibScanSessionState? Stop(Guid calibProjectId)
    {
        if (!_sessions.TryGetValue(calibProjectId, out CalibScanSessionState? session))
        {
            return null;
        }

        if (_metricLoops.TryRemove(calibProjectId, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }
            finally
            {
                cts.Dispose();
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
            _ =>
                new CalibScanSessionState
                {
                    CalibProjectId = calibProjectId,
                    ScanMode = CalibScanMode.TwoCamera0Light,
                    State = CalibScanRunState.Failed,
                    IsRunning = false,
                    LastUpdatedAt = now,
                    ErrorMessage = errorMessage,
                    LatestMetrics = BuildInitialMetrics(now),
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
        session.LastUpdatedAt = DateTime.UtcNow;
        return session;
    }

    /// <summary>
    /// 启动指定项目的实时指标推送循环。
    /// </summary>
    public void StartMetricLoop(
        Guid calibProjectId,
        Func<long, Task<CalibScanMetricsDto>> buildMetricsAsync,
        Func<CalibScanMetricsDto, Task> onMetricAsync
    )
    {
        if (_metricLoops.TryRemove(calibProjectId, out CancellationTokenSource? oldCts))
        {
            try
            {
                oldCts.Cancel();
            }
            catch
            {
                // 忽略
            }
            finally
            {
                oldCts.Dispose();
            }
        }

        CancellationTokenSource cts = new();
        _metricLoops[calibProjectId] = cts;

        _ = Task.Run(async () =>
        {
            long frameIndex = 0;
            while (!cts.IsCancellationRequested)
            {
                frameIndex++;
                CalibScanMetricsDto metrics;
                try
                {
                    metrics = await buildMetricsAsync(frameIndex);
                }
                catch
                {
                    metrics = new CalibScanMetricsDto
                    {
                        Fps = 0,
                        DepthValidRate = 0,
                        Confidence = 0,
                        FrameIndex = frameIndex,
                        Timestamp = DateTime.UtcNow,
                    };
                }

                CalibScanSessionState? session = UpdateMetrics(calibProjectId, metrics);
                if (session is null || !session.IsRunning)
                {
                    break;
                }

                await onMetricAsync(metrics);

                try
                {
                    await Task.Delay(1000, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    private static CalibScanMetricsDto BuildInitialMetrics(DateTime now)
    {
        return new CalibScanMetricsDto
        {
            Fps = 0,
            DepthValidRate = 0,
            Confidence = 0,
            FrameIndex = 0,
            Timestamp = now,
        };
    }
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
}
