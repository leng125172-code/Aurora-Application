namespace AuroraStruct3D.RS485.Protocol;

/// <summary>
/// RS485 命令优先级
/// </summary>
public enum RS485CommandPriority
{
    /// <summary>普通优先级，加入 FIFO 普通队列执行</summary>
    Normal = 0,

    /// <summary>紧急优先级，跳过普通队列优先执行（急停等实时性要求极高的指令）</summary>
    Urgent = 1,
}

/// <summary>
/// RS485 总线监控指标快照（线程安全，随时可读）
/// </summary>
public sealed class RS485PortMetrics
{
    /// <summary>普通队列当前待处理命令数</summary>
    public int PendingNormalCount { get; init; }

    /// <summary>紧急队列当前待处理命令数</summary>
    public int PendingUrgentCount { get; init; }

    /// <summary>累计成功发送命令数</summary>
    public long TotalSent { get; init; }

    /// <summary>累计超时次数（含最终失败和重发后成功的超时）</summary>
    public long TotalTimeouts { get; init; }

    /// <summary>累计自动重发次数</summary>
    public long TotalRetries { get; init; }

    /// <summary>累计串口自动重连次数</summary>
    public long TotalReconnects { get; init; }

    /// <summary>串口当前是否已连接</summary>
    public bool IsConnected { get; init; }
}

/// <summary>
/// RS485 内部待处理命令包（仅 RS485Port 工作线程使用）
/// </summary>
internal sealed class PendingRS485Command
{
    private static long _idCounter;

    /// <summary>全局递增唯一命令 ID（用于链路追踪日志）</summary>
    public long CommandId { get; } = Interlocked.Increment(ref _idCounter);

    /// <summary>请求字节帧（上层协议打包完成后传入）</summary>
    public required byte[] RequestBytes { get; init; }

    /// <summary>
    /// 预期响应字节数。
    /// 0 = 不等待响应（仅发送）；
    /// 负数 = 读取到总线空闲或整体超时；
    /// 正数 = 精确读取指定字节数。
    /// </summary>
    public int ExpectedResponseLength { get; init; }

    /// <summary>命令整体超时（毫秒），含发送+接收</summary>
    public int TimeoutMs { get; init; } = 500;

    /// <summary>最大重发次数（0=不重发，仅超时时生效）</summary>
    public int MaxRetries { get; init; }

    /// <summary>当前已重发次数（工作线程内部维护）</summary>
    public int RetryCount { get; set; }

    /// <summary>是否为紧急命令（决定进入哪条队列）</summary>
    public bool IsUrgent { get; init; }

    /// <summary>
    /// 结果完成源。上层 await <see cref="Tcs"/>.Task 阻塞等待工作线程设置结果。
    /// 使用 RunContinuationsAsynchronously 避免工作线程被上层回调阻塞。
    /// </summary>
    public TaskCompletionSource<byte[]> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>调用方取消令牌（若已取消，工作线程立即放弃本命令）</summary>
    public CancellationToken CallerToken { get; init; }

    /// <summary>入队时间（用于日志和延迟统计）</summary>
    public DateTime EnqueuedAt { get; } = DateTime.UtcNow;
}
