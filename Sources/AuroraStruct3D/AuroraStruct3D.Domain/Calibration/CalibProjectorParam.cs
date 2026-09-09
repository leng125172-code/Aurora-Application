using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 结构光投射器参数配置实体（Step 3 投影仪参数配置页）。
/// 存储用于标定的投射器光学和图案参数，
/// 可按投射器型号分类保存为模板以便复用。
///
/// 数据库表：AbpProCalibProjectorParams
/// </summary>
public class CalibProjectorParam : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID（关联AbpProCalibProjects）</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>对应的投射器设备ID（关联AbpProProjectors）</summary>
    public Guid ProjectorDeviceId { get; private set; }

    /// <summary>参数配置名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>参数配置描述</summary>
    public string? Description { get; private set; }

    /// <summary>投射器分辨率宽度（像素）</summary>
    public int ResolutionWidth { get; private set; }

    /// <summary>投射器分辨率高度（像素）</summary>
    public int ResolutionHeight { get; private set; }

    /// <summary>投射比（投射距离 / 投射宽度）</summary>
    public decimal ProjectionRatio { get; private set; }

    /// <summary>最小工作距离（mm）</summary>
    public int WorkingDistanceMin { get; private set; }

    /// <summary>最大工作距离（mm）</summary>
    public int WorkingDistanceMax { get; private set; }

    /// <summary>投射图案类型（正弦条纹 / 随机散斑 / 编码光栅）</summary>
    public ProjectorPatternType PatternType { get; private set; }

    /// <summary>图案数量</summary>
    public int PatternCount { get; private set; }

    /// <summary>条纹周期数（一个条纹周期包含的像素数，必须能整除分辨率宽度/高度）</summary>
    public int PeriodCount { get; private set; }

    /// <summary>条纹类型：bw=黑白（首色黑），wb=白黑（首色白）</summary>
    public string FringeType { get; private set; } = "bw";

    public byte DarkLevel { get; private set; }

    public byte BrightLevel { get; private set; }

    /// <summary>相位偏移量（正弦条纹时有效）</summary>
    public decimal? PhaseShift { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>是否已保存为模板</summary>
    public bool IsTemplateMode { get; private set; }

    /// <summary>模板分类（按投射器型号分类，仅模板模式有效）</summary>
    public string? TemplateCategory { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibProjectorParam() { }

    /// <summary>
    /// 创建投射器标定参数
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">所属标定项目ID</param>
    /// <param name="projectorDeviceId">投射器设备ID</param>
    /// <param name="name">参数名称</param>
    public CalibProjectorParam(Guid id, Guid calibProjectId, Guid projectorDeviceId, string name)
        : base(id)
    {
        CalibProjectId = calibProjectId;
        ProjectorDeviceId = projectorDeviceId;
        SetName(name);
        PatternType = ProjectorPatternType.SineFringe;
        PatternCount = 8;
        PeriodCount = 8;
        FringeType = "bw";
        DarkLevel = 24;
        BrightLevel = 220;
        IsEnabled = true;
        IsTemplateMode = false;
    }

    /// <summary>设置参数名称</summary>
    public CalibProjectorParam SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CalibConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CalibProjectorParam SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CalibConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>更新分辨率和投射比参数</summary>
    public CalibProjectorParam SetProjectionParams(
        int resolutionWidth,
        int resolutionHeight,
        decimal projectionRatio,
        int workingDistanceMin,
        int workingDistanceMax
    )
    {
        ResolutionWidth = resolutionWidth;
        ResolutionHeight = resolutionHeight;
        ProjectionRatio = projectionRatio;
        WorkingDistanceMin = workingDistanceMin;
        WorkingDistanceMax = workingDistanceMax;
        return this;
    }

    /// <summary>更新图案参数</summary>
    public CalibProjectorParam SetPatternParams(
        ProjectorPatternType patternType,
        int patternCount,
        decimal? phaseShift = null
    )
    {
        PatternType = patternType;
        PatternCount = patternCount;
        PhaseShift = phaseShift;
        return this;
    }

    /// <summary>
    /// 更新条纹参数（Step3 投影仪参数配置页使用）
    /// </summary>
    /// <param name="periodCount">条纹周期数</param>
    /// <param name="fringeType">条纹类型：bw=黑白，wb=白黑</param>
    /// <param name="patternCount">图案数量（相移步数）</param>
    /// <param name="phaseShift">相位偏移量</param>
    public CalibProjectorParam SetFringeParams(
        int periodCount,
        string fringeType,
        int patternCount,
        decimal? phaseShift = null,
        byte darkLevel = 24,
        byte brightLevel = 220
    )
    {
        if (darkLevel >= brightLevel)
            throw new BusinessException("Calib:InvalidFringeGrayLevels")
                .WithData("DarkLevel", darkLevel).WithData("BrightLevel", brightLevel);
        PeriodCount = periodCount;
        FringeType = string.IsNullOrEmpty(fringeType) ? "bw" : fringeType;
        PatternCount = patternCount;
        PhaseShift = phaseShift;
        DarkLevel = darkLevel;
        BrightLevel = brightLevel;
        return this;
    }

    /// <summary>更新分辨率（Step3 投影仪参数配置页使用，仅记录用户输入的高度；宽度从投影仪读取）</summary>
    public CalibProjectorParam SetResolution(int width, int height)
    {
        ResolutionWidth = width;
        ResolutionHeight = height;
        return this;
    }

    /// <summary>设置模板模式及分类</summary>
    public CalibProjectorParam SetTemplateMode(bool isTemplate, string? category = null)
    {
        IsTemplateMode = isTemplate;
        if (category != null)
        {
            Check.Length(category, nameof(category), CalibConsts.MaxTemplateCategoryLength);
        }
        TemplateCategory = category;
        return this;
    }

    /// <summary>启用或禁用</summary>
    public CalibProjectorParam SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        return this;
    }
}
