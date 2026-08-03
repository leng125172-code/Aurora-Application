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
        Guid duplicate = Guid.NewGuid();
        Guid[] ids = Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray();
        WorkflowPlcHandshakeConfig config = new(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<BusinessException>(() => config.Configure(Guid.NewGuid(),
            duplicate, duplicate, ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            ids[6], ids[7], ids[8], ids[9], ids[10], ids[11], true));
    }

    [Fact]
    public void Accept_Should_Reject_Zero_Request_Id()
    {
        WorkflowPlcHandshakeConfig config = CreateConfigured();
        Assert.Throws<BusinessException>(() => config.Accept(0, Guid.NewGuid(), DateTime.UtcNow));
    }

    private static WorkflowPlcHandshakeConfig CreateConfigured()
    {
        Guid[] ids = Enumerable.Range(0, 14).Select(_ => Guid.NewGuid()).ToArray();
        WorkflowPlcHandshakeConfig config = new(Guid.NewGuid(), Guid.NewGuid());
        config.Configure(Guid.NewGuid(), ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            ids[6], ids[7], ids[8], ids[9], ids[10], ids[11], ids[12], ids[13], true);
        return config;
    }
}
