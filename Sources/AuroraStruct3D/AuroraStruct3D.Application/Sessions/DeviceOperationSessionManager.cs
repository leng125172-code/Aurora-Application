using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AuroraStruct3D.Sessions;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Sessions;

/// <summary>
/// 设备独占操作会话管理器实现。
/// 单例，以内存 Dictionary 维护会话状态；Timer 每 10s 清理过期条目。
/// </summary>
public sealed class DeviceOperationSessionManager
    : IDeviceOperationSessionManager,
        ISingletonDependency,
        IDisposable
{
    /// <inheritdoc/>
    public event Action<DeviceSessionChangedDto>? SessionChanged;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    /// <summary>设备 ID → 当前会话信息</summary>
    private readonly Dictionary<Guid, DeviceOccupantInfo> _sessions = new();

    /// <summary>保护 _sessions 的同步锁（所有读写必须持锁）</summary>
    private readonly object _lock = new();

    /// <summary>定时清理过期会话</summary>
    private readonly Timer _cleanupTimer;

    public DeviceOperationSessionManager()
    {
        // 10s 后首次执行，之后每 10s 一次
        _cleanupTimer = new Timer(
            CleanupExpiredSessions,
            state: null,
            dueTime: TimeSpan.FromSeconds(10),
            period: TimeSpan.FromSeconds(10)
        );
    }

    /// <inheritdoc/>
    public DeviceSessionDto TryAcquire(
        Guid deviceId,
        DeviceType deviceType,
        string clientSessionId,
        string? userId,
        string userName,
        bool force = false,
        bool neverExpire = false
    )
    {
        DeviceSessionChangedDto? changedDto = null;
        DeviceSessionDto resultDto;

        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset? expiresAt = neverExpire ? null : now.Add(DefaultTtl);

            if (_sessions.TryGetValue(deviceId, out DeviceOccupantInfo? existing))
            {
                // 同 clientSessionId：续期
                if (existing.ClientSessionId == clientSessionId)
                {
                    // 若当前是无限期（预览），不降级为 TTL；否则续期
                    DateTimeOffset? newExpiry =
                        existing.ExpiresAt == null
                            ? null // 保持无限期
                            : (neverExpire ? null : now.Add(DefaultTtl));

                    DeviceOccupantInfo renewed = existing with { ExpiresAt = newExpiry };
                    _sessions[deviceId] = renewed;
                    return ToDto(renewed);
                }

                // 不同 clientSessionId：检查是否已过期
                if (existing.ExpiresAt.HasValue && existing.ExpiresAt.Value <= now)
                {
                    // 已自然过期，视为无人占用，正常 Acquire
                    DeviceOccupantInfo newSession = new(
                        deviceId,
                        deviceType,
                        clientSessionId,
                        userId,
                        userName,
                        now,
                        expiresAt
                    );
                    _sessions[deviceId] = newSession;
                    resultDto = ToDto(newSession);
                    changedDto = new DeviceSessionChangedDto
                    {
                        Action = DeviceSessionAction.Acquired,
                        NewOccupant = resultDto,
                        OldOccupant = null,
                    };
                }
                else if (force)
                {
                    // 强制接管
                    DeviceSessionDto oldDto = ToDto(existing);
                    DeviceOccupantInfo newSession = new(
                        deviceId,
                        deviceType,
                        clientSessionId,
                        userId,
                        userName,
                        now,
                        expiresAt
                    );
                    _sessions[deviceId] = newSession;
                    resultDto = ToDto(newSession);
                    changedDto = new DeviceSessionChangedDto
                    {
                        Action = DeviceSessionAction.ForceTaken,
                        NewOccupant = resultDto,
                        OldOccupant = oldDto,
                    };
                }
                else
                {
                    // 不同用户且不强制 → 拒绝，抛异常（锁内抛，锁外处理）
                    throw new DeviceOccupiedException(existing);
                }
            }
            else
            {
                // 设备无人占用
                DeviceOccupantInfo newSession = new(
                    deviceId,
                    deviceType,
                    clientSessionId,
                    userId,
                    userName,
                    now,
                    expiresAt
                );
                _sessions[deviceId] = newSession;
                resultDto = ToDto(newSession);
                changedDto = new DeviceSessionChangedDto
                {
                    Action = DeviceSessionAction.Acquired,
                    NewOccupant = resultDto,
                    OldOccupant = null,
                };
            }
        }

        // 锁外触发事件，避免死锁
        if (changedDto != null)
        {
            SessionChanged?.Invoke(changedDto);
        }

        return resultDto;
    }

    /// <inheritdoc/>
    public void Release(Guid deviceId, string? clientSessionId)
    {
        DeviceSessionChangedDto? changedDto = null;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(deviceId, out DeviceOccupantInfo? existing))
            {
                return; // 已不存在，幂等处理
            }

            // clientSessionId 为 null（系统强制释放）或匹配时才释放
            if (clientSessionId != null && existing.ClientSessionId != clientSessionId)
            {
                return; // 不是 owner，忽略
            }

            _sessions.Remove(deviceId);
            changedDto = new DeviceSessionChangedDto
            {
                Action = DeviceSessionAction.Released,
                NewOccupant = null,
                OldOccupant = ToDto(existing),
            };
        }

        if (changedDto != null)
        {
            SessionChanged?.Invoke(changedDto);
        }
    }

    /// <inheritdoc/>
    public void RenewExpiry(Guid deviceId, string clientSessionId)
    {
        lock (_lock)
        {
            if (
                !_sessions.TryGetValue(deviceId, out DeviceOccupantInfo? existing)
                || existing.ClientSessionId != clientSessionId
                || existing.ExpiresAt == null // 无限期（预览）：不修改
            )
            {
                return;
            }

            _sessions[deviceId] = existing with
            {
                ExpiresAt = DateTimeOffset.UtcNow.Add(DefaultTtl),
            };
        }
    }

    /// <inheritdoc/>
    public DeviceSessionDto? GetSession(Guid deviceId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(deviceId, out DeviceOccupantInfo? info)
                ? ToDto(info)
                : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DeviceSessionDto> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.Select(ToDto).ToList();
        }
    }

    // ─── 私有辅助 ─────────────────────────────────────────────────────────

    private static DeviceSessionDto ToDto(DeviceOccupantInfo info) =>
        new DeviceSessionDto
        {
            DeviceId = info.DeviceId,
            DeviceType = info.DeviceType,
            ClientSessionId = info.ClientSessionId,
            OccupantUserId = info.OccupantUserId,
            OccupantUserName = info.OccupantUserName,
            AcquiredAt = info.AcquiredAt,
            ExpiresAt = info.ExpiresAt,
        };

    /// <summary>清理所有已自然过期的会话（由 Timer 调用）</summary>
    private void CleanupExpiredSessions(object? state)
    {
        List<DeviceSessionChangedDto>? events = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            List<Guid> expired = _sessions
                .Where(kv => kv.Value.ExpiresAt.HasValue && kv.Value.ExpiresAt.Value <= now)
                .Select(kv => kv.Key)
                .ToList();

            if (expired.Count == 0)
            {
                return;
            }

            events = new List<DeviceSessionChangedDto>(expired.Count);
            foreach (Guid deviceId in expired)
            {
                if (_sessions.Remove(deviceId, out DeviceOccupantInfo? removed))
                {
                    events.Add(
                        new DeviceSessionChangedDto
                        {
                            Action = DeviceSessionAction.Released,
                            NewOccupant = null,
                            OldOccupant = ToDto(removed),
                        }
                    );
                }
            }
        }

        // 锁外触发事件
        if (events != null)
        {
            foreach (DeviceSessionChangedDto evt in events)
            {
                SessionChanged?.Invoke(evt);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
