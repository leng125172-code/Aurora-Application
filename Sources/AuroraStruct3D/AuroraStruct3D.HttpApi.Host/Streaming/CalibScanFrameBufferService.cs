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
    private readonly ConcurrentDictionary<
        FrameKey,
        ConcurrentBag<WeakReference<SemaphoreSlim>>
    > _waiters = new();

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

        if (_waiters.TryGetValue(key, out var waiters))
        {
            foreach (WeakReference<SemaphoreSlim> reference in waiters)
            {
                if (reference.TryGetTarget(out SemaphoreSlim? waiter))
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
                        // HTTP 订阅者已断开。
                    }
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
        _waiters
            .GetOrAdd(
                key,
                static _ => new ConcurrentBag<WeakReference<SemaphoreSlim>>()
            )
            .Add(new WeakReference<SemaphoreSlim>(waiter));
        return waiter;
    }

    public static void UnregisterWaiter(SemaphoreSlim waiter)
    {
        waiter.Dispose();
    }
}
