using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定结果聚合根（PLY 生成核心依赖）。
/// 一个工程可产生多个版本结果；通过 <see cref="IsActive"/> 标记当前生效版本。
/// 所有矩阵/数组使用 JSON 列（text）存储，便于反序列化后传递给 PLY 生成器与其它 CV 库。
/// </summary>
public class CalibrationResult : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属标定工程 ID</summary>
    public Guid CalibrationProjectId { get; private set; }

    /// <summary>所属标定设备 ID（冗余存储，PLY 生成时按 deviceId + version 检索）</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>结果版本号（同工程内递增）</summary>
    public int Version { get; private set; }

    /// <summary>是否为当前生效版本</summary>
    public bool IsActive { get; private set; }

    /// <summary>计算时间（UTC）</summary>
    public DateTime ComputedTime { get; private set; }

    // ── 标定数据（JSON） ─────────────────────────────────────────────────

    /// <summary>
    /// 所有相机的内参 JSON。结构：
    /// [{ "cameraDeviceId": "...", "role": 0, "fx": ..., "fy": ..., "cx": ..., "cy": ...,
    ///    "k1": ..., "k2": ..., "k3": ..., "p1": ..., "p2": ..., "reprojectionError": ... }]
    /// </summary>
    public string CameraIntrinsicsJson { get; private set; } = "[]";

    /// <summary>
    /// 相机之间的外参 JSON。结构：
    /// [{ "fromCameraId": "...", "toCameraId": "...",
    ///    "rotationMatrix": [[..],[..],[..]], "translation": [tx, ty, tz],
    ///    "reprojectionError": ... }]
    /// </summary>
    public string CameraExtrinsicsJson { get; private set; } = "[]";

    /// <summary>
    /// 结构光标定 JSON（仅单光系列；无则为 null）。结构：
    /// { "equivalentIntrinsics": {...}, "extrinsicsToMaster": {...}, "phaseToDepthMapping": {...} }
    /// </summary>
    public string? StructuredLightCalibrationJson { get; private set; }

    // ── 误差统计 ─────────────────────────────────────────────────────────

    /// <summary>整体重投影误差</summary>
    public double OverallReprojectionError { get; private set; }

    /// <summary>最大误差</summary>
    public double MaxError { get; private set; }

    /// <summary>最小误差</summary>
    public double MinError { get; private set; }

    /// <summary>平均误差</summary>
    public double MeanError { get; private set; }

    /// <summary>均方根误差</summary>
    public double RmsError { get; private set; }

    /// <summary>验证记录集合</summary>
    public ICollection<CalibrationValidationRecord> Validations { get; private set; } =
        new List<CalibrationValidationRecord>();

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationResult() { }

    /// <summary>创建标定结果</summary>
    public CalibrationResult(
        Guid id,
        Guid calibrationProjectId,
        Guid calibrationDeviceId,
        int version
    )
        : base(id)
    {
        CalibrationProjectId = calibrationProjectId;
        CalibrationDeviceId = calibrationDeviceId;
        Version = version;
        ComputedTime = DateTime.UtcNow;
    }

    /// <summary>写入相机内参 JSON</summary>
    public void SetCameraIntrinsics(string json) =>
        CameraIntrinsicsJson = Check.NotNullOrWhiteSpace(json, nameof(json));

    /// <summary>写入相机外参 JSON</summary>
    public void SetCameraExtrinsics(string json) =>
        CameraExtrinsicsJson = Check.NotNullOrWhiteSpace(json, nameof(json));

    /// <summary>写入结构光标定 JSON（可空）</summary>
    public void SetStructuredLightCalibration(string? json) =>
        StructuredLightCalibrationJson = json;

    /// <summary>写入整体误差统计</summary>
    public void SetErrorStatistics(double overall, double max, double min, double mean, double rms)
    {
        OverallReprojectionError = overall;
        MaxError = max;
        MinError = min;
        MeanError = mean;
        RmsError = rms;
    }

    /// <summary>标记为生效（通常由 AppService 在切换前先把同工程其它版本设为非生效）</summary>
    public void SetActive(bool isActive) => IsActive = isActive;
}
