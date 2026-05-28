namespace AuroraStruct3D.Motors.Dtos;

/// <summary>
/// 电机扫描进度阶段。
/// </summary>
public enum MotorScanProgressKind
{
    /// <summary>扫描开始</summary>
    Started = 0,

    /// <summary>某个串口开始扫描</summary>
    PortStarted = 1,

    /// <summary>正在以指定波特率与从机地址探测</summary>
    Probing = 2,

    /// <summary>探测发现设备</summary>
    DeviceFound = 3,

    /// <summary>某个串口扫描结束</summary>
    PortFinished = 4,

    /// <summary>串口打开失败或异常</summary>
    PortError = 5,

    /// <summary>整体扫描完成</summary>
    Completed = 6,
}

/// <summary>
/// 电机扫描实时进度 DTO，通过 SignalR 推送至前端。
/// </summary>
public class MotorScanProgressDto
{
    /// <summary>进度阶段</summary>
    public MotorScanProgressKind Kind { get; set; }

    /// <summary>事件时间戳（ISO 字符串，便于前端跨时区展示）</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>简短描述，可直接呈现给用户</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>串口配置 ID</summary>
    public Guid? SerialPortConfigId { get; set; }

    /// <summary>系统串口名</summary>
    public string? PortName { get; set; }

    /// <summary>当前尝试的波特率</summary>
    public int? BaudRate { get; set; }

    /// <summary>当前尝试的从机地址</summary>
    public int? SlaveId { get; set; }

    /// <summary>当前尝试的电机品牌协议</summary>
    public string? Brand { get; set; }

    /// <summary>本次进度对应的串口是否发现设备</summary>
    public bool? Found { get; set; }

    /// <summary>总串口数</summary>
    public int TotalPorts { get; set; }

    /// <summary>已完成串口数</summary>
    public int FinishedPorts { get; set; }

    /// <summary>累计探测次数</summary>
    public int TriedCount { get; set; }

    /// <summary>累计发现设备数</summary>
    public int FoundCount { get; set; }

    /// <summary>已耗时毫秒</summary>
    public long? ElapsedMs { get; set; }

    /// <summary>本次探测发送帧（十六进制，空格分隔，如 AA 55 01）</summary>
    public string? TxHex { get; set; }

    /// <summary>本次探测接收帧（十六进制，空格分隔，如 3E 20 01）</summary>
    public string? RxHex { get; set; }

    /// <summary>本次探测失败原因（仅失败时有值）</summary>
    public string? ErrorMessage { get; set; }
}
