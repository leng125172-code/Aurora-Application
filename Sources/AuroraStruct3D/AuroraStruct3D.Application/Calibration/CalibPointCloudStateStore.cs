using System.Collections.Concurrent;
using AuroraStruct3D.Calibration.Dtos;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step7 点云生成会话状态（进程内存储）。
/// </summary>
public class PointCloudSessionState
{
    public Guid CalibProjectId { get; set; }
    public PointCloudRunState State { get; set; } = PointCloudRunState.Idle;
    public bool IsRunning { get; set; }
    public int Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public string? PlyDownloadUrl { get; set; }
    public long? PlyFileSizeBytes { get; set; }

    public PointCloudStatusDto ToDto() =>
        new()
        {
            CalibProjectId = CalibProjectId,
            State = State,
            IsRunning = IsRunning,
            Progress = Progress,
            ProgressMessage = ProgressMessage,
            StartedAt = StartedAt,
            LastUpdatedAt = LastUpdatedAt,
            ErrorMessage = ErrorMessage,
            PlyDownloadUrl = PlyDownloadUrl,
            PlyFileSizeBytes = PlyFileSizeBytes,
        };
}

/// <summary>
/// Step7 点云生成状态存储（进程内，线程安全）。
/// </summary>
public class CalibPointCloudStateStore
{
    private readonly ConcurrentDictionary<Guid, PointCloudSessionState> _sessions = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningJobs = new();

    /// <summary>获取或创建空闲会话状态。</summary>
    public PointCloudSessionState GetOrCreate(Guid calibProjectId)
    {
        return _sessions.GetOrAdd(
            calibProjectId,
            id => new PointCloudSessionState { CalibProjectId = id }
        );
    }

    /// <summary>标记生成开始，返回取消令牌。</summary>
    public (PointCloudSessionState session, CancellationToken token) Start(Guid calibProjectId)
    {
        // 取消旧任务（若有）
        if (_runningJobs.TryRemove(calibProjectId, out CancellationTokenSource? oldCts))
        {
            try
            {
                oldCts.Cancel();
            }
            catch { }
            oldCts.Dispose();
        }

        CancellationTokenSource cts = new();
        _runningJobs[calibProjectId] = cts;

        DateTime now = DateTime.UtcNow;
        PointCloudSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ => new PointCloudSessionState
            {
                CalibProjectId = calibProjectId,
                State = PointCloudRunState.Running,
                IsRunning = true,
                Progress = 0,
                ProgressMessage = "准备中...",
                StartedAt = now,
                LastUpdatedAt = now,
                ErrorMessage = null,
                PlyDownloadUrl = null,
                PlyFileSizeBytes = null,
            },
            (_, old) =>
            {
                old.State = PointCloudRunState.Running;
                old.IsRunning = true;
                old.Progress = 0;
                old.ProgressMessage = "准备中...";
                old.StartedAt = now;
                old.LastUpdatedAt = now;
                old.ErrorMessage = null;
                old.PlyDownloadUrl = null;
                old.PlyFileSizeBytes = null;
                return old;
            }
        );

        return (session, cts.Token);
    }

    /// <summary>更新进度。</summary>
    public PointCloudStatusDto? UpdateProgress(
        Guid calibProjectId,
        int progress,
        string? message = null
    )
    {
        if (!_sessions.TryGetValue(calibProjectId, out PointCloudSessionState? session))
        {
            return null;
        }

        session.Progress = progress;
        session.ProgressMessage = message;
        session.LastUpdatedAt = DateTime.UtcNow;
        return session.ToDto();
    }

    /// <summary>标记生成完成。</summary>
    public PointCloudStatusDto Complete(
        Guid calibProjectId,
        string plyDownloadUrl,
        long fileSizeBytes
    )
    {
        _runningJobs.TryRemove(calibProjectId, out _);

        DateTime now = DateTime.UtcNow;
        PointCloudSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ => new PointCloudSessionState
            {
                CalibProjectId = calibProjectId,
                State = PointCloudRunState.Completed,
                IsRunning = false,
                Progress = 100,
                ProgressMessage = "点云生成完成",
                LastUpdatedAt = now,
                PlyDownloadUrl = plyDownloadUrl,
                PlyFileSizeBytes = fileSizeBytes,
            },
            (_, old) =>
            {
                old.State = PointCloudRunState.Completed;
                old.IsRunning = false;
                old.Progress = 100;
                old.ProgressMessage = "点云生成完成";
                old.LastUpdatedAt = now;
                old.PlyDownloadUrl = plyDownloadUrl;
                old.PlyFileSizeBytes = fileSizeBytes;
                return old;
            }
        );

        return session.ToDto();
    }

    /// <summary>标记生成失败。</summary>
    public PointCloudStatusDto Fail(Guid calibProjectId, string errorMessage)
    {
        _runningJobs.TryRemove(calibProjectId, out _);

        DateTime now = DateTime.UtcNow;
        PointCloudSessionState session = _sessions.AddOrUpdate(
            calibProjectId,
            _ => new PointCloudSessionState
            {
                CalibProjectId = calibProjectId,
                State = PointCloudRunState.Failed,
                IsRunning = false,
                Progress = 0,
                ProgressMessage = null,
                LastUpdatedAt = now,
                ErrorMessage = errorMessage,
            },
            (_, old) =>
            {
                old.State = PointCloudRunState.Failed;
                old.IsRunning = false;
                old.LastUpdatedAt = now;
                old.ErrorMessage = errorMessage;
                return old;
            }
        );

        return session.ToDto();
    }

    /// <summary>取消正在运行的生成任务。</summary>
    public bool Cancel(Guid calibProjectId)
    {
        if (!_runningJobs.TryRemove(calibProjectId, out CancellationTokenSource? cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
        }
        catch { }
        cts.Dispose();

        if (_sessions.TryGetValue(calibProjectId, out PointCloudSessionState? session))
        {
            session.State = PointCloudRunState.Idle;
            session.IsRunning = false;
            session.Progress = 0;
            session.LastUpdatedAt = DateTime.UtcNow;
        }

        return true;
    }
}
