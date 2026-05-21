using AuroraStruct3D.DeviceState;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态切换日志实体，记录每次状态或模式的变更。
/// 包含 ABP 标准审计字段（创建时间、创建人、修改、软删除）。
/// </summary>
public class DeviceStateLog : FullAuditedEntity<Guid>
{
    /// <summary>切换发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    // ─── 状态变化 ────────────────────────────────────────────────────────────

    /// <summary>切换前设备状态（首条记录为 null）</summary>
    public DeviceStatus? PreviousStatus { get; private set; }

    /// <summary>切换后设备状态</summary>
    public DeviceStatus NewStatus { get; private set; }

    /// <summary>是否发生了状态变化</summary>
    public bool IsStatusChange { get; private set; }

    /// <summary>新状态是否为过渡状态（Starting/Stopping/Resetting/FaultAcknowledging）</summary>
    public bool IsTransitionState { get; private set; }

    // ─── 模式变化 ────────────────────────────────────────────────────────────

    /// <summary>切换前运行模式（首条记录为 null）</summary>
    public DeviceRunMode? PreviousMode { get; private set; }

    /// <summary>切换后运行模式</summary>
    public DeviceRunMode NewMode { get; private set; }

    /// <summary>是否发生了模式变化</summary>
    public bool IsModeChange { get; private set; }

    // ─── 触发信息 ────────────────────────────────────────────────────────────

    /// <summary>触发来源（用户手动 / 系统自动 / 设备故障 / 急停按钮 / 远程指令等）</summary>
    public StateChangeTrigger Trigger { get; private set; }

    /// <summary>
    /// 关联故障记录 ID（故障或急停触发的切换时填写，指向 DeviceFault 表）
    /// </summary>
    public Guid? FaultId { get; private set; }

    // ─── 操作人信息 ──────────────────────────────────────────────────────────

    /// <summary>操作人 ID（用户手动触发时记录，Guid 字符串，最大 36 字符）</summary>
    public string? OperatorId { get; private set; }

    /// <summary>操作人姓名（最大 64 字符）</summary>
    public string? OperatorName { get; private set; }

    // ─── 原因说明 ────────────────────────────────────────────────────────────

    /// <summary>切换原因描述（最大 256 字符）</summary>
    public string? Reason { get; private set; }

    /// <summary>附加备注（最大 512 字符）</summary>
    public string? Remark { get; private set; }

    // ─── 执行结果 ────────────────────────────────────────────────────────────

    /// <summary>切换耗时（毫秒）</summary>
    public long? DurationMs { get; private set; }

    /// <summary>是否切换成功</summary>
    public bool IsSuccessful { get; private set; }

    /// <summary>失败错误信息（最大 512 字符，IsSuccessful=false 时填写）</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>EF Core 专用无参构造函数</summary>
    protected DeviceStateLog() { }

    /// <summary>
    /// 创建一条设备状态切换日志（私有，通过静态工厂方法创建）
    /// </summary>
    private DeviceStateLog(
        Guid id,
        DeviceStatus? previousStatus,
        DeviceStatus newStatus,
        DeviceRunMode? previousMode,
        DeviceRunMode newMode,
        StateChangeTrigger trigger,
        string? operatorId,
        string? operatorName,
        string? reason,
        string? remark,
        Guid? faultId,
        long? durationMs,
        bool isSuccessful,
        string? errorMessage
    )
    {
        Id = id;
        OccurredAt = DateTime.UtcNow;

        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        IsStatusChange = previousStatus != newStatus;
        IsTransitionState =
            newStatus
                is DeviceStatus.Starting
                    or DeviceStatus.Stopping
                    or DeviceStatus.Resetting
                    or DeviceStatus.FaultAcknowledging;

        PreviousMode = previousMode;
        NewMode = newMode;
        IsModeChange = previousMode != newMode;

        Trigger = trigger;
        FaultId = faultId;
        OperatorId = Truncate(operatorId, DeviceStateConsts.MaxOperatorIdLength);
        OperatorName = Truncate(operatorName, DeviceStateConsts.MaxOperatorNameLength);
        Reason = Truncate(reason, DeviceStateConsts.MaxReasonLength);
        Remark = Truncate(remark, DeviceStateConsts.MaxRemarkLength);
        DurationMs = durationMs;
        IsSuccessful = isSuccessful;
        ErrorMessage = Truncate(errorMessage, DeviceStateConsts.MaxErrorMessageLength);
    }

    // ─── 静态工厂方法 ────────────────────────────────────────────────────────

    /// <summary>
    /// 创建用户手动触发的状态切换日志
    /// </summary>
    public static DeviceStateLog ByUser(
        Guid id,
        DeviceStatus? previousStatus,
        DeviceStatus newStatus,
        DeviceRunMode? previousMode,
        DeviceRunMode newMode,
        string operatorId,
        string operatorName,
        string? reason = null,
        string? remark = null,
        long? durationMs = null
    ) =>
        new(
            id,
            previousStatus,
            newStatus,
            previousMode,
            newMode,
            StateChangeTrigger.UserManual,
            operatorId,
            operatorName,
            reason,
            remark,
            faultId: null,
            durationMs,
            isSuccessful: true,
            errorMessage: null
        );

    /// <summary>
    /// 创建系统自动触发的状态切换日志
    /// </summary>
    public static DeviceStateLog BySystem(
        Guid id,
        DeviceStatus? previousStatus,
        DeviceStatus newStatus,
        DeviceRunMode? previousMode,
        DeviceRunMode newMode,
        StateChangeTrigger trigger,
        string? reason = null,
        Guid? faultId = null,
        long? durationMs = null,
        string? remark = null
    ) =>
        new(
            id,
            previousStatus,
            newStatus,
            previousMode,
            newMode,
            trigger,
            operatorId: null,
            operatorName: null,
            reason,
            remark,
            faultId,
            durationMs,
            isSuccessful: true,
            errorMessage: null
        );

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
