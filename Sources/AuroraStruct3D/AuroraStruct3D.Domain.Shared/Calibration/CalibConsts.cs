namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定模块常量定义
/// </summary>
public static class CalibConsts
{
    /// <summary>项目名称最大长度</summary>
    public const int MaxNameLength = 256;

    /// <summary>描述最大长度</summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>目标角色名称最大长度（如"主相机"、"左相机旋转电机"）</summary>
    public const int MaxTargetRoleLength = 128;

    /// <summary>传感器尺寸描述最大长度（如"1/2.3英寸"）</summary>
    public const int MaxSensorSizeLength = 50;

    /// <summary>模板分类最大长度（如相机型号或投射器型号）</summary>
    public const int MaxTemplateCategoryLength = 256;

    /// <summary>联动规则描述最大长度</summary>
    public const int MaxRuleDescriptionLength = 512;

    /// <summary>绑定状态信息最大长度</summary>
    public const int MaxStatusMessageLength = 512;

    /// <summary>BLOB 存储键最大长度</summary>
    public const int MaxBlobKeyLength = 512;

    /// <summary>相机位置绑定标签最大长度（如"主相机(左)"）</summary>
    public const int MaxCameraPositionLength = 64;

    /// <summary>有效照片最小数量（内参/外参各需满足才可计算）</summary>
    public const int MinValidPhotoCount = 15;

    /// <summary>单光系列投影外参最小有效照片数量</summary>
    public const int MinProjectorExtrinsicPhotoCount = 18;

    /// <summary>单目标定允许的最大重投影误差（像素，棋盘格）</summary>
    public const double MaxSingleCameraReprojectionError = 0.08d;

    /// <summary>
    /// 圆点标定板单目内参允许的最大重投影误差（像素）。
    /// 圆点检测基于 SimpleBlobDetector 质心，精度低于棋盘格亚像素角点，
    /// 故阈值需高于棋盘格；用于避免对圆点板套用不现实的棋盘格阈值。
    /// </summary>
    public const double MaxCircleBoardReprojectionError = 2.0d;

    /// <summary>
    /// 双目标定允许的最大重投影误差（像素）。
    /// 按项目规范：双目 RMS 应 ≤ 0.2 px，超过才需要重新采集。
    /// </summary>
    public const double MaxStereoReprojectionError = 0.2d;

    /// <summary>内外参矩阵 JSON 最大长度</summary>
    public const int MaxCalibResultJsonLength = 2048;
}

/// <summary>
/// 设备系列枚举
/// </summary>
public enum DeviceSeries
{
    /// <summary>无光系列（纯相机，无结构光投射器）</summary>
    NoLight = 0,

    /// <summary>单光系列（相机 + 结构光投射器）</summary>
    SingleLight = 1,
}

/// <summary>
/// 设备类型枚举
/// </summary>
public enum CalibDeviceType
{
    /// <summary>2目0光：主相机(左) + 从相机(右)</summary>
    TwoCamera0Light = 0,

    /// <summary>1目1光：主相机 + 主结构光</summary>
    OneCamera1Light = 2,

    /// <summary>2目1光：主相机(左) + 从相机(右) + 主结构光</summary>
    TwoCamera1Light = 3,
}

/// <summary>
/// 标定板类型。
/// </summary>
public enum CalibrationBoardType
{
    /// <summary>棋盘格。</summary>
    Chessboard = 0,

    /// <summary>对称圆点网格。</summary>
    SymmetricCircleGrid = 1,

    /// <summary>非对称圆点网格。</summary>
    AsymmetricCircleGrid = 2,

    /// <summary>中心标记对称圆点网格（例如 27x27 中心缺孔）。</summary>
    MarkedSymmetricCircleGrid = 3,
}

/// <summary>
/// 标定流程状态枚举（对应Step 1~7）
/// </summary>
public enum CalibStatus
{
    /// <summary>Step 1：设备初始化中</summary>
    Initializing = 0,

    /// <summary>Step 2：电机参数配置中</summary>
    MotorParamConfig = 1,

    /// <summary>Step 3：电机联动限制配置中</summary>
    MotorConstraintConfig = 2,

    /// <summary>Step 5：相机硬件参数配置中</summary>
    CameraParamConfig = 4,

    /// <summary>Step 6：结构光参数配置中</summary>
    ProjectorParamConfig = 5,

    /// <summary>Step 7：设备绑定中</summary>
    DeviceBinding = 6,

    /// <summary>全部步骤已完成</summary>
    Completed = 7,
}

/// <summary>
/// Step6 在线扫描模式枚举
/// </summary>
public enum CalibScanMode
{
    /// <summary>双目无光模式</summary>
    TwoCamera0Light = 0,

    /// <summary>单目一光模式</summary>
    OneCamera1Light = 1,

    /// <summary>双目一光模式</summary>
    TwoCamera1Light = 2,
}

/// <summary>
/// Step6 在线扫描会话状态枚举
/// </summary>
public enum CalibScanRunState
{
    /// <summary>未运行</summary>
    Idle = 0,

    /// <summary>启动中</summary>
    Starting = 1,

    /// <summary>运行中</summary>
    Running = 2,

    /// <summary>停止中</summary>
    Stopping = 3,

    /// <summary>失败</summary>
    Failed = 4,
}

/// <summary>
/// 回原方向枚举。值与 LeisaiHomingDirection 统一（Negative=0, Positive=1）。
/// </summary>
public enum OriginDirection
{
    /// <summary>反向回原（0）</summary>
    Negative = 0,

    /// <summary>正向回原（1）</summary>
    Positive = 1,
}

/// <summary>
/// 回原模式枚举（对应雷赛 0x600A Bit2，值与 LeisaiHomingMode 相同，但定义于标定领域共享层）
/// </summary>
public enum CalibHomingMode
{
    /// <summary>限位回零（Bit2 = 0）</summary>
    Limit = 0,

    /// <summary>原点回零（Bit2 = 1）</summary>
    Origin = 1,
}

/// <summary>
/// 禁止运动方向枚举（用于联动限制规则）
/// </summary>
public enum ForbiddenDirection
{
    /// <summary>禁止正向运动</summary>
    Positive = 0,

    /// <summary>禁止反向运动</summary>
    Negative = 1,

    /// <summary>禁止双向运动</summary>
    Both = 2,
}

/// <summary>
/// 投射图案类型枚举
/// </summary>
public enum ProjectorPatternType
{
    /// <summary>正弦条纹</summary>
    SineFringe = 0,

    /// <summary>随机散斑</summary>
    RandomSpeckle = 1,

    /// <summary>编码光栅</summary>
    CodedGrating = 2,
}

/// <summary>
/// 设备绑定类型枚举（Step 7）
/// </summary>
public enum CalibBindingType
{
    /// <summary>相机绑定</summary>
    Camera = 0,

    /// <summary>电机绑定</summary>
    Motor = 1,

    /// <summary>结构光投射器绑定</summary>
    Projector = 2,
}

/// <summary>
/// 设备绑定状态枚举
/// </summary>
public enum CalibBindingStatus
{
    /// <summary>待绑定</summary>
    Pending = 0,

    /// <summary>已绑定</summary>
    Bound = 1,

    /// <summary>验证中</summary>
    Verifying = 2,

    /// <summary>验证失败</summary>
    Failed = 3,
}

/// <summary>
/// 标定照片类型枚举（Step 5）
/// </summary>
public enum CalibPhotoType
{
    /// <summary>内参拍照（关灯，拍真实棋盘格）</summary>
    Intrinsic = 0,

    /// <summary>外参拍照（开灯投影棋盘格，同时拍真实棋盘格）</summary>
    Extrinsic = 1,

    /// <summary>双目联合外参成对拍照（左右同步拍真实棋盘格）</summary>
    StereoExtrinsicPair = 2,
}

/// <summary>
/// CalibDeviceType 扩展方法
/// </summary>
public static class CalibDeviceTypeExtensions
{
    /// <summary>
    /// 判断设备类型是否需要投影仪标定
    /// - OneCamera1Light（单目结构光）：需要投影仪标定
    /// - TwoCamera1Light（双目结构光）：不需要，投影仪仅作为纹理生成器
    /// </summary>
    public static bool IsProjectorCalibrationRequired(this CalibDeviceType deviceType)
    {
        return deviceType switch
        {
            CalibDeviceType.OneCamera1Light => true,
            CalibDeviceType.TwoCamera1Light => false,
            _ => false,
        };
    }
}

/// <summary>
/// 双目成对照片中的相机角色
/// </summary>
public enum StereoPhotoRole
{
    /// <summary>主相机（左）</summary>
    Main = 0,

    /// <summary>从相机（右）</summary>
    Secondary = 1,
}

/// <summary>
/// 投影外参双拍阶段。
/// </summary>
public enum ExtrinsicPhotoPhase
{
    /// <summary>关灯拍实体标定板。</summary>
    ProjectorOff = 0,

    /// <summary>开灯拍投影标定图案。</summary>
    ProjectorOn = 1,

    /// <summary>S1 白屏模式拍摄圆点标定板。</summary>
    WhiteScreen = 2,

    /// <summary>S3 棋盘格模式拍摄投影棋盘格。</summary>
    Checkerboard = 3,
}
