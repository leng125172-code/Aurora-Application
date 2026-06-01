using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 结构光投射器硬件参数（标定语境下的快照，仅单光系列设备使用）。
/// </summary>
public class CalibrationProjectorParameter : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>对应物理投射器设备 ID</summary>
    public Guid ProjectorDeviceId { get; private set; }

    /// <summary>投射器分辨率宽度（像素）</summary>
    public int ResolutionWidthPx { get; private set; }

    /// <summary>投射器分辨率高度（像素）</summary>
    public int ResolutionHeightPx { get; private set; }

    /// <summary>投射比（投射距离 / 投射宽度）</summary>
    public double ThrowRatio { get; private set; }

    /// <summary>工作距离下限（mm）</summary>
    public double MinWorkingDistanceMm { get; private set; }

    /// <summary>工作距离上限（mm）</summary>
    public double MaxWorkingDistanceMm { get; private set; }

    /// <summary>投射图案类型</summary>
    public StructuredLightPattern Pattern { get; private set; }

    /// <summary>图案数量</summary>
    public int PatternCount { get; private set; }

    /// <summary>相位偏移量（弧度）</summary>
    public double PhaseShift { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationProjectorParameter() { }

    /// <summary>创建结构光参数</summary>
    public CalibrationProjectorParameter(Guid id, Guid calibrationDeviceId, Guid projectorDeviceId)
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        ProjectorDeviceId = projectorDeviceId;
    }

    /// <summary>设置分辨率</summary>
    public void SetResolution(int widthPx, int heightPx)
    {
        ResolutionWidthPx = widthPx;
        ResolutionHeightPx = heightPx;
    }

    /// <summary>设置投射光学参数</summary>
    public void SetOptics(
        double throwRatio,
        double minWorkingDistanceMm,
        double maxWorkingDistanceMm
    )
    {
        ThrowRatio = throwRatio;
        MinWorkingDistanceMm = minWorkingDistanceMm;
        MaxWorkingDistanceMm = maxWorkingDistanceMm;
    }

    /// <summary>设置图案参数</summary>
    public void SetPattern(StructuredLightPattern pattern, int patternCount, double phaseShift)
    {
        Pattern = pattern;
        PatternCount = patternCount;
        PhaseShift = phaseShift;
    }
}
