using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.SerialPorts;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.SerialPorts.Dtos;

/// <summary>
/// 串口配置输出 DTO。
/// </summary>
public class SerialPortConfigDto : EntityDto<Guid>
{
    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>系统串口名</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>波特率，0 表示未知或尚未选择</summary>
    public int BaudRate { get; set; }

    /// <summary>数据位</summary>
    public int DataBits { get; set; }

    /// <summary>校验位</summary>
    public SerialPortParity Parity { get; set; }

    /// <summary>停止位</summary>
    public SerialPortStopBits StopBits { get; set; }

    /// <summary>流控</summary>
    public SerialPortHandshake Handshake { get; set; }

    /// <summary>运行态是否已打开</summary>
    public bool IsOpen { get; set; }

    /// <summary>支持的波特率列表</summary>
    public IReadOnlyList<int> SupportedBaudRates { get; set; } = Array.Empty<int>();
}

/// <summary>
/// 串口列表查询 DTO。
/// </summary>
public class GetSerialPortListDto : PagedAndSortedResultRequestDto
{
    /// <summary>名称或串口号过滤</summary>
    public string? Filter { get; set; }

    /// <summary>启用状态过滤</summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 更新串口配置 DTO。系统串口号不允许在编辑中修改。
/// </summary>
public class UpdateSerialPortConfigDto
{
    /// <summary>显示名称</summary>
    [Required]
    [MaxLength(SerialPortConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>描述</summary>
    [MaxLength(SerialPortConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>波特率，0 表示未知</summary>
    public int BaudRate { get; set; }

    /// <summary>数据位</summary>
    [Range(5, 8)]
    public int DataBits { get; set; } = 8;

    /// <summary>校验位</summary>
    public SerialPortParity Parity { get; set; } = SerialPortParity.None;

    /// <summary>停止位</summary>
    public SerialPortStopBits StopBits { get; set; } = SerialPortStopBits.One;

    /// <summary>流控</summary>
    public SerialPortHandshake Handshake { get; set; } = SerialPortHandshake.None;
}

/// <summary>
/// 打开串口 DTO。
/// </summary>
public class ConnectSerialPortDto
{
    /// <summary>本次连接使用的波特率，为空则使用数据库配置</summary>
    public int? BaudRate { get; set; }
}

/// <summary>
/// 串口原始发送 DTO。
/// </summary>
public class SerialPortRawSendDto
{
    /// <summary>发送内容，Hex 模式可包含空格</summary>
    [Required]
    public string Payload { get; set; } = string.Empty;

    /// <summary>true 表示 Payload 为十六进制，false 表示按 UTF-8 文本发送</summary>
    public bool IsHex { get; set; } = true;

    /// <summary>文本模式下是否追加换行符</summary>
    public bool AppendNewLine { get; set; }

    /// <summary>预期响应长度，0 表示不等待，小于 0 表示读取到空闲或超时</summary>
    public int ExpectedResponseLength { get; set; } = -1;

    /// <summary>读取超时毫秒</summary>
    [Range(20, 10000)]
    public int TimeoutMs { get; set; } = 500;
}

/// <summary>
/// 串口原始发送结果 DTO。
/// </summary>
public class SerialPortRawResponseDto
{
    /// <summary>发送字节十六进制</summary>
    public string SentHex { get; set; } = string.Empty;

    /// <summary>响应字节十六进制</summary>
    public string ResponseHex { get; set; } = string.Empty;

    /// <summary>响应文本（UTF-8 尝试解码）</summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>响应字节数</summary>
    public int ResponseLength { get; set; }
}

/// <summary>
/// 系统串口扫描结果 DTO。
/// </summary>
public class SerialPortScanResultDto
{
    /// <summary>检测到的系统串口数量</summary>
    public int Count { get; set; }

    /// <summary>同步后的串口列表</summary>
    public List<SerialPortConfigDto> Items { get; set; } = new();
}