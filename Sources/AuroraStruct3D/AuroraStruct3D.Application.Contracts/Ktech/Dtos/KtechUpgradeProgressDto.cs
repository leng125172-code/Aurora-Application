namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// Ymodem 固件升级阶段。
/// </summary>
public enum KtechUpgradeStage
{
    /// <summary>开始</summary>
    Started = 0,

    /// <summary>等待握手 C 字符</summary>
    WaitingHandshake = 1,

    /// <summary>已发送首包，等待接收</summary>
    HeaderSent = 2,

    /// <summary>数据包传输中</summary>
    Transferring = 3,

    /// <summary>结束包（EOT/空包）</summary>
    Finalizing = 4,

    /// <summary>完成</summary>
    Completed = 5,

    /// <summary>失败</summary>
    Failed = 99,
}

/// <summary>
/// 升级进度推送 DTO。
/// </summary>
public class KtechUpgradeProgressDto
{
    /// <summary>电机轴 ID</summary>
    public Guid AxisId { get; set; }

    /// <summary>当前阶段</summary>
    public KtechUpgradeStage Stage { get; set; }

    /// <summary>已发送字节数</summary>
    public long BytesSent { get; set; }

    /// <summary>总字节数</summary>
    public long TotalBytes { get; set; }

    /// <summary>进度百分比（0-100）</summary>
    public int Percent { get; set; }

    /// <summary>提示信息或失败原因</summary>
    public string? Message { get; set; }

    /// <summary>失败步骤编码（仅 Stage=Failed 时有效，对应 Demo errorType 1-10）</summary>
    public int? ErrorStep { get; set; }
}
