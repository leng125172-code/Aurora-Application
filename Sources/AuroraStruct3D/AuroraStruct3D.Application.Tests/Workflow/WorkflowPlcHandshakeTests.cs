using Volo.Abp;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class WorkflowPlcHandshakeTests
{
    [Fact]
    public void Handshake_Should_Keep_Request_And_Result_State_Until_Acknowledged()
    {
        WorkflowPlcHandshakeConfig config = CreateConfigured();
        Guid runId = Guid.NewGuid();

        config.Accept(41, runId, DateTime.UtcNow);
        Assert.Equal(WorkflowPlcHandshakePhase.Accepted, config.Phase);
        Assert.Equal(41, config.CurrentRequestId);
        Assert.Equal(runId, config.CurrentRunId);

        config.MarkRunning();
        config.SetResult(WorkflowInspectionDecision.Ng, WorkflowPlcHandshakeErrorCode.None, null, DateTime.UtcNow);
        Assert.Equal(WorkflowPlcHandshakePhase.ResultPending, config.Phase);
        Assert.Equal(41, config.LastCompletedRequestId);
        Assert.Equal(WorkflowInspectionDecision.Ng, config.ResultCode);

        config.CompleteAcknowledgement();
        Assert.Equal(WorkflowPlcHandshakePhase.Idle, config.Phase);
        Assert.Equal(0, config.CurrentRequestId);
        Assert.Equal(41, config.LastCompletedRequestId);
        Assert.Equal(WorkflowInspectionDecision.None, config.ResultCode);
    }

    [Fact]
    public void Configure_Should_Reject_A_Tag_Used_By_Two_Roles()
    {
        const string duplicate = "ns=2;s=Duplicate";
        string[] addresses = Enumerable.Range(0, 13).Select(x => $"ns=2;s=Point{x}").ToArray();
        WorkflowPlcHandshakeConfig config = new(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<BusinessException>(() => config.Configure(Guid.NewGuid(),
            duplicate, duplicate, addresses[0], addresses[1], addresses[2], addresses[3],
            addresses[4], addresses[5], addresses[6], addresses[7], addresses[8],
            addresses[9], addresses[10], addresses[11], true));
    }

    [Fact]
    public void Accept_Should_Reject_Zero_Request_Id()
    {
        WorkflowPlcHandshakeConfig config = CreateConfigured();
        Assert.Throws<BusinessException>(() => config.Accept(0, Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Request_Sequence_Should_Be_Monotonic_Per_Handshake()
    {
        WorkflowPlcHandshakeConfig config = CreateConfigured();

        Assert.Equal(1, config.AllocateRequestSequence());
        Assert.Equal(2, config.AllocateRequestSequence());
        Assert.Equal(2, config.RequestSequence);
    }

    [Fact]
    public void Communication_Failure_Should_Be_Visible_In_Handshake_Status()
    {
        WorkflowPlcHandshakeConfig config = CreateConfigured();

        config.MarkProtocolFault("Handshake output 'CaptureAck (i=113)' is not writable: BadNotWritable");

        Assert.Equal(WorkflowPlcHandshakePhase.Fault, config.Phase);
        Assert.Equal(WorkflowPlcHandshakeErrorCode.PlcCommunicationFailed, config.ErrorCode);
        Assert.Contains("CaptureAck (i=113)", config.LastError);
        Assert.Contains("BadNotWritable", config.LastError);
    }

    private static WorkflowPlcHandshakeConfig CreateConfigured()
    {
        string[] addresses = Enumerable.Range(0, 14).Select(x => $"ns=2;s=Point{x}").ToArray();
        WorkflowPlcHandshakeConfig config = new(Guid.NewGuid(), Guid.NewGuid());
        config.Configure(Guid.NewGuid(), addresses[0], addresses[1], addresses[2], addresses[3],
            addresses[4], addresses[5], addresses[6], addresses[7], addresses[8], addresses[9],
            addresses[10], addresses[11], addresses[12], addresses[13], true);
        return config;
    }
}
