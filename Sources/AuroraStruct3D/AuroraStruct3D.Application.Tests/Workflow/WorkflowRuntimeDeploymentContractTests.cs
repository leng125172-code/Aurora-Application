using System.Reflection;
using AuroraStruct3D.OperatorFile;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.AspNetCore.Mvc;
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
        PropertyInfo triggerErrorCodeProperty =
            typeof(WorkflowExecutionTriggerResultDto).GetProperty(
                nameof(WorkflowExecutionTriggerResultDto.ErrorCode)
            )!;
        PropertyInfo triggerResultImageUrlsProperty =
            typeof(WorkflowExecutionTriggerResultDto).GetProperty(
                nameof(WorkflowExecutionTriggerResultDto.ResultImageUrls)
            )!;
        PropertyInfo triggerMessageProperty = typeof(WorkflowExecutionTriggerResultDto).GetProperty(
            nameof(WorkflowExecutionTriggerResultDto.Message)
        )!;
        PropertyInfo stepErrorProperty = typeof(WorkflowExecutionStepResultDto).GetProperty(
            nameof(WorkflowExecutionStepResultDto.Error)
        )!;
        PropertyInfo stepErrorCodeProperty = typeof(WorkflowExecutionStepResultDto).GetProperty(
            nameof(WorkflowExecutionStepResultDto.ErrorCode)
        )!;
        PropertyInfo stepMessageProperty = typeof(WorkflowExecutionStepResultDto).GetProperty(
            nameof(WorkflowExecutionStepResultDto.Message)
        )!;
        PropertyInfo statusErrorMessageProperty = typeof(WorkflowExecutionStatusDto).GetProperty(
            nameof(WorkflowExecutionStatusDto.ErrorMessage)
        )!;
        PropertyInfo statusErrorCodeProperty = typeof(WorkflowExecutionStatusDto).GetProperty(
            nameof(WorkflowExecutionStatusDto.ErrorCode)
        )!;
        PropertyInfo runErrorCodeProperty = typeof(WorkflowProjectRunStatusDto).GetProperty(
            nameof(WorkflowProjectRunStatusDto.ErrorCode)
        )!;
        PropertyInfo runItemErrorCodeProperty = typeof(WorkflowProjectRunItemDto).GetProperty(
            nameof(WorkflowProjectRunItemDto.ErrorCode)
        )!;

        Assert.Equal(typeof(bool), triggerErrorProperty.PropertyType);
        Assert.Equal(typeof(string), triggerErrorCodeProperty.PropertyType);
        Assert.Equal(typeof(List<string>), triggerResultImageUrlsProperty.PropertyType);
        Assert.Equal(typeof(string), triggerMessageProperty.PropertyType);
        Assert.Equal(typeof(bool), stepErrorProperty.PropertyType);
        Assert.Equal(typeof(string), stepErrorCodeProperty.PropertyType);
        Assert.Equal(typeof(string), stepMessageProperty.PropertyType);
        Assert.Equal(typeof(string), statusErrorMessageProperty.PropertyType);
        Assert.Equal(typeof(string), statusErrorCodeProperty.PropertyType);
        Assert.Equal(typeof(string), runErrorCodeProperty.PropertyType);
        Assert.Equal(typeof(string), runItemErrorCodeProperty.PropertyType);
    }

    [Fact]
    public void Debug_Run_Should_Return_Minimal_Trigger_And_Result_Query_Should_Return_Only_Outputs()
    {
        string[] triggerProperties = typeof(WorkflowDebugRunTriggerResultDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "Error", "ErrorCode", "ExecutionId", "Message" },
            triggerProperties
        );

        MethodInfo interfaceTrigger = typeof(IWorkflowRuntimeAppService).GetMethod(
            nameof(IWorkflowRuntimeAppService.DebugAndRunSourceAsync)
        )!;
        MethodInfo interfaceResult = typeof(IWorkflowRuntimeAppService).GetMethod(
            nameof(IWorkflowRuntimeAppService.GetDebugResultAsync)
        )!;
        Assert.Equal(
            typeof(Task<WorkflowDebugRunTriggerResultDto>),
            interfaceTrigger.ReturnType
        );
        Assert.Equal(
            typeof(Task<List<WorkflowExecutionOutputResultDto>>),
            interfaceResult.ReturnType
        );

        string[] outputProperties = typeof(WorkflowExecutionOutputResultDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "DisplayName", "Name", "Value", "ValueType" },
            outputProperties
        );

        MethodInfo resultMethod = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.GetDebugResultAsync)
        )!;
        HttpGetAttribute route = Assert.IsType<HttpGetAttribute>(
            resultMethod.GetCustomAttribute<HttpGetAttribute>()
        );
        Assert.Equal("executions/{executionId:guid}/result", route.Template);
    }

    [Fact]
    public void Operator_File_Download_Should_Use_Stable_Absolute_Get_Route()
    {
        MethodInfo downloadMethod = typeof(OperatorFileAppService).GetMethod(
            nameof(OperatorFileAppService.DownloadAsync)
        )!;

        // 相对 HttpGet("download") 会被注册成根路径 /download；而完全依赖 ABP
        // 约定时，DownloadAsync 又不满足 Get* 命名规则。使用绝对模板固定公开 GET 地址。
        HttpGetAttribute route = Assert.Single(
            downloadMethod.GetCustomAttributes<HttpGetAttribute>()
        );
        Assert.Equal("/api/app/operator-file/download", route.Template);
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
            frozenTaskConfig: new WorkflowProjectFrozenTaskConfig(),
            snapshotHash: "snapshot-hash"
        );
    }

    [Fact]
    public void Apply_To_Device_Contracts_Should_Expose_Stable_Routes_And_Result()
    {
        MethodInfo statusMethod = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.GetProjectApplicationStatusAsync))!;
        MethodInfo applyMethod = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.ApplyProjectToDeviceAsync))!;

        Assert.Equal("projects/{projectId:guid}/application-status",
            statusMethod.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal("projects/{projectId:guid}/apply",
            applyMethod.GetCustomAttribute<HttpPostAttribute>()?.Template);

        var input = new ApplyWorkflowProjectInput();
        var result = new ApplyWorkflowProjectResultDto();
        var status = new WorkflowProjectApplicationStatusDto();
        Assert.NotNull(input.TaskConfig);
        Assert.NotNull(result.Deployment);
        Assert.Empty(status.ValidationMessages);
    }

    [Fact]
    public void Project_Task_Registration_Should_Only_Require_Project_And_Enabled_State()
    {
        string[] properties = typeof(CreateWorkflowProjectTaskInput).GetProperties()
            .Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "IsEnabled", "Name", "PlcHandshake", "ProjectId" }, properties);

        MethodInfo createMethod = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.CreateProjectTaskAsync))!;
        MethodInfo enabledMethod = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.SetProjectTaskEnabledAsync))!;
        Assert.Equal("project-tasks", createMethod.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("project-tasks/{taskId:guid}/enabled",
            enabledMethod.GetCustomAttribute<HttpPutAttribute>()?.Template);
        MethodInfo updateMethod = typeof(WorkflowRuntimeAppService).GetMethod("UpdateProjectTaskAsync")!;
        Assert.Equal("project-tasks/{taskId:guid}",
            updateMethod.GetCustomAttribute<HttpPutAttribute>()?.Template);
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
