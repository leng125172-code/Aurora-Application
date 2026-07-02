using Xunit;

namespace AuroraStruct3D.Application.Tests.Variables;

public class VariableModuleIntegrationStubsTests
{
    [Fact(Skip = "Integration stub: requires seeded runtime instance and database fixture.")]
    public void WorkflowExecution_Should_Read_Write_With_CrossWorkflow_Bindings() { }

    [Fact(Skip = "Integration stub: requires distributed lock provider and multi-writer fixture.")]
    public void OnlinePool_Should_Detect_Concurrent_Write_Conflicts() { }

    [Fact(
        Skip = "Integration stub: requires compile endpoint wiring and full declaration/import graph."
    )]
    public void OfflineCompile_Should_Respect_Suppression_And_DefUse_Mode() { }
}
