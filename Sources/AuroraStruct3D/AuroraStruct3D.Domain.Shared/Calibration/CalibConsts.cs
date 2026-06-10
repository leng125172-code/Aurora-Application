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

    /// <summary>单目标定允许的最大重投影误差（像素）</summary>
    public const double MaxSingleCameraReprojectionError = 0.08d;

    /// <summary>双目标定允许的最大重投影误差（像素）</summary>
    public const double MaxStereoReprojectionError = 0.1d;

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
/// 标定用电机类型枚举
/// </summary>
public enum CalibMotorType
{
    /// <summary>旋转电机（用于相机角度调节，单位：度°）</summary>
    Rotation = 0,

    /// <summary>距离电机/平移电机（用于基线距离调节，单位：mm）</summary>
    Distance = 1,
}

/// <summary>
/// 回原方向枚举
/// </summary>
public enum OriginDirection
{
    /// <summary>正向回原</summary>
    Positive = 0,

    /// <summary>反向回原</summary>
    Negative = 1,
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
/// 双目成对照片中的相机角色
/// </summary>
public enum StereoPhotoRole
{
    /// <summary>主相机（左）</summary>
    Main = 0,

    /// <summary>从相机（右）</summary>
    Secondary = 1,
}
