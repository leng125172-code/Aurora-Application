namespace AuroraStruct3D.RS485.Protocol;

/// <summary>
/// RS485串口通信接口，提供原始字节帧的收发能力。
/// 内部通过优先级命令队列调度，支持紧急命令插队、超时自动重发和串口断线自动重连。
/// </summary>
public interface IRS485Port : IDisposable
{
    /// <summary>串口名称（如 /dev/ttyS6）</summary>
    string PortName { get; }

    /// <summary>串口是否已打开</summary>
    bool IsOpen { get; }

    /// <summary>
    /// 打开串口
    /// </summary>
    void Open();

    /// <summary>
    /// 关闭串口
    /// </summary>
    void Close();

    /// <summary>
    /// 发送字节帧并等待响应（普通优先级，加入 FIFO 命令队列）。
    /// 支持按配置的最大重发次数自动重试。
    /// </summary>
    /// <param name="request">待发送的请求帧</param>
    /// <param name="expectedResponseLength">
    /// 预期响应字节数。0=不等待响应；负数=读到空闲或超时；正数=精确接收。
    /// </param>
    /// <param name="timeoutMs">命令整体超时毫秒数，默认 500ms</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>收到的响应帧；若无响应则返回空数组</returns>
    Task<byte[]> SendAndReceiveAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 发送紧急字节帧并等待响应（高优先级，跳过普通队列立即执行）。
    /// 不支持自动重发，失败立即上报。
    /// 仅用于急停等实时性要求极高的指令。
    /// </summary>
    /// <param name="request">待发送的请求帧</param>
    /// <param name="expectedResponseLength">预期响应字节数（同 SendAndReceiveAsync）</param>
    /// <param name="timeoutMs">超时毫秒数，默认 200ms（急停场景宜短）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>收到的响应帧；若无响应则返回空数组</returns>
    Task<byte[]> SendAndReceiveUrgentAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 200,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取总线互斥锁；返回的 disposable 释放时归还锁。
    /// 仅供需要在多次原子读写之间保持互斥的高级场景（如 Ymodem 升级）使用。
    /// 持锁期间工作线程将暂停命令调度。
    /// </summary>
    Task<IDisposable> AcquireBusLockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 原始字节写入（绕过帧封装，必须在持有总线锁的上下文中使用）
    /// </summary>
    Task WriteRawAsync(
        ReadOnlyMemory<byte> data,
        int timeoutMs,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 原始字节读取一字节（持有总线锁中使用）。
    /// 超时返回 -1。
    /// </summary>
    Task<int> ReadRawByteAsync(int timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空输入缓冲区（持有总线锁中使用）
    /// </summary>
    void DiscardInputBuffer();

    /// <summary>
    /// 获取当前总线运行监控指标快照（线程安全，可随时调用）。
    /// 包含队列深度、成功率、超时率、重发次数、重连次数等。
    /// </summary>
    RS485PortMetrics GetMetrics();
}
