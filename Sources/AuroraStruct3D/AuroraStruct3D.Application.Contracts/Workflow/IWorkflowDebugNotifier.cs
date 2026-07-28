using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Workflow;

public interface IWorkflowDebugHubClient
{
    Task DebugStateChangedAsync(Guid executionId, string eventType, DateTime updatedAt);
}

public interface IWorkflowDebugNotifier
{
    Task NotifyAsync(Guid executionId, string eventType, WorkflowExecutionStatusDto status);
}

[Dependency(TryRegister = true)]
[ExposeServices(typeof(IWorkflowDebugNotifier))]
public sealed class NullWorkflowDebugNotifier : IWorkflowDebugNotifier, ISingletonDependency
{
    public Task NotifyAsync(Guid executionId, string eventType, WorkflowExecutionStatusDto status) =>
        Task.CompletedTask;
}
