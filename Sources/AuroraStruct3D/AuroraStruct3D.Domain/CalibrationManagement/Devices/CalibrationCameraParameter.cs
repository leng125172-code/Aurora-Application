using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 相机硬件参数（标定语境下的快照）。
/// 与 PLY 生成算法所需相机参数字段一一对应。
/// 通过 <see cref="CameraDeviceId"/> 关联到 Cameras.CameraDevice，但参数值是标定时点的独立快照。
/// </summary>
public class CalibrationCameraParameter : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>对应物理相机设备 ID</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>该相机的角色（冗余，便于查询）</summary>
    public CameraRole Role { get; private set; }

    // ── CMOS 传感器 ─────────────────────────────────────────────────────────

    /// <summary>CMOS 尺寸枚举</summary>
    public CmosSensorSize CmosSize { get; private set; }

    /// <summary>CMOS 物理宽度（mm）</summary>
    public double CmosWidthMm { get; private set; }

    /// <summary>CMOS 物理高度（mm）</summary>
    public double CmosHeightMm { get; private set; }

    // ── 分辨率 ──────────────────────────────────────────────────────────────

    /// <summary>图像宽度（像素）</summary>
    public int ResolutionWidthPx { get; private set; }

    /// <summary>图像高度（像素）</summary>
    public int ResolutionHeightPx { get; private set; }

    // ── 镜头 ────────────────────────────────────────────────────────────────

    /// <summary>镜头标称焦距（mm）</summary>
    public double NominalFocalLengthMm { get; private set; }

    /// <summary>最大光圈 F 值（光圈最大开启对应最小 F 数）</summary>
    public double MaxAperture { get; private set; }

    /// <summary>最小光圈 F 值</summary>
    public double MinAperture { get; private set; }

    /// <summary>当前光圈 F 值</summary>
    public double CurrentAperture { get; private set; }

    // ── 曝光 / 增益 ─────────────────────────────────────────────────────────

    /// <summary>最小曝光时间（μs）</summary>
    public double MinExposureUs { get; private set; }

    /// <summary>最大曝光时间（μs）</summary>
    public double MaxExposureUs { get; private set; }

    /// <summary>最小增益（dB）</summary>
    public double MinGainDb { get; private set; }

    /// <summary>最大增益（dB）</summary>
    public double MaxGainDb { get; private set; }

    // ── 派生 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 像素物理尺寸（μm/像素），由 CmosWidthMm / ResolutionWidthPx × 1000 自动计算。
    /// 计算结果在 <see cref="Recompute"/> 中写入，确保查询时无需重复计算。
    /// </summary>
    public double PixelSizeUm { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationCameraParameter() { }

    /// <summary>创建相机硬件参数记录（含派生字段自动计算）</summary>
    public CalibrationCameraParameter(
        Guid id,
        Guid calibrationDeviceId,
        Guid cameraDeviceId,
        CameraRole role
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        CameraDeviceId = cameraDeviceId;
        Role = role;
    }

    /// <summary>
    /// 设置 CMOS 信息（同时刷新派生的像素尺寸）。
    /// </summary>
    public void SetCmos(CmosSensorSize size, double widthMm, double heightMm)
    {
        CmosSize = size;
        CmosWidthMm = widthMm;
        CmosHeightMm = heightMm;
        Recompute();
    }

    /// <summary>
    /// 设置分辨率（同时刷新派生的像素尺寸）。
    /// </summary>
    public void SetResolution(int widthPx, int heightPx)
    {
        ResolutionWidthPx = widthPx;
        ResolutionHeightPx = heightPx;
        Recompute();
    }

    /// <summary>设置镜头参数</summary>
    public void SetLens(
        double focalLengthMm,
        double maxAperture,
        double minAperture,
        double currentAperture
    )
    {
        NominalFocalLengthMm = focalLengthMm;
        MaxAperture = maxAperture;
        MinAperture = minAperture;
        CurrentAperture = currentAperture;
    }

    /// <summary>设置曝光与增益范围</summary>
    public void SetExposureGain(
        double minExposureUs,
        double maxExposureUs,
        double minGainDb,
        double maxGainDb
    )
    {
        MinExposureUs = minExposureUs;
        MaxExposureUs = maxExposureUs;
        MinGainDb = minGainDb;
        MaxGainDb = maxGainDb;
    }

    /// <summary>重新计算像素物理尺寸（μm/像素）</summary>
    private void Recompute()
    {
        if (ResolutionWidthPx > 0 && CmosWidthMm > 0)
        {
            PixelSizeUm = CmosWidthMm / ResolutionWidthPx * 1000.0;
        }
        else
        {
            PixelSizeUm = 0;
        }
    }
}
