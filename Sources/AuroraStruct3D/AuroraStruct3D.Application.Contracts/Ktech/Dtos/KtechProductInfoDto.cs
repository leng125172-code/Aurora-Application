namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 设备产品信息 DTO（对应 CMD 0x12 响应）。
/// </summary>
public class KtechProductInfoDto
{
    /// <summary>设备类型代码（如 MS=8209）</summary>
    public int DeviceTypeCode { get; set; }

    /// <summary>设备类型名称（如 "MS"、"MF"、"MG"、"MGE"、"MH"）</summary>
    public string DeviceTypeName { get; set; } = string.Empty;

    /// <summary>驱动器名称（最长 20 字节 ASCII）</summary>
    public string DriverName { get; set; } = string.Empty;

    /// <summary>电机名称（最长 20 字节 ASCII）</summary>
    public string MotorName { get; set; } = string.Empty;

    /// <summary>芯片 ID（十六进制字符串）</summary>
    public string ChipId { get; set; } = string.Empty;

    /// <summary>硬件版本（如 V1.2）</summary>
    public string HardwareVersion { get; set; } = string.Empty;

    /// <summary>电机固件版本</summary>
    public string MotorVersion { get; set; } = string.Empty;

    /// <summary>驱动器固件版本</summary>
    public string FirmwareVersion { get; set; } = string.Empty;
}
