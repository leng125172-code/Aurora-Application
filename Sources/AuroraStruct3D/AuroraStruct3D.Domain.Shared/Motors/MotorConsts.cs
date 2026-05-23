namespace AuroraStruct3D.Motors;

/// <summary>
/// 伺服电机模块常量定义
/// </summary>
public static class MotorConsts
{
    /// <summary>电机轴名称最大长度（如"X轴"、"Y轴"）</summary>
    public const int MaxNameLength = 128;

    /// <summary>描述最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>串口设备路径最大长度（如 /dev/ttyS6）</summary>
    public const int MaxPortNameLength = 64;

    /// <summary>品牌名称最大长度</summary>
    public const int MaxBrandLength = 64;

    /// <summary>型号最大长度</summary>
    public const int MaxModelLength = 64;

    /// <summary>运动配置名称最大长度</summary>
    public const int MaxMotionConfigNameLength = 128;

    /// <summary>PR路径名称最大长度（雷赛电机）</summary>
    public const int MaxPrPathNameLength = 64;

    /// <summary>瓴控单圈角度单位上限（0.01°，36000 表示一圈）</summary>
    public const long KtechSingleTurnAngleUnits = 36000;

    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";

    // ─────────────────────────── 操作日志字段长度 ───────────────────────────

    /// <summary>命令码最大长度（如 "0x9A Move"、"Modbus FC10"）</summary>
    public const int MaxCommandCodeLength = 32;

    /// <summary>操作参数摘要最大长度（如 "位置=10000, 速度=300rpm"）</summary>
    public const int MaxOperationParameterSummaryLength = 128;

    /// <summary>操作日志错误消息最大长度</summary>
    public const int MaxOperationLogErrorMessageLength = 512;
}

/// <summary>
/// 电机品牌协议类型
/// </summary>
public enum MotorBrand
{
    /// <summary>瓴控KTECH — 私有协议 CMD 0x9A，上电自动使能，硬限位回原</summary>
    KtechKtech = 0,

    /// <summary>雷赛iCL-RS — Modbus RTU 协议，需软件使能，光电开关DI回原</summary>
    LeisaiIclRs = 1,
}

/// <summary>
/// 电机当前状态枚举
/// </summary>
public enum MotorDeviceStatus
{
    /// <summary>未知/未连接</summary>
    Unknown = 0,

    /// <summary>离线</summary>
    Offline = 1,

    /// <summary>在线但未使能</summary>
    Online = 2,

    /// <summary>已使能，待机中</summary>
    Enabled = 3,

    /// <summary>运动中</summary>
    Moving = 4,

    /// <summary>故障报警</summary>
    Faulted = 5,
}

/// <summary>
/// 回零方式枚举
/// </summary>
public enum HomeMethod
{
    /// <summary>硬限位回原 — 电机低速撞限位，驱动器内部清零（瓴控KTECH）</summary>
    HardLimit = 0,

    /// <summary>光电开关DI回原 — 触发外部传感器DI信号后停止清零（雷赛iCL-RS）</summary>
    PhotoelectricSwitchDI = 1,

    /// <summary>力矩回零 — 电机堵转超时后判定为原点（雷赛iCL-RS 支持）</summary>
    TorqueStall = 2,

    /// <summary>手动设零 — 将当前位置直接设为零点</summary>
    ManualSetZero = 3,
}

/// <summary>
/// 电机控制操作类型枚举（用于 MotorOperationLog 操作分类）
/// </summary>
public enum MotorOperationType
{
    /// <summary>使能电机</summary>
    Enable = 0,

    /// <summary>去使能电机</summary>
    Disable = 1,

    /// <summary>绝对位置运动</summary>
    MoveAbsolute = 2,

    /// <summary>相对位置运动</summary>
    MoveRelative = 3,

    /// <summary>减速停止</summary>
    Stop = 4,

    /// <summary>紧急停止（立即断电）</summary>
    EmergencyStop = 5,

    /// <summary>回零</summary>
    Home = 6,

    /// <summary>清除故障</summary>
    ClearFault = 7,

    /// <summary>查询状态</summary>
    QueryStatus = 8,
}
