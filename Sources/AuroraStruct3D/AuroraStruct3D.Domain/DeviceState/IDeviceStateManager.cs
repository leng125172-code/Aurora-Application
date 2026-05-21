using AuroraStruct3D.DeviceState;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 切换操作上下文，描述本次切换的原因和发起者信息
/// </summary>
public sealed class StateChangeContext
{
    /// <summary>触发来源</summary>
    public StateChangeTrigger Trigger { get; init; }

    /// <summary>切换原因描述（可选）</summary>
    public string? Reason { get; init; }

    /// <summary>操作人 ID（用户手动触发时填写）</summary>
    public string? OperatorId { get; init; }

    /// <summary>操作人姓名（用户手动触发时填写）</summary>
    public string? OperatorName { get; init; }

    /// <summary>故障等级（状态变为 Fault/EmergencyStop 时填写）</summary>
    public DeviceFaultLevel? FaultLevel { get; init; }

    /// <summary>故障码（可选）</summary>
    public string? FaultCode { get; init; }

    /// <summary>故障内容描述（写入 DeviceFault.FaultMessage，最大 1024 字符）</summary>
    public string? FaultMessage { get; init; }

    /// <summary>故障原因分析（写入 DeviceFault.FaultReason，最大 512 字符）</summary>
    public string? FaultReason { get; init; }

    /// <summary>附加备注（可选）</summary>
    public string? Remark { get; init; }

    // ─── 常用预设工厂方法 ─────────────────────────────────────────────────────

    /// <summary>用户手动操作上下文</summary>
    public static StateChangeContext User(
        string operatorId,
        string operatorName,
        string? reason = null
    ) =>
        new()
        {
            Trigger = StateChangeTrigger.UserManual,
            OperatorId = operatorId,
            OperatorName = operatorName,
            Reason = reason,
        };

    /// <summary>系统自动触发上下文</summary>
    public static StateChangeContext System(string? reason = null) =>
        new() { Trigger = StateChangeTrigger.SystemAuto, Reason = reason };

    /// <summary>设备故障触发上下文</summary>
    public static StateChangeContext Fault(
        DeviceFaultLevel faultLevel,
        string? faultCode = null,
        string? faultMessage = null,
        string? faultReason = null,
        string? reason = null
    ) =>
        new()
        {
            Trigger = StateChangeTrigger.DeviceFault,
            FaultLevel = faultLevel,
            FaultCode = faultCode,
            FaultMessage = faultMessage,
            FaultReason = faultReason,
            Reason = reason,
        };

    /// <summary>急停按钮触发上下文</summary>
    public static StateChangeContext Emergency(
        string? faultMessage = null,
        string? reason = null
    ) =>
        new()
        {
            Trigger = StateChangeTrigger.EmergencyButton,
            FaultLevel = DeviceFaultLevel.SafetyFault,
            FaultMessage = faultMessage,
            Reason = reason ?? "急停按钮触发",
        };

    /// <summary>远程指令触发上下文</summary>
    public static StateChangeContext Remote(string? reason = null) =>
        new() { Trigger = StateChangeTrigger.RemoteCommand, Reason = reason };

    /// <summary>系统初始化触发上下文</summary>
    public static StateChangeContext Init() =>
        new() { Trigger = StateChangeTrigger.SystemInit, Reason = "系统初始化" };
}

/// <summary>
/// 设备状态管理器接口（单例注入）。
/// 负责管理设备的总状态（Status）与运行模式（RunMode），
/// 执行状态机互锁校验，并异步写入状态切换日志。
/// </summary>
public interface IDeviceStateManager
{
    // ─── 当前状态只读属性 ─────────────────────────────────────────────────────

    /// <summary>当前设备总状态</summary>
    DeviceStatus Status { get; }

    /// <summary>当前运行模式</summary>
    DeviceRunMode RunMode { get; }

    /// <summary>当前故障等级（无故障时为 null）</summary>
    DeviceFaultLevel? CurrentFaultLevel { get; }

    /// <summary>当前故障码（无故障时为 null）</summary>
    string? CurrentFaultCode { get; }

    /// <summary>当前活跃故障记录的数据库 ID（无活跃故障时为 null）</summary>
    Guid? CurrentFaultId { get; }

    // ─── 状态查询 ─────────────────────────────────────────────────────────────

    /// <summary>设备是否处于过渡状态（Starting/Stopping/Resetting/FaultAcknowledging）</summary>
    bool IsInTransition { get; }

    /// <summary>设备是否可以接收生产指令（Running 或 Paused 状态）</summary>
    bool CanAcceptProductionCommand { get; }

    /// <summary>设备是否可以切换运行模式（Standby 或 Stopped 状态且不在过渡中）</summary>
    bool CanSwitchMode { get; }

    // ─── 状态切换方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 启动设备（Standby/Stopped → Starting → Running）
    /// </summary>
    /// <exception cref="InvalidOperationException">当前状态不允许启动时</exception>
    Task StartAsync(StateChangeContext context);

    /// <summary>
    /// 暂停设备（Running → Paused）
    /// </summary>
    Task PauseAsync(StateChangeContext context);

    /// <summary>
    /// 恢复设备（Paused → Running）
    /// </summary>
    Task ResumeAsync(StateChangeContext context);

    /// <summary>
    /// 正常停机（Running/Paused → Stopping → Stopped）
    /// </summary>
    Task StopAsync(StateChangeContext context);

    /// <summary>
    /// 急停（任意可运行状态 → EmergencyStop）
    /// 直接切换，不经过过渡状态
    /// </summary>
    Task EmergencyStopAsync(StateChangeContext context);

    /// <summary>
    /// 触发故障（任意状态 → Fault）
    /// </summary>
    Task FaultAsync(StateChangeContext context);

    /// <summary>
    /// 确认故障（Fault → FaultAcknowledging → Standby）
    /// </summary>
    Task AcknowledgeFaultAsync(StateChangeContext context);

    /// <summary>
    /// 复位（EmergencyStop/Fault/Stopped → Resetting → Standby）
    /// </summary>
    Task ResetAsync(StateChangeContext context);

    // ─── 模式切换 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 切换运行模式（仅在 Standby 或 Stopped 时允许）
    /// </summary>
    /// <exception cref="InvalidOperationException">当前状态不允许切换模式时</exception>
    Task SwitchModeAsync(DeviceRunMode newMode, StateChangeContext context);

    /// <summary>
    /// 完成初始化（Initializing → Standby）。
    /// 由启动初始化 Hangfire 任务在设备扫描完成后调用。
    /// </summary>
    Task CompleteInitializationAsync(StateChangeContext context);

    // ─── 事件 ─────────────────────────────────────────────────────────────────

    /// <summary>状态发生变化时触发（oldStatus, newStatus, context）</summary>
    event Action<DeviceStatus, DeviceStatus, StateChangeContext>? StatusChanged;

    /// <summary>模式发生变化时触发（oldMode, newMode, context）</summary>
    event Action<DeviceRunMode, DeviceRunMode, StateChangeContext>? ModeChanged;
}
