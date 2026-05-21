using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AuroraStruct3D.Cameras.Dtos;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Streaming;

/// <summary>
/// RTP/MJPEG UDP 推流服务器（RFC 2435）。
/// 每台相机独立使用一个 UDP 端口，帧数据以 RTP 包形式发送。
/// </summary>
public class RtpMjpegServer : IDisposable
{
    /// <summary>RTP MJPEG 负载类型（RFC 2435 定义为 26）</summary>
    private const byte RtpPayloadTypeMjpeg = 26;

    /// <summary>每个相机的基础 UDP 端口号，后续相机依次+1</summary>
    private const int BasePort = 5100;

    /// <summary>RTP 最大 UDP 载荷大小（字节），预留 IP/UDP/RTP 头部</summary>
    private const int MaxRtpPayload = 1400;

    private readonly ILogger<RtpMjpegServer> _logger;

    /// <summary>相机 ID → (UdpClient, 已分配端口, 序列号)</summary>
    private readonly ConcurrentDictionary<Guid, CameraRtpSession> _sessions = new();

    private bool _disposed;

    public RtpMjpegServer(ILogger<RtpMjpegServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 为指定相机分配或复用 RTP 会话
    /// </summary>
    public CameraRtpSession GetOrCreateSession(Guid cameraId)
    {
        return _sessions.GetOrAdd(
            cameraId,
            id =>
            {
                int port = BasePort + _sessions.Count;
                UdpClient udpClient = new UdpClient(port);
                _logger.LogInformation("相机 {Id} RTP 会话已创建，监听端口 {Port}", id, port);
                return new CameraRtpSession(udpClient, port);
            }
        );
    }

    /// <summary>
    /// 将一帧 JPEG 数据通过 RTP 发送给所有已订阅的客户端
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="jpegFrame">JPEG 编码的帧字节数组</param>
    public void SendFrame(Guid cameraId, byte[] jpegFrame)
    {
        if (!_sessions.TryGetValue(cameraId, out CameraRtpSession? session))
            return;

        if (session.Subscribers.IsEmpty)
            return;

        // 时间戳：90kHz 时钟
        uint timestamp = (uint)(Environment.TickCount64 * 90);
        int offset = 0;
        int totalLen = jpegFrame.Length;
        bool isFirst = true;

        while (offset < totalLen)
        {
            int chunkLen = Math.Min(MaxRtpPayload, totalLen - offset);
            bool isLast = (offset + chunkLen) >= totalLen;

            byte[] packet = BuildRtpMjpegPacket(
                session.IncrementSequence(),
                timestamp,
                isLast,
                jpegFrame,
                offset,
                chunkLen,
                isFirst
            );

            foreach (IPEndPoint ep in session.Subscribers.Keys)
            {
                try
                {
                    session.UdpClient.Send(packet, packet.Length, ep);
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(ex, "RTP 发送失败，移除订阅 {Ep}", ep);
                    session.Subscribers.TryRemove(ep, out _);
                }
            }

            offset += chunkLen;
            isFirst = false;
        }
    }

    /// <summary>
    /// 订阅指定相机的 RTP 流
    /// </summary>
    public void Subscribe(Guid cameraId, IPEndPoint clientEndPoint)
    {
        CameraRtpSession session = GetOrCreateSession(cameraId);
        session.Subscribers.TryAdd(clientEndPoint, 0);
        _logger.LogInformation("客户端 {Ep} 订阅相机 {Id} RTP 流", clientEndPoint, cameraId);
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe(Guid cameraId, IPEndPoint clientEndPoint)
    {
        if (_sessions.TryGetValue(cameraId, out CameraRtpSession? session))
        {
            session.Subscribers.TryRemove(clientEndPoint, out _);
        }
    }

    /// <summary>
    /// 获取指定相机的 RTP 端点信息 DTO
    /// </summary>
    public CameraRtpEndpointDto? GetEndpoint(Guid cameraId, string hostAddress)
    {
        if (!_sessions.TryGetValue(cameraId, out CameraRtpSession? session))
            return null;

        string endpoint = $"{hostAddress}:{session.Port}";

        // 生成简单 SDP（Base64）
        string sdp =
            $"v=0\r\n"
            + $"o=- 0 0 IN IP4 {hostAddress}\r\n"
            + $"s=CameraStream\r\n"
            + $"c=IN IP4 {hostAddress}\r\n"
            + $"t=0 0\r\n"
            + $"m=video {session.Port} RTP/AVP 26\r\n"
            + $"a=rtpmap:26 JPEG/90000\r\n";

        return new CameraRtpEndpointDto
        {
            Endpoint = endpoint,
            SdpBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sdp)),
        };
    }

    /// <summary>
    /// 停止并释放指定相机的 RTP 会话
    /// </summary>
    public void RemoveSession(Guid cameraId)
    {
        if (_sessions.TryRemove(cameraId, out CameraRtpSession? session))
        {
            session.UdpClient.Dispose();
            _logger.LogInformation("相机 {Id} RTP 会话已释放", cameraId);
        }
    }

    /// <summary>
    /// 构建 RTP/JPEG 数据包（RFC 2435）
    /// </summary>
    private static byte[] BuildRtpMjpegPacket(
        ushort seqNum,
        uint timestamp,
        bool marker,
        byte[] jpegData,
        int jpegOffset,
        int jpegLen,
        bool isFirstFragment
    )
    {
        // RTP 固定头部（12字节）
        // JPEG 特定头部（8字节）
        int headerLen = 12 + 8;
        byte[] packet = new byte[headerLen + jpegLen];

        // RTP 头部
        packet[0] = 0x80; // V=2, P=0, X=0, CC=0
        packet[1] = (byte)((marker ? 0x80 : 0x00) | RtpPayloadTypeMjpeg);
        packet[2] = (byte)(seqNum >> 8);
        packet[3] = (byte)(seqNum & 0xFF);
        packet[4] = (byte)(timestamp >> 24);
        packet[5] = (byte)(timestamp >> 16);
        packet[6] = (byte)(timestamp >> 8);
        packet[7] = (byte)(timestamp & 0xFF);
        // SSRC = 0（简化）

        // JPEG 特定头部（RFC 2435 Section 3.1）
        int rtpJpegOffset = isFirstFragment ? 0 : jpegOffset;
        packet[12] = 0; // Type-specific
        packet[13] = (byte)(rtpJpegOffset >> 16);
        packet[14] = (byte)(rtpJpegOffset >> 8);
        packet[15] = (byte)(rtpJpegOffset & 0xFF);
        packet[16] = 1; // Type: 1（YUV422 JPEG）
        packet[17] = 255; // Q: 255（动态量化表）
        packet[18] = 0; // Width placeholder
        packet[19] = 0; // Height placeholder

        // JPEG 数据载荷
        Buffer.BlockCopy(jpegData, jpegOffset, packet, headerLen, jpegLen);
        return packet;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (CameraRtpSession? session in _sessions.Values)
        {
            session.UdpClient.Dispose();
        }
        _sessions.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 相机 RTP 会话状态
/// </summary>
public class CameraRtpSession
{
    private int _sequenceNumber;

    public CameraRtpSession(UdpClient udpClient, int port)
    {
        UdpClient = udpClient;
        Port = port;
    }

    /// <summary>UDP 客户端</summary>
    public UdpClient UdpClient { get; }

    /// <summary>绑定的本地端口</summary>
    public int Port { get; }

    /// <summary>已订阅的客户端端点集合</summary>
    public ConcurrentDictionary<IPEndPoint, byte> Subscribers { get; } = new();

    /// <summary>递增并返回 RTP 序列号</summary>
    public ushort IncrementSequence()
    {
        return (ushort)(Interlocked.Increment(ref _sequenceNumber) & 0xFFFF);
    }
}
