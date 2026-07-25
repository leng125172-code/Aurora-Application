using System.Collections.Concurrent;

namespace AuroraStruct3D.Streaming;

/// <summary>
/// 相机帧缓冲服务（单例）。
/// 为每台相机维护最新一帧的图像缓冲区，供 HTTP MJPEG 流订阅者读取。
/// CameraPreviewService 抓帧后调用 PublishFrame 写入，
/// /api/streaming/cameras/{id}/preview 端点通过订阅读取实现推送。
/// </summary>
public class CameraFrameBufferService
{
    /// <summary>
    /// 单帧槽位：保存帧内容 + 版本号。
    /// 使用不可变引用替换（Volatile 读/写）保证多线程可见性。
    /// </summary>
    private sealed class FrameSlot
    {
        /// <summary>帧字节数组（BMP），引用不可变，发布时整体替换</summary>
        public byte[]? Frame;

        /// <summary>帧长度（字节），避免每次访问 Frame.Length</summary>
        public int FrameLength;

        /// <summary>版本号，自增；订阅者通过对比版本号判断是否有新帧</summary>
        public long Version;
    }

    /// <summary>相机 ID → 帧槽位</summary>
    private readonly ConcurrentDictionary<Guid, FrameSlot> _slots = new();

    /// <summary>相机 ID → 订阅者唤醒事件集合（有新帧时 Set 通知）</summary>
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentBag<WeakReference<SemaphoreSlim>>
    > _waiters = new();

    /// <summary>相机 ID → 当前活跃的 HTTP MJPEG 订阅者数量</summary>
    private readonly ConcurrentDictionary<Guid, int> _subscriberCounts = new();

    /// <summary>
    /// 发布一帧到指定相机的缓冲槽。
    /// 同时唤醒所有正在等待该相机新帧的订阅者。
    /// </summary>
    /// <param name="cameraId">相机 ID</param>
    /// <param name="frame">帧字节数组（BMP），内部直接持有引用，不做拷贝以降低延迟</param>
    public void PublishFrame(Guid cameraId, byte[] frame)
    {
        if (frame == null)
            return;

        FrameSlot slot = _slots.GetOrAdd(cameraId, _ => new FrameSlot());

        // 整体替换引用 + 更新长度 + 自增版本（操作顺序：先写数据，再写版本——读取端反向）
        slot.Frame = frame;
        Volatile.Write(ref slot.FrameLength, frame.Length);
        Interlocked.Increment(ref slot.Version);

        // 唤醒所有该相机的订阅者（已断开的订阅者 Semaphore 会在 GC 后被自动清理）
        if (_waiters.TryGetValue(cameraId, out ConcurrentBag<WeakReference<SemaphoreSlim>>? bag))
        {
            foreach (WeakReference<SemaphoreSlim> wr in bag)
            {
                if (wr.TryGetTarget(out SemaphoreSlim? sem))
                {
                    try
                    {
                        // 只释放 1 个等待者即可：若有多个订阅者，下一个 PublishFrame 会继续唤醒
                        if (sem.CurrentCount == 0)
                            sem.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // 订阅者已停止，忽略
                    }
                }
            }
        }
    }

    /// <summary>
    /// 立即读取指定相机的最新帧（非阻塞）。
    /// 返回 null 表示该相机尚无任何帧发布。
    /// </summary>
    /// <param name="cameraId">相机 ID</param>
    /// <param name="version">输出：读取到的帧版本号（用于下次等待）</param>
    public byte[]? TryGetLatest(Guid cameraId, out long version)
    {
        if (_slots.TryGetValue(cameraId, out FrameSlot? slot))
        {
            // 先读版本再读数据，和 Publish 写入顺序对称，避免读到版本新但数据旧的撕裂
            version = Volatile.Read(ref slot.Version);
            byte[]? f = slot.Frame;
            int len = Volatile.Read(ref slot.FrameLength);
            if (f != null && len > 0 && f.Length >= len)
                return f;
        }
        version = 0;
        return null;
    }

    /// <summary>
    /// 注册一个订阅者并返回其 Semaphore。
    /// 订阅者在帧循环中通过 await sem.WaitAsync 阻塞，
    /// PublishFrame 会释放该 sem 从而唤醒订阅者。
    /// 订阅者退出循环后务必调用 UnregisterWaiter 以释放资源。
    /// </summary>
    public SemaphoreSlim RegisterWaiter(Guid cameraId)
    {
        SemaphoreSlim sem = new SemaphoreSlim(0, 1);
        ConcurrentBag<WeakReference<SemaphoreSlim>> bag = _waiters.GetOrAdd(
            cameraId,
            _ => new ConcurrentBag<WeakReference<SemaphoreSlim>>()
        );
        bag.Add(new WeakReference<SemaphoreSlim>(sem));
        _subscriberCounts.AddOrUpdate(cameraId, 1, (_, old) => old + 1);
        return sem;
    }

    /// <summary>
    /// 取消订阅者注册：释放 Semaphore 并从袋中移除无效引用。
    /// 为性能考虑，仅 Dispose 传入的 Semaphore；袋中已失效的 WeakReference 由 GC 自动回收。
    /// </summary>
    public void UnregisterWaiter(Guid cameraId, SemaphoreSlim sem)
    {
        sem.Dispose();
        _subscriberCounts.AddOrUpdate(cameraId, 0, (_, old) => Math.Max(0, old - 1));
    }

    /// <summary>
    /// 获取指定相机当前的 MJPEG HTTP 订阅者数量（用于决定是否需要真实抓帧）。
    /// </summary>
    public int GetSubscriberCount(Guid cameraId)
    {
        return _subscriberCounts.TryGetValue(cameraId, out int count) ? count : 0;
    }

    /// <summary>
    /// 查询指定相机当前是否至少已有 1 帧缓冲。
    /// </summary>
    public bool HasFrame(Guid cameraId)
    {
        return _slots.TryGetValue(cameraId, out FrameSlot? s) && Volatile.Read(ref s.Version) > 0;
    }

    /// <summary>
    /// 停止相机时调用：清理帧缓冲和订阅计数。
    /// </summary>
    public void ClearCamera(Guid cameraId)
    {
        if (_slots.TryRemove(cameraId, out FrameSlot? slot))
        {
            slot.Frame = null;
            Volatile.Write(ref slot.FrameLength, 0);
        }
        _subscriberCounts.TryRemove(cameraId, out _);
    }
}
