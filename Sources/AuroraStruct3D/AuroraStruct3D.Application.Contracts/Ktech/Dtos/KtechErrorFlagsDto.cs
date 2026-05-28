namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 错误位 DTO（State1 字节 6 → 8 位）。
/// </summary>
public class KtechErrorFlagsDto
{
    /// <summary>欠压</summary>
    public bool UnderVoltage { get; set; }

    /// <summary>过压</summary>
    public bool OverVoltage { get; set; }

    /// <summary>驱动温度过高</summary>
    public bool DriverOverTemp { get; set; }

    /// <summary>电机温度过高</summary>
    public bool MotorOverTemp { get; set; }

    /// <summary>过流</summary>
    public bool OverCurrent { get; set; }

    /// <summary>短路</summary>
    public bool ShortCircuit { get; set; }

    /// <summary>失速</summary>
    public bool Stall { get; set; }

    /// <summary>失控</summary>
    public bool LostInput { get; set; }

    /// <summary>原始字节</summary>
    public byte Raw { get; set; }
}
