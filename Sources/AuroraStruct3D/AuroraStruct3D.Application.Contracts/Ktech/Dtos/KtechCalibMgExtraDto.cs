namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH MG/MG_E 型号标定数据的扩展字段（仅在 DeviceTypeCode=8241/8242 时填充）。
/// </summary>
public class KtechCalibMgExtraDto
{
    /// <summary>对齐校准值数组（32 个 u16）</summary>
    public ushort[] AlignValueList { get; set; } = new ushort[32];

    /// <summary>减速比（仅 MG_E 有效）</summary>
    public byte ReductionRatio { get; set; }

    /// <summary>编码器关联值（u32）</summary>
    public uint EncoderRelateValue { get; set; }

    /// <summary>编码器关联值标志</summary>
    public byte EncoderRelateValueFlag { get; set; }

    /// <summary>第二编码器偏置（u32，MG_E 专用）</summary>
    public uint Encoder2Offset { get; set; }

    /// <summary>第二编码器偏置标志</summary>
    public byte Encoder2OffsetFlag { get; set; }
}
