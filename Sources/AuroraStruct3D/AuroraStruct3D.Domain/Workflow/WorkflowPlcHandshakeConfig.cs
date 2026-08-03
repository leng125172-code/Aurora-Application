using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>项目级 OPC UA 生产任务握手配置及持久化协议状态。</summary>
public class WorkflowPlcHandshakeConfig : FullAuditedAggregateRoot<Guid>
{
    public Guid ProjectId { get; private set; }
    public Guid PlcDeviceId { get; private set; }
    public Guid CaptureRequestTagId { get; private set; }
    public Guid RequestIdTagId { get; private set; }
    public Guid ResultAckTagId { get; private set; }
    public Guid ResultAckIdTagId { get; private set; }
    public Guid HeartbeatTagId { get; private set; }
    public Guid DeviceStatusTagId { get; private set; }
    public Guid TaskStatusTagId { get; private set; }
    public Guid CanCaptureTagId { get; private set; }
    public Guid CaptureAckTagId { get; private set; }
    public Guid AckRequestIdTagId { get; private set; }
    public Guid ResultValidTagId { get; private set; }
    public Guid ResultRequestIdTagId { get; private set; }
    public Guid ResultCodeTagId { get; private set; }
    public Guid ErrorCodeTagId { get; private set; }
    public bool IsEnabled { get; private set; }
    public WorkflowPlcHandshakePhase Phase { get; private set; }
    public int CurrentRequestId { get; private set; }
    public int LastCompletedRequestId { get; private set; }
    public Guid? CurrentRunId { get; private set; }
    public WorkflowInspectionDecision ResultCode { get; private set; }
    public WorkflowPlcHandshakeErrorCode ErrorCode { get; private set; }
    public DateTime? LastRequestAt { get; private set; }
    public DateTime? LastResultAt { get; private set; }
    public string? LastError { get; private set; }

    protected WorkflowPlcHandshakeConfig() { }

    public WorkflowPlcHandshakeConfig(Guid id, Guid projectId) : base(id)
    {
        if (projectId == Guid.Empty) throw new BusinessException("Workflow.PlcHandshake.ProjectId.Empty");
        ProjectId = projectId;
        Phase = WorkflowPlcHandshakePhase.Idle;
    }

    public void Configure(
        Guid plcDeviceId,
        Guid captureRequestTagId, Guid requestIdTagId, Guid resultAckTagId, Guid resultAckIdTagId,
        Guid heartbeatTagId, Guid deviceStatusTagId, Guid taskStatusTagId, Guid canCaptureTagId,
        Guid captureAckTagId, Guid ackRequestIdTagId, Guid resultValidTagId,
        Guid resultRequestIdTagId, Guid resultCodeTagId, Guid errorCodeTagId,
        bool isEnabled)
    {
        Guid[] ids = [plcDeviceId, captureRequestTagId, requestIdTagId, resultAckTagId,
            resultAckIdTagId, heartbeatTagId, deviceStatusTagId, taskStatusTagId,
            canCaptureTagId, captureAckTagId, ackRequestIdTagId, resultValidTagId,
            resultRequestIdTagId, resultCodeTagId, errorCodeTagId];
        if (ids.Any(x => x == Guid.Empty)) throw new BusinessException("Workflow.PlcHandshake.Tag.Empty");
        if (ids.Skip(1).Distinct().Count() != ids.Length - 1)
            throw new BusinessException("Workflow.PlcHandshake.Tag.Duplicate");
        PlcDeviceId = plcDeviceId;
        CaptureRequestTagId = captureRequestTagId; RequestIdTagId = requestIdTagId;
        ResultAckTagId = resultAckTagId; ResultAckIdTagId = resultAckIdTagId;
        HeartbeatTagId = heartbeatTagId; DeviceStatusTagId = deviceStatusTagId;
        TaskStatusTagId = taskStatusTagId; CanCaptureTagId = canCaptureTagId;
        CaptureAckTagId = captureAckTagId; AckRequestIdTagId = ackRequestIdTagId;
        ResultValidTagId = resultValidTagId; ResultRequestIdTagId = resultRequestIdTagId;
        ResultCodeTagId = resultCodeTagId; ErrorCodeTagId = errorCodeTagId;
        IsEnabled = isEnabled;
    }

    public void Accept(int requestId, Guid runId, DateTime now)
    {
        if (requestId <= 0) throw new BusinessException("Workflow.PlcHandshake.RequestId.Invalid");
        CurrentRequestId = requestId; CurrentRunId = runId;
        Phase = WorkflowPlcHandshakePhase.Accepted; LastRequestAt = now;
        ResultCode = WorkflowInspectionDecision.None; ErrorCode = WorkflowPlcHandshakeErrorCode.None;
        LastError = null;
    }

    public void MarkRunning() => Phase = WorkflowPlcHandshakePhase.Running;

    public void SetResult(WorkflowInspectionDecision result, WorkflowPlcHandshakeErrorCode error, string? message, DateTime now)
    {
        ResultCode = result; ErrorCode = error; LastError = Trim(message);
        LastCompletedRequestId = CurrentRequestId; Phase = WorkflowPlcHandshakePhase.ResultPending;
        LastResultAt = now;
    }

    public void CompleteAcknowledgement()
    {
        Phase = WorkflowPlcHandshakePhase.Idle; CurrentRequestId = 0; CurrentRunId = null;
        ResultCode = WorkflowInspectionDecision.None; ErrorCode = WorkflowPlcHandshakeErrorCode.None;
        LastError = null;
    }

    public void MarkProtocolFault(string message)
    {
        Phase = WorkflowPlcHandshakePhase.Fault;
        ErrorCode = WorkflowPlcHandshakeErrorCode.PlcCommunicationFailed;
        LastError = Trim(message);
    }

    public void Reset()
    {
        Phase = WorkflowPlcHandshakePhase.Idle; CurrentRequestId = 0; CurrentRunId = null;
        ResultCode = WorkflowInspectionDecision.None; ErrorCode = WorkflowPlcHandshakeErrorCode.None;
        LastError = null;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 1024)];
}
