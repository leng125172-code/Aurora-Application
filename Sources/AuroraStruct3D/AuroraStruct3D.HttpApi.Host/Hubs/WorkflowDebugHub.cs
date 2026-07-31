using AuroraStruct3D.Workflow;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

[Authorize]
[DisableAutoHubMap]
public class WorkflowDebugHub : AbpHub<IWorkflowDebugHubClient>
{
    public Task JoinExecutionAsync(Guid executionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(executionId));

    public Task LeaveExecutionAsync(Guid executionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(executionId));

    internal static string Group(Guid executionId) => $"workflow-debug:{executionId:N}";
}
