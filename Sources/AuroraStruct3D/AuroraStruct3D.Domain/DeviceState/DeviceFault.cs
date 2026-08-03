using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备故障记录聚合根，记录每次设备故障的完整生命周期信息。
/// 包含 ABP 标准审计字段（创建时间、创建人、修改、软删除）。
/// 数据库表：AbpProDeviceFaults
/// </summary>
public class DeviceFault : FullAuditedAggregateRoot<Guid>
{
    // ─── 故障发生 ────────────────────────────────────────────────────────────

    /// <summary>故障发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>故障级别</summary>
    public DeviceFaultLevel FaultLevel { get; private set; }

    /// <summary>故障码（设备或系统错误码，最大 64 字符）</summary>
    public string? FaultCode { get; private set; }

    /// <summary>故障内容描述（最大 1024 字符）</summary>
    public string? FaultMessage { get; private set; }

    /// <summary>故障原因分析（最大 512 字符）</summary>
    public string? FaultReason { get; private set; }

    /// <summary>故障来源。</summary>
    public DeviceFaultSource Source { get; private set; }

    /// <summary>用于合并同一活跃故障的稳定指纹。</summary>
    public string? Fingerprint { get; private set; }

    public Guid? DeviceId { get; private set; }
    public string? DeviceName { get; private set; }
    public Guid? WorkflowProjectId { get; private set; }
    public string? WorkflowProjectName { get; private set; }
    public Guid? WorkflowRunId { get; private set; }
    public Guid? WorkflowId { get; private set; }
    public string? WorkflowName { get; private set; }
    public string? WorkflowNodeId { get; private set; }
    public DateTime LastOccurredAt { get; private set; }
    public int OccurrenceCount { get; private set; }

    // ─── 处理状态 ────────────────────────────────────────────────────────────

    /// <summary>故障是否已处理（false = 未处理，含重启自动解决）</summary>
    public bool IsResolved { get; private set; }

    /// <summary>是否自动恢复（系统自动消除，无需人工干预）</summary>
    public bool IsAutoRecovered { get; private set; }

    // ─── 处理信息 ────────────────────────────────────────────────────────────

    /// <summary>故障处理人 ID（最大 36 字符）</summary>
    public string? ResolverId { get; private set; }

    /// <summary>故障处理人姓名（最大 64 字符）</summary>
    public string? ResolverName { get; private set; }

    /// <summary>处理时间（UTC）</summary>
    public DateTime? ResolvedAt { get; private set; }

    /// <summary>处理措施描述（最大 512 字符）</summary>
    public string? ResolutionDescription { get; private set; }

    /// <summary>故障持续时间（毫秒，ResolvedAt - OccurredAt）</summary>
    public long? DurationMs { get; private set; }

    // ─── 模式影响 ────────────────────────────────────────────────────────────

    /// <summary>故障是否导致运行模式切换</summary>
    public bool CausedModeSwitch { get; private set; }

    /// <summary>故障触发后切换到的运行模式（CausedModeSwitch=true 时有值）</summary>
    public DeviceRunMode? SwitchedToMode { get; private set; }

    // ─── 关联信息 ────────────────────────────────────────────────────────────

    /// <summary>关联的首条状态切换日志 ID（指向 DeviceStateLog 表）</summary>
    public Guid? StateLogId { get; private set; }

    /// <summary>附加备注（最大 512 字符）</summary>
    public string? Remark { get; private set; }

    // ─── 构造函数 ────────────────────────────────────────────────────────────

    /// <summary>EF Core 专用无参构造函数</summary>
    protected DeviceFault() { }

    /// <summary>
    /// 记录一次新故障
    /// </summary>
    /// <param name="id">聚合根 ID（由调用方生成，便于同步关联日志 ID）</param>
    /// <param name="faultLevel">故障级别</param>
    /// <param name="faultCode">故障码</param>
    /// <param name="faultMessage">故障内容描述</param>
    /// <param name="faultReason">故障原因分析</param>
    /// <param name="causedModeSwitch">是否导致模式切换</param>
    /// <param name="switchedToMode">切换后的运行模式</param>
    /// <param name="stateLogId">关联状态日志 ID</param>
    /// <param name="remark">附加备注</param>
    public DeviceFault(
        Guid id,
        DeviceFaultLevel faultLevel,
        string? faultCode = null,
        string? faultMessage = null,
        string? faultReason = null,
        bool causedModeSwitch = false,
        DeviceRunMode? switchedToMode = null,
        Guid? stateLogId = null,
        string? remark = null
    )
    {
        Id = id;
        OccurredAt = DateTime.UtcNow;
        FaultLevel = faultLevel;
        FaultCode = Truncate(faultCode, DeviceStateConsts.MaxFaultCodeLength);
        FaultMessage = Truncate(faultMessage, DeviceStateConsts.MaxFaultMessageLength);
        FaultReason = Truncate(faultReason, DeviceStateConsts.MaxFaultReasonLength);
        CausedModeSwitch = causedModeSwitch;
        SwitchedToMode = switchedToMode;
        StateLogId = stateLogId;
        Remark = Truncate(remark, DeviceStateConsts.MaxRemarkLength);
        IsResolved = false;
        Source = DeviceFaultSource.System;
        LastOccurredAt = OccurredAt;
        OccurrenceCount = 1;
    }

    /// <summary>设置运行故障的来源与关联信息。</summary>
    public void SetRuntimeContext(DeviceFaultReport report)
    {
        Source = report.Source;
        Fingerprint = Truncate(report.Fingerprint, 256);
        DeviceId = report.DeviceId;
        DeviceName = Truncate(report.DeviceName, 128);
        WorkflowProjectId = report.WorkflowProjectId;
        WorkflowProjectName = Truncate(report.WorkflowProjectName, 128);
        WorkflowRunId = report.WorkflowRunId;
        WorkflowId = report.WorkflowId;
        WorkflowName = Truncate(report.WorkflowName, 128);
        WorkflowNodeId = Truncate(report.WorkflowNodeId, 128);
    }

    /// <summary>合并一次同类活跃故障。</summary>
    public void Reoccur(string message)
    {
        FaultMessage = Truncate(message, DeviceStateConsts.MaxFaultMessageLength);
        LastOccurredAt = DateTime.UtcNow;
        OccurrenceCount++;
    }

    // ─── 领域方法 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 标记故障已由操作人手动处理
    /// </summary>
    /// <param name="resolverId">处理人 ID</param>
    /// <param name="resolverName">处理人姓名</param>
    /// <param name="resolutionDescription">处理措施描述</param>
    /// <param name="remark">附加备注</param>
    public void MarkResolved(
        string resolverId,
        string resolverName,
        string? resolutionDescription = null,
        string? remark = null
    )
    {
        IsResolved = true;
        IsAutoRecovered = false;
        ResolverId = Truncate(resolverId, DeviceStateConsts.MaxResolverIdLength);
        ResolverName = Truncate(resolverName, DeviceStateConsts.MaxResolverNameLength);
        ResolvedAt = DateTime.UtcNow;
        ResolutionDescription = Truncate(
            resolutionDescription,
            DeviceStateConsts.MaxResolutionDescriptionLength
        );
        if (remark is not null)
            Remark = Truncate(remark, DeviceStateConsts.MaxRemarkLength);
        DurationMs = (long)(ResolvedAt.Value - OccurredAt).TotalMilliseconds;
    }

    /// <summary>
    /// 标记故障已由系统自动恢复（无需人工干预）
    /// </summary>
    /// <param name="reason">自动恢复原因说明</param>
    public void MarkAutoRecovered(string? reason = null)
    {
        IsResolved = true;
        IsAutoRecovered = true;
        ResolvedAt = DateTime.UtcNow;
        ResolutionDescription = Truncate(reason, DeviceStateConsts.MaxResolutionDescriptionLength);
        DurationMs = (long)(ResolvedAt.Value - OccurredAt).TotalMilliseconds;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
