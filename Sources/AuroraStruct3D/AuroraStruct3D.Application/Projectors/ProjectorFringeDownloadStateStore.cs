using System.Collections.Concurrent;
using AuroraStruct3D.Projectors.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影机条纹下载状态内存存储。
/// 用于支撑“接口立即返回 + 前端可刷新恢复”的状态查询与推送。
/// </summary>
public class ProjectorFringeDownloadStateStore : ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, ProjectorFringeDownloadStatusDto> _states = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// 获取指定投影机当前状态；若不存在则返回 Idle。
    /// </summary>
    public ProjectorFringeDownloadStatusDto Get(Guid projectorId)
    {
        if (_states.TryGetValue(projectorId, out ProjectorFringeDownloadStatusDto? status))
        {
            return Clone(status);
        }

        return new ProjectorFringeDownloadStatusDto
        {
            ProjectorId = projectorId,
            Status = "Idle",
            Progress = 0,
        };
    }

    /// <summary>
    /// 尝试将指定投影机置为 Running；若已在运行则返回 false。
    /// </summary>
    public bool TryStart(Guid projectorId, out ProjectorFringeDownloadStatusDto status)
    {
        lock (_syncRoot)
        {
            ProjectorFringeDownloadStatusDto current = Get(projectorId);
            if (string.Equals(current.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                status = current;
                return false;
            }

            ProjectorFringeDownloadStatusDto next = new()
            {
                ProjectorId = projectorId,
                Status = "Running",
                Progress = 0,
                ErrorMessage = null,
            };

            _states[projectorId] = next;
            status = Clone(next);
            return true;
        }
    }

    /// <summary>
    /// 更新进度为 Running。
    /// </summary>
    public ProjectorFringeDownloadStatusDto UpdateProgress(Guid projectorId, int progress)
    {
        ProjectorFringeDownloadStatusDto next = new()
        {
            ProjectorId = projectorId,
            Status = "Running",
            Progress = Math.Clamp(progress, 0, 100),
            ErrorMessage = null,
        };
        _states.AddOrUpdate(projectorId, next, (_, _) => next);
        return Clone(next);
    }

    /// <summary>
    /// 标记下载完成。
    /// </summary>
    public ProjectorFringeDownloadStatusDto Complete(Guid projectorId)
    {
        ProjectorFringeDownloadStatusDto next = new()
        {
            ProjectorId = projectorId,
            Status = "Completed",
            Progress = 100,
            ErrorMessage = null,
        };
        _states.AddOrUpdate(projectorId, next, (_, _) => next);
        return Clone(next);
    }

    /// <summary>
    /// 标记下载失败。
    /// </summary>
    public ProjectorFringeDownloadStatusDto Fail(Guid projectorId, string? errorMessage)
    {
        ProjectorFringeDownloadStatusDto next = new()
        {
            ProjectorId = projectorId,
            Status = "Failed",
            Progress = 0,
            ErrorMessage = errorMessage,
        };
        _states.AddOrUpdate(projectorId, next, (_, _) => next);
        return Clone(next);
    }

    private static ProjectorFringeDownloadStatusDto Clone(ProjectorFringeDownloadStatusDto status)
    {
        return new ProjectorFringeDownloadStatusDto
        {
            ProjectorId = status.ProjectorId,
            Status = status.Status,
            Progress = status.Progress,
            ErrorMessage = status.ErrorMessage,
        };
    }
}
