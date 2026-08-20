using System.Reflection;
using System.Text.Json;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Runtime;
using Volo.Abp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

/// <summary>
/// 工作流四层重构（任务配置 / 运行 / 冻结部署 / 周期任务）新行为单元测试。
/// </summary>
public class WorkflowProjectRunRefactorTests
{
    [Fact]
    public void Task_Create_Cyclic_Without_Interval_Should_Throw()
    {
        Assert.Throws<BusinessException>(() =>
            WorkflowProjectTaskConfig.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                WorkflowProjectTaskType.Cyclic,
                cycleIntervalSeconds: null
            )
        );
    }

    [Fact]
    public void TaskConfig_Create_Immediate_Should_Null_CycleInterval()
    {
        WorkflowProjectTaskConfig taskConfig = WorkflowProjectTaskConfig.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowProjectTaskType.Immediate,
            cycleIntervalSeconds: 30
        );

        Assert.Equal(WorkflowProjectTaskType.Immediate, taskConfig.TaskType);
        Assert.Null(taskConfig.CycleIntervalSeconds);
    }

    [Fact]
    public void TaskConfig_Create_Cyclic_Should_Keep_Interval()
    {
        WorkflowProjectTaskConfig taskConfig = WorkflowProjectTaskConfig.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowProjectTaskType.Cyclic,
            cycleIntervalSeconds: 45
        );

        Assert.Equal(WorkflowProjectTaskType.Cyclic, taskConfig.TaskType);
        Assert.Equal(45, taskConfig.CycleIntervalSeconds);
    }

    [Fact]
    public void Task_Create_Should_Not_Require_Trigger_Fields()
    {
        WorkflowProjectTask task = WorkflowProjectTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            isEnabled: true,
            orderNo: 5
        );

        Assert.True(task.IsEnabled);
        Assert.Equal(5, task.OrderNo);
    }

    [Fact]
    public void Run_Create_Should_Store_StartType_Interval_And_Deployment()
    {
        Guid deploymentId = Guid.NewGuid();
        WorkflowProjectRun run = WorkflowProjectRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "cyclic-run",
            WorkflowProjectRunStartType.Cyclic,
            cycleIntervalSeconds: 15,
            deploymentId,
            deploymentRevision: 3,
            [Guid.NewGuid(), Guid.NewGuid()],
            continueOnError: true
        );

        Assert.Equal(WorkflowProjectRunStartType.Cyclic, run.StartType);
        Assert.Equal(15, run.CycleIntervalSeconds);
        Assert.Equal(deploymentId, run.DeploymentId);
        Assert.Equal(3, run.DeploymentRevision);
        Assert.Equal(WorkflowProjectRunStatus.Queued, run.Status);
    }

    [Fact]
    public void Deployment_CreatePublished_Should_Persist_Frozen_Graphs_And_Variables()
    {
        Guid workflowId = Guid.NewGuid();
        List<WorkflowProjectDeploymentItem> items =
        [
            new WorkflowProjectDeploymentItem
            {
                WorkflowId = workflowId,
                WorkflowName = "wf",
                OrderNo = 0,
                GraphHash = "hash",
            },
        ];
        List<WorkflowProjectFrozenGraph> frozenGraphs =
        [
            new WorkflowProjectFrozenGraph
            {
                WorkflowId = workflowId,
                GraphData = "{\"nodes\":[],\"edges\":[]}",
            },
        ];
        List<WorkflowProjectFrozenVariable> frozenVariables =
        [
            new WorkflowProjectFrozenVariable
            {
                OwnerWorkflowId = workflowId,
                Name = "v1",
                TypeName = "System.Int32",
                DefaultValueJson = null,
            },
        ];

        WorkflowProjectDeployment deployment = WorkflowProjectDeployment.CreatePublished(
            Guid.NewGuid(),
            Guid.NewGuid(),
            revision: 1,
            items,
            frozenGraphs,
            frozenVariables,
            new WorkflowProjectFrozenTaskConfig(),
            snapshotHash: "snap"
        );

        List<WorkflowProjectFrozenGraph> graphs = JsonSerializer.Deserialize<
            List<WorkflowProjectFrozenGraph>
        >(deployment.FrozenGraphsJson)!;
        List<WorkflowProjectFrozenVariable> variables = JsonSerializer.Deserialize<
            List<WorkflowProjectFrozenVariable>
        >(deployment.FrozenVariablesJson)!;

        Assert.Single(graphs);
        Assert.Equal(workflowId, graphs[0].WorkflowId);
        Assert.Equal("{\"nodes\":[],\"edges\":[]}", graphs[0].GraphData);
        Assert.Single(variables);
        Assert.Equal("v1", variables[0].Name);
    }

    [Theory]
    [InlineData(5, "*/5 * * * * *")]
    [InlineData(30, "*/30 * * * * *")]
    [InlineData(60, "*/1 * * * *")]
    [InlineData(120, "*/2 * * * *")]
    public void BuildCronExpression_Should_Map_Interval_To_Cron(
        int intervalSeconds,
        string expected
    )
    {
        MethodInfo method = typeof(WorkflowRuntimeAppService).GetMethod(
            "BuildCronExpression",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        string cron = (string)method.Invoke(null, [intervalSeconds])!;

        Assert.Equal(expected, cron);
    }
}
