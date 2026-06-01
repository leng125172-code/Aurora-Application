using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 云台组预设位置。
/// 支持保存和加载，每条预设可命名并附备注。
/// </summary>
public class CalibrationGimbalPreset : Entity<Guid>
{
    /// <summary>所属云台组 ID</summary>
    public Guid GimbalGroupId { get; private set; }

    /// <summary>预设名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>备注</summary>
    public string? Remarks { get; private set; }

    /// <summary>X 轴位置</summary>
    public double XPosition { get; private set; }

    /// <summary>Y 轴位置</summary>
    public double YPosition { get; private set; }

    /// <summary>Z 轴旋转位置（可选）</summary>
    public double? ZRotatePosition { get; private set; }

    /// <summary>Z 轴平移位置（可选）</summary>
    public double? ZTranslatePosition { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationGimbalPreset() { }

    /// <summary>创建云台预设位置</summary>
    public CalibrationGimbalPreset(
        Guid id,
        Guid gimbalGroupId,
        string name,
        double xPosition,
        double yPosition,
        double? zRotatePosition = null,
        double? zTranslatePosition = null,
        string? remarks = null
    )
        : base(id)
    {
        GimbalGroupId = gimbalGroupId;
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        XPosition = xPosition;
        YPosition = yPosition;
        ZRotatePosition = zRotatePosition;
        ZTranslatePosition = zTranslatePosition;
        Remarks = remarks;
    }
}
