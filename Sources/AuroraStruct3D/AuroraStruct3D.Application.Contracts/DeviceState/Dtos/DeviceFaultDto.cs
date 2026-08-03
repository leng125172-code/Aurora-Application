namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备故障记录 DTO
/// </summary>
public class DeviceFaultDto
{
    /// <summary>故障记录唯一 ID</summary>
    public Guid Id { get; set; }

    /// <summary>故障发生时间（UTC）</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>故障等级</summary>
    public DeviceFaultLevel FaultLevel { get; set; }

    /// <summary>故障码（可选）</summary>
    public string? FaultCode { get; set; }

    /// <summary>故障内容描述</summary>
    public string? FaultMessage { get; set; }

    /// <summary>故障原因分析</summary>
    public string? FaultReason { get; set; }

    public DeviceFaultSource Source { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public Guid? WorkflowProjectId { get; set; }
    public string? WorkflowProjectName { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public Guid? WorkflowId { get; set; }
    public string? WorkflowName { get; set; }
    public string? WorkflowNodeId { get; set; }
    public DateTime LastOccurredAt { get; set; }
    public int OccurrenceCount { get; set; }

    /// <summary>是否已处理</summary>
    public bool IsResolved { get; set; }

    /// <summary>是否系统自动恢复</summary>
    public bool IsAutoRecovered { get; set; }

    /// <summary>处理人 ID</summary>
    public string? ResolverId { get; set; }

    /// <summary>处理人姓名</summary>
    public string? ResolverName { get; set; }

    /// <summary>处理时间（UTC）</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>处理说明</summary>
    public string? ResolutionDescription { get; set; }

    /// <summary>故障持续时长（毫秒）</summary>
    public long? DurationMs { get; set; }

    /// <summary>是否触发了模式切换</summary>
    public bool CausedModeSwitch { get; set; }

    /// <summary>切换后的模式（如发生了模式切换）</summary>
    public DeviceRunMode? SwitchedToMode { get; set; }

    /// <summary>关联状态日志 ID</summary>
    public Guid? StateLogId { get; set; }

    /// <summary>附加备注</summary>
    public string? Remark { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTime CreationTime { get; set; }
}
