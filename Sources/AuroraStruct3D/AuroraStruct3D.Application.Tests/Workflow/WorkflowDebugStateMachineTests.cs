using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowDebugStateMachineTests
{
    [Fact]
    public void Breakpoint_then_repeated_steps_should_advance_exactly_one_node()
    {
        CountingStatement first = new();
        CountingStatement second = new();
        using WorkflowExecutionSession session = CreateSession(first, second);
        session.Breakpoints["first"] = new WorkflowBreakpointState
        {
            Definition = new WorkflowBreakpointDto { NodeId = "first" },
        };

        WorkflowExecutionKernel kernel = new();
        kernel.Continue(session, markCompleted: false);

        Assert.Equal(0, session.StepCursor);
        Assert.Equal("first", session.CurrentNodeId);
        Assert.Equal(1, session.Breakpoints["first"].CurrentHitCount);

        kernel.ExecuteSteps(session, 1, markCompleted: false);

        Assert.Equal(1, session.StepCursor);
        Assert.Equal(1, first.ExecutionCount);
        Assert.Equal(0, second.ExecutionCount);
        Assert.Null(session.LastPausedNodeId);

        kernel.ExecuteSteps(session, 1, markCompleted: false);

        Assert.Equal(2, session.StepCursor);
        Assert.Equal(1, first.ExecutionCount);
        Assert.Equal(1, second.ExecutionCount);
        Assert.Equal(WorkflowExecutionStatus.Running, session.Status);
    }

    [Fact]
    public void Continue_after_breakpoint_should_not_count_the_same_hit_twice()
    {
        CountingStatement first = new();
        using WorkflowExecutionSession session = CreateSession(first);
        session.Breakpoints["first"] = new WorkflowBreakpointState
        {
            Definition = new WorkflowBreakpointDto { NodeId = "first" },
        };

        WorkflowExecutionKernel kernel = new();
        kernel.Continue(session, markCompleted: false);
        kernel.Continue(session, markCompleted: false);

        Assert.Equal(1, session.Breakpoints["first"].CurrentHitCount);
        Assert.Equal(1, first.ExecutionCount);
        Assert.Equal(1, session.StepCursor);
    }

    [Fact]
    public void Pause_request_should_stop_current_continue_without_blocking_next_step()
    {
        CountingStatement first = new();
        using WorkflowExecutionSession session = CreateSession(first);
        WorkflowExecutionKernel kernel = new();

        session.BeginExecutionCommand();
        Assert.True(session.RequestPause());
        kernel.Continue(session, markCompleted: false);
        session.EndExecutionCommand();

        Assert.Equal(0, session.StepCursor);
        Assert.Equal(0, first.ExecutionCount);

        session.BeginExecutionCommand();
        kernel.ExecuteSteps(session, 1, markCompleted: false);
        session.EndExecutionCommand();

        Assert.Equal(1, session.StepCursor);
        Assert.Equal(1, first.ExecutionCount);
    }

    [Fact]
    public void Stop_request_should_end_continue_as_stopped_instead_of_faulted()
    {
        CountingStatement first = new();
        using WorkflowExecutionSession session = CreateSession(first);
        WorkflowExecutionKernel kernel = new();
        session.StopRequested = true;

        kernel.Continue(session, markCompleted: false);

        Assert.Equal(WorkflowExecutionStatus.Stopped, session.Status);
        Assert.Equal(0, session.StepCursor);
        Assert.Equal(0, first.ExecutionCount);
    }

    private static WorkflowExecutionSession CreateSession(
        params IWorkflowStatement[] statements
    )
    {
        WorkflowContext context = new();
        WorkflowExecutionVariablePool variablePool = new([]);
        variablePool.Initialize(context);
        return new WorkflowExecutionSession
        {
            ExecutionId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            WorkflowName = "debug-state-machine",
            Mode = WorkflowExecutionMode.DebugStep,
            RuntimeWorkflow = new WorkflowDefinition("debug-state-machine", statements),
            Context = context,
            VariablePool = variablePool,
            StatementNodeIds = statements
                .Select((_, index) => index == 0 ? "first" : $"node-{index}")
                .ToList(),
        };
    }

    private sealed class CountingStatement : IWorkflowStatement
    {
        public int ExecutionCount { get; private set; }

        public void Execute(IWorkflowContext context)
        {
            ExecutionCount++;
        }
    }
}
