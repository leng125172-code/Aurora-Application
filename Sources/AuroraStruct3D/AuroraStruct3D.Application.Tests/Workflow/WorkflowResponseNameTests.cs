using System.Reflection;
using AuroraStruct3D.Workflow;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowResponseNameTests
{
    [Fact]
    public void Execution_Response_Name_Should_Prefer_Persisted_Name()
    {
        MethodInfo method = typeof(WorkflowExecutionAppService).GetMethod(
            "ResolveResponseWorkflowName",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        string result = (string)method.Invoke(null, ["PersistedName", "workflow"])!;

        Assert.Equal("PersistedName", result);
    }

    [Fact]
    public void Execution_Response_Name_Should_Fallback_When_Persisted_Is_Empty()
    {
        MethodInfo method = typeof(WorkflowExecutionAppService).GetMethod(
            "ResolveResponseWorkflowName",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        string result = (string)method.Invoke(null, ["", "workflow"])!;

        Assert.Equal("workflow", result);
    }

    [Fact]
    public void Simulate_Response_Name_Should_Prefer_Entity_Name()
    {
        MethodInfo method = typeof(WorkflowAppService).GetMethod(
            "ResolveResponseWorkflowName",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "EntityName",
            "{\"nodes\":[],\"edges\":[]}"
        );

        string result = (string)method.Invoke(null, [workflow, "workflow"])!;

        Assert.Equal("EntityName", result);
    }
}
