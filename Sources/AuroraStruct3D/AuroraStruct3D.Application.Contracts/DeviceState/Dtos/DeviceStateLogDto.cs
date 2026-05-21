namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态切换日志 DTO
/// </summary>
public class DeviceStateLogDto
{
    /// <summary>日志记录唯一 ID</summary>
    public Guid Id { get; set; }

    /// <summary>切换发生时间（UTC）</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>切换前设备状态</summary>
    public DeviceStatus? PreviousStatus { get; set; }

    /// <summary>切换后设备状态</summary>
    public DeviceStatus NewStatus { get; set; }

    /// <summary>是否发生了状态变化</summary>
    public bool IsStatusChange { get; set; }

    /// <summary>新状态是否为过渡状态</summary>
    public bool IsTransitionState { get; set; }

    /// <summary>切换前运行模式</summary>
    public DeviceRunMode? PreviousMode { get; set; }

    /// <summary>切换后运行模式</summary>
    public DeviceRunMode NewMode { get; set; }

    /// <summary>是否发生了模式变化</summary>
    public bool IsModeChange { get; set; }

    /// <summary>触发来源</summary>
    public StateChangeTrigger Trigger { get; set; }

    /// <summary>关联故障记录 ID</summary>
    public Guid? FaultId { get; set; }

    /// <summary>操作人 ID</summary>
    public string? OperatorId { get; set; }

    /// <summary>操作人姓名</summary>
    public string? OperatorName { get; set; }

    /// <summary>切换原因描述</summary>
    public string? Reason { get; set; }

    /// <summary>附加备注</summary>
    public string? Remark { get; set; }

    /// <summary>切换耗时（毫秒）</summary>
    public long? DurationMs { get; set; }

    /// <summary>是否切换成功</summary>
    public bool IsSuccessful { get; set; }

    /// <summary>失败错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTime CreationTime { get; set; }
}
