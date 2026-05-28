namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 雷赛 iCL-RS DI/DO 功能配置 DTO（功能码从 0x0145-0x0151 / 0x0157-0x015B 读取）。
/// 每个功能码的低 8 位为功能定义，bit7 = 1 表示常闭（NC）。
/// </summary>
public class LeisaiIoConfigDto
{
    /// <summary>电机轴 ID</summary>
    public Guid AxisId { get; set; }

    /// <summary>7 路数字输入功能码（DI1..DI7，按下标顺序对应 0x0145, 0x0147, ..., 0x0151）</summary>
    public ushort[] DiFunctionCodes { get; set; } = new ushort[7];

    /// <summary>3 路数字输出功能码（DO1..DO3，按下标顺序对应 0x0157, 0x0159, 0x015B）</summary>
    public ushort[] DoFunctionCodes { get; set; } = new ushort[3];
}
