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
}
