using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 设备标定项目聚合根。
/// 代表一次完整的多目相机（或结构光）标定工程，
/// 记录设备类型选择和当前标定步骤进度。
///
/// 数据库表：AbpProCalibProjects
/// </summary>
public class CalibProject : FullAuditedAggregateRoot<Guid>
{
    /// <summary>标定项目名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>标定项目描述</summary>
    public string? Description { get; private set; }

    /// <summary>设备系列（无光系列 / 单光系列）</summary>
    public DeviceSeries DeviceSeries { get; private set; }

    /// <summary>设备类型（2目0光 / 3目0光 / 1目1光 / 2目1光 / 3目1光）</summary>
    public CalibDeviceType DeviceType { get; private set; }

    /// <summary>相机数量（由设备类型决定，1~3）</summary>
    public int CameraCount { get; private set; }

    /// <summary>结构光数量（由设备类型决定，0~1）</summary>
    public int ProjectorCount { get; private set; }

    /// <summary>当前标定流程状态（对应 Step 1~7）</summary>
    public CalibStatus CalibStatus { get; private set; }

    // ── 标定板参数（Step 5 使用） ─────────────────────────────────────────────────

    /// <summary>实体棋盘格内角点行数</summary>
    public int PhysicalCornerRows { get; private set; }

    /// <summary>实体棋盘格内角点列数</summary>
    public int PhysicalCornerCols { get; private set; }

    /// <summary>实体棋盘格单个方格物理边长（mm）</summary>
    public decimal PhysicalSquareSizeMm { get; private set; }

    /// <summary>投影棋盘格内角点行数</summary>
    public int ProjectedCornerRows { get; private set; }

    /// <summary>投影棋盘格内角点列数</summary>
    public int ProjectedCornerCols { get; private set; }

    /// <summary>投影棋盘格单个方格像素尺寸（px）</summary>
    public int ProjectedPixelSize { get; private set; }

    /// <summary>标定板类型（默认棋盘格）。</summary>
    public CalibrationBoardType BoardType { get; private set; } = CalibrationBoardType.Chessboard;

    /// <summary>圆点板列数（可空，圆点类型时必填）。</summary>
    public int? CirclePatternCols { get; private set; }

    /// <summary>圆点板行数（可空，圆点类型时必填）。</summary>
    public int? CirclePatternRows { get; private set; }

    /// <summary>圆点中心间距（mm，可空，圆点类型时必填）。</summary>
    public decimal? CircleSpacingMm { get; private set; }

    /// <summary>圆点直径（mm，可空）。</summary>
    public decimal? CircleDiameterMm { get; private set; }

    /// <summary>是否存在中心标记（缺孔）。</summary>
    public bool? HasCenterMarker { get; private set; }

    /// <summary>是否启用四角定位点（配置元数据）。</summary>
    public bool? HasCornerLocators { get; private set; }

    /// <summary>中心标记行坐标（0-based，可空）。</summary>
    public int? MarkerRow { get; private set; }

    /// <summary>中心标记列坐标（0-based，可空）。</summary>
    public int? MarkerCol { get; private set; }

    /// <summary>圆点检测器参数 JSON（可空）。</summary>
    public string? CircleDetectorConfigJson { get; private set; }

    /// <summary>标定板厚度（mm，用于投影仪外参标定补偿，默认 1mm）。</summary>
    public decimal BoardThicknessMm { get; private set; } = 1m;

    /// <summary>绑定的结构光投影仪设备ID（单光系列在 Step 3 选定后持久化）</summary>
    public Guid? BoundProjectorDeviceId { get; private set; }

    /// <summary>绑定的主相机设备ID</summary>
    public Guid? MainCameraDeviceId { get; private set; }

    /// <summary>绑定的从相机设备ID</summary>
    public Guid? SecondaryCameraDeviceId { get; private set; }

    /// <summary>绑定的主相机角度控制电机轴ID</summary>
    public Guid? MainCameraMotorAxisId { get; private set; }

    /// <summary>绑定的从相机角度控制电机轴ID</summary>
    public Guid? SecondaryCameraMotorAxisId { get; private set; }

    /// <summary>绑定的间距控制电机轴ID</summary>
    public Guid? DistanceMotorAxisId { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibProject() { }

    /// <summary>
    /// 创建标定项目
    /// </summary>
    /// <param name="id">聚合根ID</param>
    /// <param name="name">项目名称</param>
    /// <param name="deviceType">设备类型</param>
    public CalibProject(Guid id, string name, CalibDeviceType deviceType)
        : base(id)
    {
        SetName(name);
        SetDeviceType(deviceType);
        SetBoardConfig(
            physicalCornerRows: 9,
            physicalCornerCols: 6,
            physicalSquareSizeMm: 30m,
            projectedCornerRows: 9,
            projectedCornerCols: 6,
            projectedPixelSize: 20
        );
        CalibStatus = CalibStatus.Initializing;
    }

    /// <summary>设置项目名称</summary>
    public CalibProject SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CalibConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CalibProject SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CalibConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>设置设备类型，并自动推算相机数量、结构光数量和设备系列</summary>
    public CalibProject SetDeviceType(CalibDeviceType deviceType)
    {
        DeviceType = deviceType;
        (DeviceSeries, CameraCount, ProjectorCount) = deviceType switch
        {
            CalibDeviceType.TwoCamera0Light => (DeviceSeries.NoLight, 2, 0),
            CalibDeviceType.OneCamera1Light => (DeviceSeries.SingleLight, 1, 1),
            CalibDeviceType.TwoCamera1Light => (DeviceSeries.SingleLight, 2, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(deviceType)),
        };
        return this;
    }

    /// <summary>
    /// 判断是否需要投影仪标定
    /// - OneCamera1Light（单目结构光）：需要投影仪标定
    /// - TwoCamera1Light（双目结构光）：不需要，投影仪仅作为纹理生成器
    /// </summary>
    public bool IsProjectorCalibrationRequired()
    {
        return DeviceType.IsProjectorCalibrationRequired();
    }

    /// <summary>推进标定步骤状态</summary>
    public CalibProject AdvanceStatus(CalibStatus status)
    {
        CalibStatus = status;
        return this;
    }

    /// <summary>设置绑定的结构光投影仪设备ID（传 null 清除绑定）</summary>
    public CalibProject SetBoundProjector(Guid? projectorDeviceId)
    {
        BoundProjectorDeviceId = projectorDeviceId;
        return this;
    }

    /// <summary>
    /// 设置设备绑定（相机/电机/结构光），传 null 可清除对应绑定。
    /// </summary>
    public CalibProject SetDeviceBindings(
        Guid? mainCameraDeviceId,
        Guid? secondaryCameraDeviceId,
        Guid? mainCameraMotorAxisId,
        Guid? secondaryCameraMotorAxisId,
        Guid? distanceMotorAxisId,
        Guid? boundProjectorDeviceId
    )
    {
        MainCameraDeviceId = mainCameraDeviceId;
        SecondaryCameraDeviceId = secondaryCameraDeviceId;
        MainCameraMotorAxisId = mainCameraMotorAxisId;
        SecondaryCameraMotorAxisId = secondaryCameraMotorAxisId;
        DistanceMotorAxisId = distanceMotorAxisId;
        BoundProjectorDeviceId = boundProjectorDeviceId;
        return this;
    }

    /// <summary>
    /// 设置标定板（棋盘格）参数
    /// </summary>
    /// <param name="physicalCornerRows">实体棋盘格内角点行数</param>
    /// <param name="physicalCornerCols">实体棋盘格内角点列数</param>
    /// <param name="physicalSquareSizeMm">实体方格物理边长（mm）</param>
    /// <param name="projectedCornerRows">投影棋盘格内角点行数</param>
    /// <param name="projectedCornerCols">投影棋盘格内角点列数</param>
    /// <param name="projectedPixelSize">投影棋盘格方格像素尺寸（px）</param>
    public CalibProject SetBoardConfig(
        int physicalCornerRows,
        int physicalCornerCols,
        decimal physicalSquareSizeMm,
        int projectedCornerRows,
        int projectedCornerCols,
        int projectedPixelSize
    )
    {
        BoardType = CalibrationBoardType.Chessboard;
        CirclePatternCols = null;
        CirclePatternRows = null;
        CircleSpacingMm = null;
        CircleDiameterMm = null;
        HasCenterMarker = null;
        HasCornerLocators = null;
        MarkerRow = null;
        MarkerCol = null;
        CircleDetectorConfigJson = null;

        if (physicalCornerRows < 2)
            throw new ArgumentOutOfRangeException(nameof(physicalCornerRows), "内角点行数至少为 2");
        if (physicalCornerCols < 2)
            throw new ArgumentOutOfRangeException(nameof(physicalCornerCols), "内角点列数至少为 2");
        if (physicalSquareSizeMm <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(physicalSquareSizeMm),
                "方格边长必须大于 0"
            );
        if (projectedCornerRows < 2)
            throw new ArgumentOutOfRangeException(
                nameof(projectedCornerRows),
                "投影内角点行数至少为 2"
            );
        if (projectedCornerCols < 2)
            throw new ArgumentOutOfRangeException(
                nameof(projectedCornerCols),
                "投影内角点列数至少为 2"
            );
        if (projectedPixelSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(projectedPixelSize),
                "投影像素尺寸至少为 1"
            );

        PhysicalCornerRows = physicalCornerRows;
        PhysicalCornerCols = physicalCornerCols;
        PhysicalSquareSizeMm = physicalSquareSizeMm;
        ProjectedCornerRows = projectedCornerRows;
        ProjectedCornerCols = projectedCornerCols;
        ProjectedPixelSize = projectedPixelSize;
        return this;
    }

    /// <summary>
    /// 设置标定板参数（支持棋盘格与圆点板）。
    /// </summary>
    public CalibProject SetBoardConfig(
        CalibrationBoardType boardType,
        int physicalCornerRows,
        int physicalCornerCols,
        decimal physicalSquareSizeMm,
        int projectedCornerRows,
        int projectedCornerCols,
        int projectedPixelSize,
        int? circlePatternCols,
        int? circlePatternRows,
        decimal? circleSpacingMm,
        decimal? circleDiameterMm,
        bool? hasCenterMarker,
        bool? hasCornerLocators,
        int? markerRow,
        int? markerCol,
        string? circleDetectorConfigJson,
        decimal? boardThicknessMm = null
    )
    {
        // 保留原有棋盘格字段，确保历史数据和旧接口兼容。
        SetBoardConfig(
            physicalCornerRows,
            physicalCornerCols,
            physicalSquareSizeMm,
            projectedCornerRows,
            projectedCornerCols,
            projectedPixelSize
        );

        BoardType = boardType;
        if (boardType == CalibrationBoardType.Chessboard)
        {
            if (boardThicknessMm.HasValue)
            {
                BoardThicknessMm = boardThicknessMm.Value;
            }
            return this;
        }

        if (!circlePatternCols.HasValue || circlePatternCols.Value < 2)
            throw new ArgumentOutOfRangeException(nameof(circlePatternCols), "圆点板列数至少为 2");
        if (!circlePatternRows.HasValue || circlePatternRows.Value < 2)
            throw new ArgumentOutOfRangeException(nameof(circlePatternRows), "圆点板行数至少为 2");
        if (!circleSpacingMm.HasValue || circleSpacingMm.Value <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(circleSpacingMm),
                "圆点中心间距必须大于 0"
            );
        if (circleDiameterMm.HasValue && circleDiameterMm.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(circleDiameterMm), "圆点直径必须大于 0");

        bool markerRequired = boardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        bool markerEnabled = hasCenterMarker ?? markerRequired;
        if (markerRequired && !markerEnabled)
            throw new ArgumentOutOfRangeException(
                nameof(hasCenterMarker),
                "中心标记圆点板必须启用标记点"
            );

        int rows = circlePatternRows.Value;
        int cols = circlePatternCols.Value;
        int resolvedMarkerRow = markerRow ?? (rows - 1) / 2;
        int resolvedMarkerCol = markerCol ?? (cols - 1) / 2;

        if (markerEnabled)
        {
            if (resolvedMarkerRow < 0 || resolvedMarkerRow >= rows)
                throw new ArgumentOutOfRangeException(nameof(markerRow), "标记点行坐标超出范围");
            if (resolvedMarkerCol < 0 || resolvedMarkerCol >= cols)
                throw new ArgumentOutOfRangeException(nameof(markerCol), "标记点列坐标超出范围");
        }

        CirclePatternCols = cols;
        CirclePatternRows = rows;
        CircleSpacingMm = circleSpacingMm;
        CircleDiameterMm = circleDiameterMm;
        HasCenterMarker = markerEnabled;
        HasCornerLocators = hasCornerLocators ?? false;
        MarkerRow = markerEnabled ? resolvedMarkerRow : null;
        MarkerCol = markerEnabled ? resolvedMarkerCol : null;
        CircleDetectorConfigJson = circleDetectorConfigJson;
        if (boardThicknessMm.HasValue)
        {
            BoardThicknessMm = boardThicknessMm.Value;
        }
        return this;
    }
}
