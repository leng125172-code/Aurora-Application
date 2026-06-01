namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定设备拓扑类型。
/// 与需求文档"无光系列 / 单光系列"矩阵一一对应。
/// </summary>
public enum CalibrationDeviceType
{
    /// <summary>2 目 0 光：主相机（左）+ 从相机（右），水平对称</summary>
    TwoCamZeroLight = 0,

    /// <summary>3 目 0 光：主相机（中上）+ 左下从相机 + 右下从相机，等腰三角形</summary>
    ThreeCamZeroLight = 1,

    /// <summary>1 目 1 光：主相机 + 主结构光</summary>
    OneCamOneLight = 10,

    /// <summary>2 目 1 光：主相机（左）+ 从相机（右）+ 主结构光</summary>
    TwoCamOneLight = 11,

    /// <summary>3 目 1 光：主相机（中上）+ 左下从相机 + 右下从相机 + 主结构光</summary>
    ThreeCamOneLight = 12,
}

/// <summary>
/// 相机在标定拓扑中的逻辑角色。
/// </summary>
public enum CameraRole
{
    /// <summary>主相机（2 目设备的左相机、1 目设备的唯一相机）</summary>
    Master = 0,

    /// <summary>从相机：左（2 目设备）</summary>
    LeftSlave = 1,

    /// <summary>从相机：右（2 目设备）</summary>
    RightSlave = 2,

    /// <summary>3 目设备的中上主相机</summary>
    TopMaster = 10,

    /// <summary>3 目设备的左下从相机</summary>
    LeftBottomSlave = 11,

    /// <summary>3 目设备的右下从相机</summary>
    RightBottomSlave = 12,
}

/// <summary>
/// 电机在标定拓扑中的逻辑角色。
/// </summary>
public enum MotorRole
{
    /// <summary>左相机旋转电机</summary>
    LeftCameraRotate = 0,

    /// <summary>右相机旋转电机</summary>
    RightCameraRotate = 1,

    /// <summary>单相机旋转电机（1 目设备）</summary>
    SingleCameraRotate = 2,

    /// <summary>相机间基线距离平移电机（2 目设备）</summary>
    BaselineTranslate = 10,

    /// <summary>相机与结构光基线距离平移电机（1 目 1 光设备）</summary>
    CameraLightTranslate = 11,

    /// <summary>三组云台整体间距平移电机（3 目设备）</summary>
    GimbalGroupTranslate = 12,

    /// <summary>云台 X 轴旋转</summary>
    GimbalAxisX = 20,

    /// <summary>云台 Y 轴旋转</summary>
    GimbalAxisY = 21,

    /// <summary>云台 Z 轴旋转（可选）</summary>
    GimbalAxisZRotate = 22,

    /// <summary>云台 Z 轴平移（可选）</summary>
    GimbalAxisZTranslate = 23,
}

/// <summary>
/// 结构光投射器在标定拓扑中的逻辑角色。
/// </summary>
public enum ProjectorRole
{
    /// <summary>主结构光投射器（整机中心位，位置固定）</summary>
    MainStructuredLight = 0,
}

/// <summary>
/// 标定板类型。
/// </summary>
public enum CalibrationBoardType
{
    /// <summary>棋盘格标定板</summary>
    Chessboard = 0,

    /// <summary>圆形点阵标定板</summary>
    CircleGrid = 1,

    /// <summary>AprilTag 标定板</summary>
    AprilTag = 2,
}

/// <summary>
/// CMOS 传感器尺寸枚举。
/// 物理尺寸（宽 x 高，单位 mm）见 <see cref="CmosSensorSizes"/> 静态字典。
/// </summary>
public enum CmosSensorSize
{
    /// <summary>1/4 英寸：3.2 × 2.4 mm</summary>
    QuarterInch = 0,

    /// <summary>1/3 英寸：4.8 × 3.6 mm</summary>
    OneThirdInch = 1,

    /// <summary>1/2.3 英寸：6.17 × 4.55 mm</summary>
    OneOverTwoPointThreeInch = 2,

    /// <summary>1/2 英寸：6.4 × 4.8 mm</summary>
    OneSecondInch = 3,

    /// <summary>2/3 英寸：8.8 × 6.6 mm</summary>
    TwoThirdInch = 4,

    /// <summary>1 英寸：13.2 × 8.8 mm</summary>
    OneInch = 5,

    /// <summary>4/3 英寸：17.3 × 13.0 mm</summary>
    FourThirdInch = 6,

    /// <summary>APS-C：23.6 × 15.6 mm</summary>
    ApsC = 7,

    /// <summary>全画幅 (35mm)：36 × 24 mm</summary>
    FullFrame35mm = 8,

    /// <summary>中画幅：44 × 33 mm（典型值）</summary>
    MediumFormat = 9,

    /// <summary>自定义尺寸（手动填写 CmosWidthMm / CmosHeightMm）</summary>
    Custom = 99,
}

/// <summary>
/// CMOS 传感器物理尺寸查询表（宽 mm, 高 mm）。
/// 选择 <see cref="CmosSensorSize.Custom"/> 时不在此表中。
/// </summary>
public static class CmosSensorSizes
{
    private static readonly Dictionary<CmosSensorSize, (double WidthMm, double HeightMm)> Map =
        new()
        {
            { CmosSensorSize.QuarterInch, (3.2, 2.4) },
            { CmosSensorSize.OneThirdInch, (4.8, 3.6) },
            { CmosSensorSize.OneOverTwoPointThreeInch, (6.17, 4.55) },
            { CmosSensorSize.OneSecondInch, (6.4, 4.8) },
            { CmosSensorSize.TwoThirdInch, (8.8, 6.6) },
            { CmosSensorSize.OneInch, (13.2, 8.8) },
            { CmosSensorSize.FourThirdInch, (17.3, 13.0) },
            { CmosSensorSize.ApsC, (23.6, 15.6) },
            { CmosSensorSize.FullFrame35mm, (36.0, 24.0) },
            { CmosSensorSize.MediumFormat, (44.0, 33.0) },
        };

    /// <summary>
    /// 尝试根据 CMOS 枚举获取标准物理尺寸（mm）。
    /// </summary>
    /// <param name="size">CMOS 枚举值</param>
    /// <param name="dimensions">输出物理尺寸（宽 mm, 高 mm）</param>
    /// <returns>true=查表命中；false=自定义或未知</returns>
    public static bool TryGet(CmosSensorSize size, out (double WidthMm, double HeightMm) dimensions)
    {
        return Map.TryGetValue(size, out dimensions);
    }
}

/// <summary>
/// 图像采集像素格式。
/// </summary>
public enum ImageCaptureFormat
{
    /// <summary>原始 Bayer 数据</summary>
    Raw = 0,

    /// <summary>BGR 三通道彩色</summary>
    Bgr = 1,

    /// <summary>灰度（单通道）</summary>
    Gray = 2,
}

/// <summary>
/// 结构光投射图案类型。
/// </summary>
public enum StructuredLightPattern
{
    /// <summary>正弦条纹（相位测量轮廓术）</summary>
    SineFringe = 0,

    /// <summary>随机散斑</summary>
    RandomSpeckle = 1,

    /// <summary>编码光栅</summary>
    CodedGrating = 2,
}

/// <summary>
/// 标定工程生命周期状态。
/// </summary>
public enum CalibrationProjectStatus
{
    /// <summary>草稿（刚创建，未完成 Step 1）</summary>
    Draft = 0,

    /// <summary>配置中（Step 2~3：电机绑定、硬件参数填写）</summary>
    Configuring = 1,

    /// <summary>采集中（Step 4：图像采集进行中）</summary>
    Capturing = 2,

    /// <summary>计算中（Step 5：内外参/结构光标定计算中）</summary>
    Computing = 3,

    /// <summary>验证中（Step 6：精度验证）</summary>
    Validating = 4,

    /// <summary>已完成（标定结果可导出与生效）</summary>
    Completed = 5,

    /// <summary>失败（任意阶段抛出且未恢复）</summary>
    Failed = 6,

    /// <summary>已归档（不再展示在活跃列表）</summary>
    Archived = 7,
}

/// <summary>
/// 标定结果导出格式。
/// </summary>
public enum CalibrationResultExportFormat
{
    /// <summary>JSON 格式（默认，兼容主流 CV 库）</summary>
    Json = 0,

    /// <summary>XML 格式（OpenCV FileStorage 兼容）</summary>
    Xml = 1,

    /// <summary>YAML 格式（OpenCV FileStorage 兼容）</summary>
    Yaml = 2,

    /// <summary>纯文本（人类可读，便于排查）</summary>
    Txt = 3,
}

/// <summary>
/// 联动软限位中"被禁止的运动方向"。
/// </summary>
public enum BlockedMotionDirection
{
    /// <summary>禁止正方向运动</summary>
    Positive = 0,

    /// <summary>禁止负方向运动</summary>
    Negative = 1,

    /// <summary>禁止所有方向（双向锁定）</summary>
    Both = 2,
}

/// <summary>
/// 标定验证记录的类型。
/// </summary>
public enum CalibrationValidationType
{
    /// <summary>畸变校正验证</summary>
    DistortionCorrection = 0,

    /// <summary>立体匹配验证（多目设备）</summary>
    StereoMatching = 1,

    /// <summary>结构光深度验证（单光系列）</summary>
    StructuredLightDepth = 2,

    /// <summary>整体三维重建精度验证</summary>
    OverallAccuracy = 3,
}
