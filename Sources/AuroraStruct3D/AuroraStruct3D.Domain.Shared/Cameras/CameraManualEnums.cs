namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机自动曝光模式枚举
/// </summary>
public enum CameraAutoExposureMode
{
    /// <summary>关闭自动曝光，使用手动曝光</summary>
    Off = 0,

    /// <summary>单次自动曝光后锁定</summary>
    Once = 1,

    /// <summary>持续自动曝光</summary>
    Continuous = 2,
}

/// <summary>
/// 相机增益模式枚举（对应 TUIDC_SHUTTER）
/// </summary>
public enum CameraGainMode
{
    /// <summary>HDR 模式</summary>
    Hdr = 0,

    /// <summary>高增益模式</summary>
    High = 1,

    /// <summary>低增益模式</summary>
    Low = 2,
}

/// <summary>
/// 相机 Binning 模式枚举
/// </summary>
public enum CameraBinningMode
{
    /// <summary>关闭 Binning（1×1）</summary>
    Off = 0,

    /// <summary>2×2 Binning</summary>
    X2 = 1,

    /// <summary>4×4 Binning</summary>
    X4 = 2,
}

/// <summary>
/// 相机像素位深度枚举（对应 TUIDC_BITOFDEPTH）
/// </summary>
public enum CameraPixelDepth
{
    /// <summary>8 位</summary>
    Bit8 = 0,

    /// <summary>12 位</summary>
    Bit12 = 1,
}

/// <summary>
/// 相机白平衡模式枚举（对应 TUIDC_ATWBALANCE）
/// </summary>
public enum CameraWhiteBalanceMode
{
    /// <summary>关闭自动白平衡，使用手动通道增益</summary>
    Manual = 0,

    /// <summary>单次自动白平衡后锁定</summary>
    Once = 1,

    /// <summary>持续自动白平衡</summary>
    Continuous = 2,
}
