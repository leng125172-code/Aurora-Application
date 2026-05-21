namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态管理模块常量定义
/// </summary>
public static class DeviceStateConsts
{
    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";

    // ─────────────────────────── 状态切换日志字段 ───────────────────────────

    /// <summary>操作人姓名最大长度</summary>
    public const int MaxOperatorNameLength = 64;

    /// <summary>操作人 ID（Guid 字符串化）最大长度</summary>
    public const int MaxOperatorIdLength = 36;

    /// <summary>切换原因描述最大长度</summary>
    public const int MaxReasonLength = 256;

    /// <summary>附加说明最大长度</summary>
    public const int MaxRemarkLength = 512;

    /// <summary>切换失败错误信息最大长度</summary>
    public const int MaxErrorMessageLength = 512;

    // ─────────────────────────── 故障记录字段 ───────────────────────────────

    /// <summary>故障码最大长度（设备或系统错误码）</summary>
    public const int MaxFaultCodeLength = 64;

    /// <summary>故障内容描述最大长度</summary>
    public const int MaxFaultMessageLength = 1024;

    /// <summary>故障原因分析最大长度</summary>
    public const int MaxFaultReasonLength = 512;

    /// <summary>故障处理人 ID 最大长度</summary>
    public const int MaxResolverIdLength = 36;

    /// <summary>故障处理人姓名最大长度</summary>
    public const int MaxResolverNameLength = 64;

    /// <summary>处理措施描述最大长度</summary>
    public const int MaxResolutionDescriptionLength = 512;
}

/// <summary>
/// 设备总状态枚举
/// </summary>
public enum DeviceStatus
{
    /// <summary>待机 — 上电就绪，等待指令</summary>
    Standby = 0,

    /// <summary>启动中 — 正在执行启动序列（过渡状态）</summary>
    Starting = 1,

    /// <summary>运行中 — 设备正常执行生产任务</summary>
    Running = 2,

    /// <summary>暂停 — 生产任务暂停，可恢复</summary>
    Paused = 3,

    /// <summary>停机中 — 正在执行停机序列（过渡状态）</summary>
    Stopping = 4,

    /// <summary>已停机 — 正常停止，可重新启动</summary>
    Stopped = 5,

    /// <summary>复位中 — 正在执行故障/急停复位（过渡状态）</summary>
    Resetting = 6,

    /// <summary>故障确认中 — 等待操作人确认故障（过渡状态）</summary>
    FaultAcknowledging = 7,

    /// <summary>故障 — 发生可复位的故障，需要处理后复位</summary>
    Fault = 8,

    /// <summary>急停 — 触发了急停，切断动力电源，必须人工复位</summary>
    EmergencyStop = 9,

    /// <summary>初始化中 — 程序启动后正在扫描并注册设备，完成后自动转为 Standby（默认初始值）</summary>
    Initializing = 10,
}

/// <summary>
/// 设备运行模式枚举
/// </summary>
public enum DeviceRunMode
{
    /// <summary>联机模式 — 由上位机/MES 远程控制</summary>
    Online = 0,

    /// <summary>自动模式 — 本地自动运行流程</summary>
    Auto = 1,

    /// <summary>手动模式 — 操作人手动单步操作</summary>
    Manual = 2,

    /// <summary>检修模式 — 专业人员维护，禁止生产流程</summary>
    Maintenance = 3,
}

/// <summary>
/// 故障等级枚举（非系统/程序错误的设备故障）
/// </summary>
public enum DeviceFaultLevel
{
    /// <summary>警告 — 不影响运行，仅提示，可继续</summary>
    Warning = 0,

    /// <summary>一般故障 — 停止当前工序，操作人确认后可复位继续</summary>
    GeneralFault = 1,

    /// <summary>严重故障 — 立即停机，需专业人员排查处理</summary>
    SevereFault = 2,

    /// <summary>安全故障 — 触发急停并切断动力电源，需专业人员介入</summary>
    SafetyFault = 3,
}

/// <summary>
/// 状态切换触发来源枚举
/// </summary>
public enum StateChangeTrigger
{
    /// <summary>操作人通过界面手动操作</summary>
    UserManual = 0,

    /// <summary>系统自动流程触发（如流程完成后自动转为 Stopped）</summary>
    SystemAuto = 1,

    /// <summary>设备检测到硬件/传感器故障自动触发</summary>
    DeviceFault = 2,

    /// <summary>急停按钮或安全回路触发</summary>
    EmergencyButton = 3,

    /// <summary>上位机/MES 远程指令触发</summary>
    RemoteCommand = 4,

    /// <summary>看门狗/超时保护触发</summary>
    Watchdog = 5,

    /// <summary>系统启动/初始化触发</summary>
    SystemInit = 6,
}
