using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定结果验证记录（Step 6）。
/// 每种验证类型可有多次记录，便于追踪历史。
/// </summary>
public class CalibrationValidationRecord : Entity<Guid>
{
    /// <summary>所属标定结果 ID</summary>
    public Guid CalibrationResultId { get; private set; }

    /// <summary>验证类型</summary>
    public CalibrationValidationType ValidationType { get; private set; }

    /// <summary>是否通过</summary>
    public bool IsPassed { get; private set; }

    /// <summary>误差统计 JSON（结构由验证类型决定）</summary>
    public string? MetricsJson { get; private set; }

    /// <summary>报告 BLOB 键名（PDF / 截图，可空）</summary>
    public string? ReportBlobName { get; private set; }

    /// <summary>备注</summary>
    public string? Remarks { get; private set; }

    /// <summary>验证时间（UTC）</summary>
    public DateTime ValidatedTime { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationValidationRecord() { }

    /// <summary>创建验证记录</summary>
    public CalibrationValidationRecord(
        Guid id,
        Guid calibrationResultId,
        CalibrationValidationType validationType,
        bool isPassed,
        string? metricsJson = null,
        string? reportBlobName = null,
        string? remarks = null
    )
        : base(id)
    {
        CalibrationResultId = calibrationResultId;
        ValidationType = validationType;
        IsPassed = isPassed;
        MetricsJson = metricsJson;
        ReportBlobName = reportBlobName;
        Remarks = remarks;
        ValidatedTime = DateTime.UtcNow;
    }
}
