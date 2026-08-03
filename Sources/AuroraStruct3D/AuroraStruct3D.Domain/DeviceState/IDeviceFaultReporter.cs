namespace AuroraStruct3D.DeviceState;

/// <summary>不改变整机状态的设备运行故障上报器。</summary>
public interface IDeviceFaultReporter
{
    Task ReportAsync(DeviceFaultReport report, CancellationToken cancellationToken = default);

    Task RecoverAsync(string fingerprint, string? reason = null, CancellationToken cancellationToken = default);
}

/// <summary>设备运行故障的结构化上报内容。</summary>
public sealed class DeviceFaultReport
{
    public required DeviceFaultSource Source { get; init; }
    public required string FaultCode { get; init; }
    public required string FaultMessage { get; init; }
    public required string Fingerprint { get; init; }
    public DeviceFaultLevel FaultLevel { get; init; } = DeviceFaultLevel.GeneralFault;
    public Guid? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public Guid? WorkflowProjectId { get; init; }
    public string? WorkflowProjectName { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowId { get; init; }
    public string? WorkflowName { get; init; }
    public string? WorkflowNodeId { get; init; }
}
