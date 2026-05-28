namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 标定参数 DTO（CMD 0x16/0x17），统一承载 MS/MF/MH 与 MG/MG_E 两种结构。
/// </summary>
/// <remarks>
/// 通用字段（MotorPoles..SavedFlag）在所有型号均有效；
/// 当 <see cref="DeviceTypeCode"/> 为 8241(MG)/8242(MG_E) 时，<see cref="Mg"/> 子块会被填充。
/// </remarks>
public class KtechCalibDto
{
    /// <summary>设备类型码（8209=MS, 8225=MF, 8241=MG, 8242=MG_E, 8257=MH，0=未知）</summary>
    public int DeviceTypeCode { get; set; }

    /// <summary>电机极对数</summary>
    public byte MotorPoles { get; set; }

    /// <summary>编码器类型索引</summary>
    public byte EncoderType { get; set; }

    /// <summary>编码器位置：0=Normal, 1=Reverse</summary>
    public byte EncoderPos { get; set; }

    /// <summary>电机相序：0=Normal, 1=Reverse</summary>
    public byte MotorPhaseSequence { get; set; }

    /// <summary>电机编码器对齐偏置（u32）</summary>
    public uint MotorEncoderAlignBias { get; set; }

    /// <summary>电机编码器对齐比率（u16）</summary>
    public ushort MotorEncoderAlignRatio { get; set; }

    /// <summary>电机编码器对齐电压（V）</summary>
    public double MotorEncoderAlignVoltage { get; set; }

    /// <summary>电机编码器对齐标志</summary>
    public byte MotorEncoderAlignFlag { get; set; }

    /// <summary>编码器零点偏置（u32）</summary>
    public uint EncoderOffset { get; set; }

    /// <summary>编码器偏置标志</summary>
    public byte EncoderOffsetFlag { get; set; }

    /// <summary>保存标志（一般为 0x55555555）</summary>
    public uint SavedFlag { get; set; }

    /// <summary>MG/MG_E 型号扩展字段（其他型号为 null）</summary>
    public KtechCalibMgExtraDto? Mg { get; set; }
}
