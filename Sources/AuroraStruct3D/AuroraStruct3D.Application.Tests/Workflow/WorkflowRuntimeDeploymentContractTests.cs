using System.Reflection;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowRuntimeDeploymentContractTests
{
    [Fact]
    public void Deployment_Map_Should_Set_Current_Active_And_Action_Flags_For_Published()
    {
        WorkflowProjectDeployment deployment = CreatePublishedDeployment();

        WorkflowProjectDeploymentDto dto = MapDeployment(deployment);

        Assert.False(dto.IsCurrentActive);
        Assert.True(dto.CanActivate);
        Assert.True(dto.CanReactivate);
        Assert.True(dto.CanRollback);
        Assert.Equal(2, dto.ItemCount);
    }

    [Fact]
    public void Deployment_Map_Should_Set_Current_Active_And_Action_Flags_For_Activated()
    {
        WorkflowProjectDeployment deployment = CreatePublishedDeployment();
        deployment.Activate(Guid.NewGuid(), DateTime.UtcNow);

        WorkflowProjectDeploymentDto dto = MapDeployment(deployment);

        Assert.True(dto.IsCurrentActive);
        Assert.False(dto.CanActivate);
        Assert.True(dto.CanReactivate);
        Assert.False(dto.CanRollback);
    }

    [Fact]
    public void Deployment_Map_Should_Set_Current_Active_And_Action_Flags_For_Archived()
    {
        WorkflowProjectDeployment deployment = CreatePublishedDeployment();
        deployment.Archive();

        WorkflowProjectDeploymentDto dto = MapDeployment(deployment);

        Assert.False(dto.IsCurrentActive);
        Assert.True(dto.CanActivate);
        Assert.True(dto.CanReactivate);
        Assert.True(dto.CanRollback);
    }

    [Fact]
    public void Deployment_Entity_Should_Support_Activate_And_Archive_State_Transitions()
    {
        WorkflowProjectDeployment deployment = CreatePublishedDeployment();
        Guid userId = Guid.NewGuid();
        DateTime activatedAt = DateTime.UtcNow;

        deployment.Activate(userId, activatedAt);
        deployment.Archive();

        Assert.Equal(WorkflowProjectDeploymentStatus.Archived, deployment.Status);
        Assert.Equal(userId, deployment.ActivatedBy);
        Assert.Equal(activatedAt, deployment.ActivatedAt);
    }

    [Fact]
    public void Enqueue_Result_Deployment_Fields_Should_Be_Required_After_Mandatory_Deployment()
    {
        PropertyInfo deploymentIdProperty = typeof(WorkflowProjectRunEnqueueResultDto).GetProperty(
            nameof(WorkflowProjectRunEnqueueResultDto.DeploymentId)
        )!;
        PropertyInfo deploymentRevisionProperty =
            typeof(WorkflowProjectRunEnqueueResultDto).GetProperty(
                nameof(WorkflowProjectRunEnqueueResultDto.DeploymentRevision)
            )!;

        Assert.Equal(typeof(Guid), deploymentIdProperty.PropertyType);
        Assert.Equal(typeof(int), deploymentRevisionProperty.PropertyType);
    }

    [Fact]
    public void Execution_Result_Dto_Should_Expose_Error_And_Message_While_Status_Keeps_ErrorMessage()
    {
        PropertyInfo triggerErrorProperty = typeof(WorkflowExecutionTriggerResultDto).GetProperty(
            nameof(WorkflowExecutionTriggerResultDto.Error)
        )!;
        PropertyInfo triggerMessageProperty = typeof(WorkflowExecutionTriggerResultDto).GetProperty(
            nameof(WorkflowExecutionTriggerResultDto.Message)
        )!;
        PropertyInfo stepErrorProperty = typeof(WorkflowExecutionStepResultDto).GetProperty(
            nameof(WorkflowExecutionStepResultDto.Error)
        )!;
        PropertyInfo stepMessageProperty = typeof(WorkflowExecutionStepResultDto).GetProperty(
            nameof(WorkflowExecutionStepResultDto.Message)
        )!;
        PropertyInfo statusErrorMessageProperty = typeof(WorkflowExecutionStatusDto).GetProperty(
            nameof(WorkflowExecutionStatusDto.ErrorMessage)
        )!;

        Assert.Equal(typeof(bool), triggerErrorProperty.PropertyType);
        Assert.Equal(typeof(string), triggerMessageProperty.PropertyType);
        Assert.Equal(typeof(bool), stepErrorProperty.PropertyType);
        Assert.Equal(typeof(string), stepMessageProperty.PropertyType);
        Assert.Equal(typeof(string), statusErrorMessageProperty.PropertyType);
    }

    private static WorkflowProjectDeployment CreatePublishedDeployment()
    {
        List<WorkflowProjectDeploymentItem> items =
        [
            new WorkflowProjectDeploymentItem
            {
                WorkflowId = Guid.NewGuid(),
                WorkflowName = "wf-A",
                OrderNo = 10,
                GraphHash = "hash-a",
            },
            new WorkflowProjectDeploymentItem
            {
                WorkflowId = Guid.NewGuid(),
                WorkflowName = "wf-B",
                OrderNo = 20,
                GraphHash = "hash-b",
            },
        ];

        return WorkflowProjectDeployment.CreatePublished(
            Guid.NewGuid(),
            Guid.NewGuid(),
            revision: 1,
            items,
            frozenGraphs: [],
            frozenVariables: [],
            snapshotHash: "snapshot-hash"
        );
    }

    private static WorkflowProjectDeploymentDto MapDeployment(WorkflowProjectDeployment deployment)
    {
        MethodInfo method = typeof(WorkflowRuntimeAppService).GetMethod(
            "MapDeploymentDto",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        return (WorkflowProjectDeploymentDto)method.Invoke(null, [deployment])!;
    }
}
