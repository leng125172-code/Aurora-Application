using System.Collections.Concurrent;

namespace AuroraStruct3D.Streaming;

/// <summary>
/// Step6 在线扫描主/从相机最新帧缓冲。
/// 图像经 HTTP Multipart 输出，避免大帧占用 SignalR 消息通道。
/// </summary>
public sealed class CalibScanFrameBufferService
{
    public readonly record struct FrameKey(Guid ProjectId, int CameraRole);

    private sealed class FrameSlot
    {
        public byte[]? Frame;
        public long Version;
    }

    private readonly ConcurrentDictionary<FrameKey, FrameSlot> _slots = new();
    private readonly ConcurrentDictionary<FrameKey, ConcurrentDictionary<SemaphoreSlim, byte>> _waiters = new();

    public void PublishFrame(Guid projectId, int cameraRole, byte[] frame)
    {
        if (frame.Length == 0)
        {
            return;
        }

        FrameKey key = new(projectId, cameraRole);
        FrameSlot slot = _slots.GetOrAdd(key, static _ => new FrameSlot());
        Volatile.Write(ref slot.Frame, frame);
        Interlocked.Increment(ref slot.Version);

        if (_waiters.TryGetValue(key, out ConcurrentDictionary<SemaphoreSlim, byte>? waiters))
        {
            foreach (SemaphoreSlim waiter in waiters.Keys)
            {
                try
                {
                    if (waiter.CurrentCount == 0)
                    {
                        waiter.Release();
                    }
                }
                catch (ObjectDisposedException)
                {
                    waiters.TryRemove(waiter, out _);
                }
            }
        }
    }

    public byte[]? TryGetLatest(Guid projectId, int cameraRole, out long version)
    {
        FrameKey key = new(projectId, cameraRole);
        if (_slots.TryGetValue(key, out FrameSlot? slot))
        {
            version = Volatile.Read(ref slot.Version);
            return Volatile.Read(ref slot.Frame);
        }

        version = 0;
        return null;
    }

    public bool HasFrame(Guid projectId, int cameraRole)
    {
        return _slots.TryGetValue(new FrameKey(projectId, cameraRole), out FrameSlot? slot)
            && Volatile.Read(ref slot.Version) > 0;
    }

    public SemaphoreSlim RegisterWaiter(Guid projectId, int cameraRole)
    {
        FrameKey key = new(projectId, cameraRole);
        SemaphoreSlim waiter = new(0, 1);
        _waiters.GetOrAdd(key, static _ => new ConcurrentDictionary<SemaphoreSlim, byte>())
            .TryAdd(waiter, 0);
        return waiter;
    }

    public void UnregisterWaiter(Guid projectId, int cameraRole, SemaphoreSlim waiter)
    {
        FrameKey key = new(projectId, cameraRole);
        if (_waiters.TryGetValue(key, out ConcurrentDictionary<SemaphoreSlim, byte>? waiters))
        {
            waiters.TryRemove(waiter, out _);
            if (waiters.IsEmpty)
                _waiters.TryRemove(new KeyValuePair<FrameKey, ConcurrentDictionary<SemaphoreSlim, byte>>(key, waiters));
        }
        waiter.Dispose();
    }
}
