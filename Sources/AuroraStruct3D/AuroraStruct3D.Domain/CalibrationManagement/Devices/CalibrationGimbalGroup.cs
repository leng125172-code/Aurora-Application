using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 云台组（仅 3 目设备使用）。
/// 每个云台组绑定 X/Y（必填）以及可选的 Z 旋转 / Z 平移轴，并维护整体运动参数和若干预设位置。
/// </summary>
public class CalibrationGimbalGroup : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>云台组名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>X 轴旋转电机 ID（必填）</summary>
    public Guid XAxisMotorId { get; private set; }

    /// <summary>Y 轴旋转电机 ID（必填）</summary>
    public Guid YAxisMotorId { get; private set; }

    /// <summary>Z 轴旋转电机 ID（可选）</summary>
    public Guid? ZRotateAxisMotorId { get; private set; }

    /// <summary>Z 轴平移电机 ID（可选）</summary>
    public Guid? ZTranslateAxisMotorId { get; private set; }

    /// <summary>云台组整体最大速度</summary>
    public double MaxVelocity { get; private set; }

    /// <summary>加速度</summary>
    public double Acceleration { get; private set; }

    /// <summary>加减速时间（ms）</summary>
    public double AccelDecelTime { get; private set; }

    /// <summary>预设位置集合</summary>
    public ICollection<CalibrationGimbalPreset> PresetPositions { get; private set; } =
        new List<CalibrationGimbalPreset>();

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationGimbalGroup() { }

    /// <summary>创建云台组</summary>
    public CalibrationGimbalGroup(
        Guid id,
        Guid calibrationDeviceId,
        string name,
        Guid xAxisMotorId,
        Guid yAxisMotorId,
        Guid? zRotateAxisMotorId = null,
        Guid? zTranslateAxisMotorId = null
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        XAxisMotorId = xAxisMotorId;
        YAxisMotorId = yAxisMotorId;
        ZRotateAxisMotorId = zRotateAxisMotorId;
        ZTranslateAxisMotorId = zTranslateAxisMotorId;
    }

    /// <summary>更新运动参数</summary>
    public void SetMotionParameters(double maxVelocity, double acceleration, double accelDecelTime)
    {
        MaxVelocity = maxVelocity;
        Acceleration = acceleration;
        AccelDecelTime = accelDecelTime;
    }
}
