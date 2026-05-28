using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 手动控制 DO 输出请求。
/// </summary>
public class LeisaiWriteOutputInputDto
{
    /// <summary>DO 序号（1..3）</summary>
    [Range(1, 3)]
    public int DoIndex { get; set; }

    /// <summary>输出电平（true=高电平 / 闭合）</summary>
    public bool Value { get; set; }
}

/// <summary>
/// 清除故障类型。
/// </summary>
public class LeisaiClearFaultInputDto
{
    /// <summary>
    /// 清除模式：
    /// <list type="bullet">
    ///   <item>"current"：仅清当前故障（写 0x1111 至 0x1801）</item>
    ///   <item>"all"：清除历史故障记录（写 0x1122 至 0x1801）</item>
    /// </list>
    /// </summary>
    [Required]
    public string Mode { get; set; } = "current";
}

/// <summary>
/// 手动 Modbus 读请求（FC03）。
/// </summary>
public class LeisaiRawModbusReadInputDto
{
    /// <summary>功能码（当前仅支持 03）</summary>
    [Range(1, 255)]
    public byte FunctionCode { get; set; } = 3;

    /// <summary>起始地址</summary>
    public ushort StartAddress { get; set; }

    /// <summary>寄存器数量（1..125）</summary>
    [Range(1, 125)]
    public ushort Quantity { get; set; } = 1;
}

/// <summary>
/// 手动 Modbus 写请求（FC06 / FC16）。
/// </summary>
public class LeisaiRawModbusWriteInputDto
{
    /// <summary>功能码（6 或 16）</summary>
    [Range(1, 255)]
    public byte FunctionCode { get; set; } = 6;

    /// <summary>起始地址</summary>
    public ushort StartAddress { get; set; }

    /// <summary>写入的寄存器值列表（FC06 仅取第 1 个）</summary>
    [Required]
    [MinLength(1)]
    public ushort[] Values { get; set; } = [];
}
