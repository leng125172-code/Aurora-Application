using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>项目级 OPC UA 生产任务握手配置及持久化协议状态。</summary>
public class WorkflowPlcHandshakeConfig : FullAuditedAggregateRoot<Guid>
{
    public Guid TaskConfigId { get; private set; }
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
    public string CaptureRequestAddress { get; private set; } = string.Empty;
    public string RequestIdAddress { get; private set; } = string.Empty;
    public string ResultAckAddress { get; private set; } = string.Empty;
    public string ResultAckIdAddress { get; private set; } = string.Empty;
    public string HeartbeatAddress { get; private set; } = string.Empty;
    public string DeviceStatusAddress { get; private set; } = string.Empty;
    public string TaskStatusAddress { get; private set; } = string.Empty;
    public string CanCaptureAddress { get; private set; } = string.Empty;
    public string CaptureAckAddress { get; private set; } = string.Empty;
    public string AckRequestIdAddress { get; private set; } = string.Empty;
    public string ResultValidAddress { get; private set; } = string.Empty;
    public string ResultRequestIdAddress { get; private set; } = string.Empty;
    public string ResultCodeAddress { get; private set; } = string.Empty;
    public string ErrorCodeAddress { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public WorkflowPlcHandshakePhase Phase { get; private set; }
    public int CurrentRequestId { get; private set; }
    public int LastCompletedRequestId { get; private set; }
    public long RequestSequence { get; private set; }
    public Guid? CurrentRunId { get; private set; }
    public WorkflowInspectionDecision ResultCode { get; private set; }
    public WorkflowPlcHandshakeErrorCode ErrorCode { get; private set; }
    public DateTime? LastRequestAt { get; private set; }
    public DateTime? LastResultAt { get; private set; }
    public string? LastError { get; private set; }

    protected WorkflowPlcHandshakeConfig() { }

    public WorkflowPlcHandshakeConfig(Guid id, Guid projectId, Guid taskConfigId = default) : base(id)
    {
        if (projectId == Guid.Empty) throw new BusinessException("Workflow.PlcHandshake.ProjectId.Empty");
        ProjectId = projectId;
        TaskConfigId = taskConfigId;
        Phase = WorkflowPlcHandshakePhase.Idle;
    }

    public void Configure(
        Guid plcDeviceId,
        string captureRequestAddress, string requestIdAddress, string resultAckAddress, string resultAckIdAddress,
        string heartbeatAddress, string deviceStatusAddress, string taskStatusAddress, string canCaptureAddress,
        string captureAckAddress, string ackRequestIdAddress, string resultValidAddress,
        string resultRequestIdAddress, string resultCodeAddress, string errorCodeAddress,
        bool isEnabled)
    {
        string[] addresses = [captureRequestAddress, requestIdAddress, resultAckAddress,
            resultAckIdAddress, heartbeatAddress, deviceStatusAddress, taskStatusAddress,
            canCaptureAddress, captureAckAddress, ackRequestIdAddress, resultValidAddress,
            resultRequestIdAddress, resultCodeAddress, errorCodeAddress];
        if (plcDeviceId == Guid.Empty || addresses.Any(string.IsNullOrWhiteSpace))
            throw new BusinessException("Workflow.PlcHandshake.Tag.Empty");
        if (addresses.Distinct(StringComparer.Ordinal).Count() != addresses.Length)
            throw new BusinessException("Workflow.PlcHandshake.Tag.Duplicate");
        PlcDeviceId = plcDeviceId;
        CaptureRequestAddress = captureRequestAddress.Trim(); RequestIdAddress = requestIdAddress.Trim();
        ResultAckAddress = resultAckAddress.Trim(); ResultAckIdAddress = resultAckIdAddress.Trim();
        HeartbeatAddress = heartbeatAddress.Trim(); DeviceStatusAddress = deviceStatusAddress.Trim();
        TaskStatusAddress = taskStatusAddress.Trim(); CanCaptureAddress = canCaptureAddress.Trim();
        CaptureAckAddress = captureAckAddress.Trim(); AckRequestIdAddress = ackRequestIdAddress.Trim();
        ResultValidAddress = resultValidAddress.Trim(); ResultRequestIdAddress = resultRequestIdAddress.Trim();
        ResultCodeAddress = resultCodeAddress.Trim(); ErrorCodeAddress = errorCodeAddress.Trim();
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

    public long AllocateRequestSequence()
    {
        RequestSequence = RequestSequence == long.MaxValue ? 1 : RequestSequence + 1;
        return RequestSequence;
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
