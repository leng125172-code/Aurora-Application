using AuroraStruct3D.Hubs;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

[ExposeServices(typeof(IWorkflowDebugNotifier))]
public sealed class SignalRWorkflowDebugNotifier : IWorkflowDebugNotifier, ISingletonDependency
{
    private readonly IHubContext<WorkflowDebugHub, IWorkflowDebugHubClient> _hub;

    public SignalRWorkflowDebugNotifier(
        IHubContext<WorkflowDebugHub, IWorkflowDebugHubClient> hub
    ) => _hub = hub;

    public Task NotifyAsync(Guid executionId, string eventType, WorkflowExecutionStatusDto status) =>
        _hub.Clients.Group(WorkflowDebugHub.Group(executionId))
            .DebugStateChangedAsync(executionId, eventType, DateTime.UtcNow);
}
