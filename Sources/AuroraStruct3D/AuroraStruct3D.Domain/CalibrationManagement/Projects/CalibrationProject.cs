using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定工程聚合根（Step 4~6 的执行主体）。
/// 一个工程绑定到一个 <see cref="CalibrationDevice"/>，记录标定板/采集配置、
/// 采集到的帧（含图像）以及生命周期状态。最终产出的标定结果由 <see cref="CalibrationResult"/> 承载。
/// </summary>
public class CalibrationProject : FullAuditedAggregateRoot<Guid>
{
    // ── 基本信息 ─────────────────────────────────────────────────────────

    /// <summary>工程显示名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>工程描述</summary>
    public string? Description { get; private set; }

    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>当前生命周期状态</summary>
    public CalibrationProjectStatus Status { get; private set; }

    /// <summary>开始执行时间（UTC）</summary>
    public DateTime? StartedTime { get; private set; }

    /// <summary>完成时间（UTC）</summary>
    public DateTime? CompletedTime { get; private set; }

    /// <summary>失败原因</summary>
    public string? FailureReason { get; private set; }

    // ── 标定板配置（Step 4） ─────────────────────────────────────────────

    /// <summary>标定板类型</summary>
    public CalibrationBoardType BoardType { get; private set; }

    /// <summary>标定板行数（棋盘=内角点行、圆=圆点行、AprilTag=标签行）</summary>
    public int BoardRows { get; private set; }

    /// <summary>标定板列数</summary>
    public int BoardCols { get; private set; }

    /// <summary>方格边长（mm，棋盘格使用）</summary>
    public double? SquareSizeMm { get; private set; }

    /// <summary>圆点直径（mm，圆形标定板使用）</summary>
    public double? CircleDiameterMm { get; private set; }

    /// <summary>圆点中心间距（mm，圆形标定板使用）</summary>
    public double? CircleSpacingMm { get; private set; }

    /// <summary>AprilTag 标签家族（如 tag36h11）</summary>
    public string? AprilTagFamily { get; private set; }

    /// <summary>AprilTag 标签尺寸（mm）</summary>
    public double? AprilTagSizeMm { get; private set; }

    /// <summary>AprilTag 标签间距（mm）</summary>
    public double? AprilTagSpacingMm { get; private set; }

    /// <summary>标定板制作精度（mm，用于计算理论精度上限）</summary>
    public double BoardManufactureAccuracyMm { get; private set; }

    // ── 采集配置（Step 4） ────────────────────────────────────────────────

    /// <summary>目标采集帧数（建议 20~50）</summary>
    public int TargetCaptureCount { get; private set; }

    /// <summary>统一曝光时间（μs）</summary>
    public double UnifiedExposureUs { get; private set; }

    /// <summary>统一增益（dB）</summary>
    public double UnifiedGainDb { get; private set; }

    /// <summary>统一白平衡（色温 K 或预设字符串）</summary>
    public string? UnifiedWhiteBalance { get; private set; }

    /// <summary>图像采集格式</summary>
    public ImageCaptureFormat ImageFormat { get; private set; }

    /// <summary>结构光投射亮度（0~100，仅单光系列）</summary>
    public int? StructuredLightBrightness { get; private set; }

    /// <summary>图案投射间隔时间（ms，仅单光系列）</summary>
    public int? PatternIntervalMs { get; private set; }

    /// <summary>每个相位的采集次数（仅单光系列）</summary>
    public int? CapturesPerPhase { get; private set; }

    // ── 子集合 ─────────────────────────────────────────────────────────────

    /// <summary>已采集的帧</summary>
    public ICollection<CalibrationCaptureFrame> Frames { get; private set; } =
        new List<CalibrationCaptureFrame>();

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationProject() { }

    /// <summary>创建标定工程</summary>
    public CalibrationProject(
        Guid id,
        string name,
        Guid calibrationDeviceId,
        string? description = null
    )
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        CalibrationDeviceId = calibrationDeviceId;
        Description = description;
        Status = CalibrationProjectStatus.Draft;
        BoardType = CalibrationBoardType.Chessboard;
        ImageFormat = ImageCaptureFormat.Gray;
        TargetCaptureCount = 30;
    }

    /// <summary>更新名称与描述</summary>
    public void UpdateBasicInfo(string name, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        Description = description;
    }

    /// <summary>切换状态（带可选时间戳与失败原因记录）</summary>
    public void TransitionStatus(CalibrationProjectStatus status, string? failureReason = null)
    {
        Status = status;
        if (status == CalibrationProjectStatus.Capturing && StartedTime is null)
        {
            StartedTime = DateTime.UtcNow;
        }
        if (status == CalibrationProjectStatus.Completed)
        {
            CompletedTime = DateTime.UtcNow;
            FailureReason = null;
        }
        if (status == CalibrationProjectStatus.Failed)
        {
            FailureReason = failureReason;
        }
    }

    /// <summary>设置标定板配置</summary>
    public void SetBoardConfig(
        CalibrationBoardType boardType,
        int rows,
        int cols,
        double manufactureAccuracyMm,
        double? squareSizeMm = null,
        double? circleDiameterMm = null,
        double? circleSpacingMm = null,
        string? aprilTagFamily = null,
        double? aprilTagSizeMm = null,
        double? aprilTagSpacingMm = null
    )
    {
        BoardType = boardType;
        BoardRows = rows;
        BoardCols = cols;
        BoardManufactureAccuracyMm = manufactureAccuracyMm;
        SquareSizeMm = squareSizeMm;
        CircleDiameterMm = circleDiameterMm;
        CircleSpacingMm = circleSpacingMm;
        AprilTagFamily = aprilTagFamily;
        AprilTagSizeMm = aprilTagSizeMm;
        AprilTagSpacingMm = aprilTagSpacingMm;
    }

    /// <summary>设置统一采集参数</summary>
    public void SetCaptureConfig(
        int targetCaptureCount,
        double unifiedExposureUs,
        double unifiedGainDb,
        ImageCaptureFormat imageFormat,
        string? unifiedWhiteBalance = null,
        int? structuredLightBrightness = null,
        int? patternIntervalMs = null,
        int? capturesPerPhase = null
    )
    {
        TargetCaptureCount = targetCaptureCount;
        UnifiedExposureUs = unifiedExposureUs;
        UnifiedGainDb = unifiedGainDb;
        ImageFormat = imageFormat;
        UnifiedWhiteBalance = unifiedWhiteBalance;
        StructuredLightBrightness = structuredLightBrightness;
        PatternIntervalMs = patternIntervalMs;
        CapturesPerPhase = capturesPerPhase;
    }
}
