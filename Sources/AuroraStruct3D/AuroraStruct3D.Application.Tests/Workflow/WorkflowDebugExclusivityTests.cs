using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Volo.Abp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

/// <summary>
/// 项目级调试互斥单元测试：单机一次只能调试一个项目（固定调试任务）。
/// </summary>
public class WorkflowDebugExclusivityTests
{
    private static WorkflowExecutionSession DebugSession(Guid projectId) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            ProjectId = projectId,
            Mode = WorkflowExecutionMode.DebugStep,
        };

    private static WorkflowExecutionSession RunOnceSession(Guid projectId) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            ProjectId = projectId,
            Mode = WorkflowExecutionMode.RunOnce,
        };

    [Fact]
    public void Debug_Second_Project_Should_Be_Rejected()
    {
        WorkflowDebugSessionStore store = new();
        Guid projectA = Guid.NewGuid();
        Guid projectB = Guid.NewGuid();

        store.Add(DebugSession(projectA));

        UserFriendlyException ex = Assert.Throws<UserFriendlyException>(() =>
            store.Add(DebugSession(projectB))
        );
        Assert.Contains("一次只能调试一个项目", ex.Message);
    }

    [Fact]
    public void Debug_Same_Project_Should_Be_Allowed()
    {
        WorkflowDebugSessionStore store = new();
        Guid projectA = Guid.NewGuid();

        store.Add(DebugSession(projectA));
        store.Add(DebugSession(projectA));

        Assert.Equal(projectA, store.GetActiveDebugProjectId());
    }

    [Fact]
    public void GetActiveDebugProjectId_Should_Be_Null_When_No_Debug_Session()
    {
        WorkflowDebugSessionStore store = new();

        Assert.Null(store.GetActiveDebugProjectId());
    }

    [Fact]
    public void RunOnce_Session_Should_Not_Block_Other_Project_Debug()
    {
        WorkflowDebugSessionStore store = new();
        Guid projectA = Guid.NewGuid();
        Guid projectB = Guid.NewGuid();

        // 非调试会话（RunOnce）不参与调试互斥。
        store.Add(RunOnceSession(projectA));

        Exception? ex = Record.Exception(() => store.Add(DebugSession(projectB)));
        Assert.Null(ex);
        Assert.Equal(projectB, store.GetActiveDebugProjectId());
    }

    [Fact]
    public void EnsureExclusiveDebugProject_Should_Throw_For_Other_Active_Project()
    {
        WorkflowDebugSessionStore store = new();
        Guid projectA = Guid.NewGuid();
        Guid projectB = Guid.NewGuid();

        store.Add(DebugSession(projectA));

        Assert.Throws<UserFriendlyException>(() => store.EnsureExclusiveDebugProject(projectB));
        // 同项目不抛。
        store.EnsureExclusiveDebugProject(projectA);
    }
}
