namespace AuroraStruct3D.RS485.Protocol;

/// <summary>
/// RS485串口通信接口，提供原始字节帧的收发能力
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
    /// 发送字节帧并等待响应（互斥访问，适用于半双工RS485总线）
    /// </summary>
    /// <param name="request">待发送的请求帧</param>
    /// <param name="expectedResponseLength">预期响应字节数，0表示不等待响应，小于0表示读取到空闲或超时</param>
    /// <param name="timeoutMs">等待响应超时毫秒数，默认500ms</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>收到的响应帧；若无响应则返回空数组</returns>
    Task<byte[]> SendAndReceiveAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取总线互斥锁；返回的 disposable 释放时归还锁。
    /// 仅供需要在多次原子读写之间保持互斥的高级场景（如 Ymodem 升级）使用。
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
}
