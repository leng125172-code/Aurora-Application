using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Plcs;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>持久化 OPC UA 生产任务握手协调器。</summary>
internal sealed class WorkflowPlcHandshakeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceStateManager _deviceState;
    private readonly ILogger<WorkflowPlcHandshakeHostedService> _logger;
    private readonly Dictionary<Guid, int> _heartbeats = [];
    private readonly Dictionary<Guid, DateTime> _nextHeartbeat = [];
    private readonly Dictionary<Guid, DateTime> _nextAttempt = [];

    public WorkflowPlcHandshakeHostedService(IServiceScopeFactory scopeFactory,
        IDeviceStateManager deviceState, ILogger<WorkflowPlcHandshakeHostedService> logger)
    { _scopeFactory = scopeFactory; _deviceState = deviceState; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PollAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "OPC UA workflow handshake polling failed"); }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var configs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowPlcHandshakeConfig, Guid>>();
        var tags = scope.ServiceProvider.GetRequiredService<IRepository<PlcTag, Guid>>();
        var runs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectRun, Guid>>();
        var deployments = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectDeployment, Guid>>();
        var taskConfigs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectTaskConfig, Guid>>();
        var executer = scope.ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>();
        var accessor = scope.ServiceProvider.GetRequiredService<IPlcTagAccessor>();
        var runtime = scope.ServiceProvider.GetRequiredService<WorkflowRuntimeAppService>();

        List<WorkflowPlcHandshakeConfig> all = await configs.GetListAsync(cancellationToken: cancellationToken);
        foreach (WorkflowPlcHandshakeConfig config in all)
        {
            if (_nextAttempt.GetValueOrDefault(config.Id) > DateTime.UtcNow) continue;
            try
            {
                List<PlcTag> mappedTags = await tags.GetListAsync(x => x.PlcDeviceId == config.PlcDeviceId,
                    cancellationToken: cancellationToken);
                Dictionary<Guid, PlcTag> byId = mappedTags.ToDictionary(x => x.Id);
                if (!TryResolve(config, byId, out HandshakeTags map))
                { _logger.LogWarning("Handshake {ConfigId} contains missing PLC tags", config.Id); continue; }

                if (!config.IsEnabled)
                {
                    await WritePlatformStateAsync(accessor, config.PlcDeviceId, map, 0,
                        (int)_deviceState.Status, WorkflowPlcHandshakePhase.Idle, false, false, 0,
                        false, 0, WorkflowInspectionDecision.None, WorkflowPlcHandshakeErrorCode.None,
                        cancellationToken);
                    continue;
                }

                IReadOnlyList<PlcTagValueDto> inputValues = await accessor.ReadByCodesAsync(
                    config.PlcDeviceId,
                    [map.CaptureRequest.Code, map.RequestId.Code, map.ResultAck.Code, map.ResultAckId.Code],
                    cancellationToken);
                Dictionary<string, object?> inputs = inputValues.ToDictionary(x => x.Code,
                    x => x.EngineeringValue, StringComparer.Ordinal);
                bool request = AsBool(inputs.GetValueOrDefault(map.CaptureRequest.Code));
                int requestId = AsInt(inputs.GetValueOrDefault(map.RequestId.Code));
                bool resultAck = AsBool(inputs.GetValueOrDefault(map.ResultAck.Code));
                int resultAckId = AsInt(inputs.GetValueOrDefault(map.ResultAckId.Code));

                WorkflowProjectRun? currentRun = config.CurrentRunId.HasValue
                    ? await runs.FindAsync(config.CurrentRunId.Value, cancellationToken: cancellationToken)
                    : null;
                if (config.Phase == WorkflowPlcHandshakePhase.Accepted
                    && currentRun?.Status == WorkflowProjectRunStatus.Running)
                { config.MarkRunning(); await configs.UpdateAsync(config, autoSave: true, cancellationToken); }

                if (config.Phase is WorkflowPlcHandshakePhase.Accepted or WorkflowPlcHandshakePhase.Running
                    && currentRun is not null
                    && currentRun.Status is WorkflowProjectRunStatus.Succeeded or WorkflowProjectRunStatus.Failed or WorkflowProjectRunStatus.Canceled)
                {
                    (WorkflowInspectionDecision decision, WorkflowPlcHandshakeErrorCode error, string? message) =
                        ResolveRunResult(currentRun);
                    config.SetResult(decision, error, message, DateTime.UtcNow);
                    await configs.UpdateAsync(config, autoSave: true, cancellationToken);
                }

                if (config.Phase == WorkflowPlcHandshakePhase.ResultPending
                    && resultAck && resultAckId == config.CurrentRequestId)
                { config.CompleteAcknowledgement(); await configs.UpdateAsync(config, autoSave: true, cancellationToken); }

                bool hasActiveRun = await executer.AnyAsync((await runs.GetQueryableAsync()).Where(x =>
                    x.ProjectId == config.ProjectId && (x.Status == WorkflowProjectRunStatus.Queued || x.Status == WorkflowProjectRunStatus.Running)));
                bool hasDeployment = await executer.AnyAsync((await deployments.GetQueryableAsync()).Where(x =>
                    x.ProjectId == config.ProjectId && x.Status == WorkflowProjectDeploymentStatus.Activated));
                WorkflowProjectTaskConfig? taskConfig = await executer.FirstOrDefaultAsync(
                    (await taskConfigs.GetQueryableAsync()).Where(x => x.ProjectId == config.ProjectId));
                bool productionState = _deviceState.Status == DeviceStatus.Running
                    && _deviceState.RunMode is DeviceRunMode.Auto or DeviceRunMode.Online;
                bool canCapture = productionState && hasDeployment && !hasActiveRun
                    && taskConfig?.ResultWorkflowId is not null
                    && !string.IsNullOrWhiteSpace(taskConfig.ResultVariableName)
                    && config.Phase == WorkflowPlcHandshakePhase.Idle && !request;

                if (config.Phase == WorkflowPlcHandshakePhase.Idle && request && requestId > 0
                    && requestId != config.LastCompletedRequestId && productionState && hasDeployment
                    && taskConfig?.ResultWorkflowId is not null)
                {
                    string runName = $"PLC request {requestId}";
                    // 恢复“运行已创建、握手状态尚未来得及持久化”这一极窄崩溃窗口。
                    WorkflowProjectRun? recoveredRun = await executer.FirstOrDefaultAsync(
                        (await runs.GetQueryableAsync()).Where(x => x.ProjectId == config.ProjectId
                            && x.Name == runName).OrderByDescending(x => x.CreationTime));
                    if (recoveredRun is not null || !hasActiveRun)
                    {
                        Guid runId;
                        if (recoveredRun is not null) runId = recoveredRun.Id;
                        else
                        {
                            WorkflowProjectRunEnqueueResultDto enqueued = await runtime.EnqueueProjectRunAsync(
                                new WorkflowProjectRunEnqueueInput { ProjectId = config.ProjectId,
                                    Name = runName, StartType = WorkflowProjectRunStartType.Immediate });
                            runId = enqueued.RunId;
                        }
                        config.Accept(requestId, runId, DateTime.UtcNow);
                        await configs.UpdateAsync(config, autoSave: true, cancellationToken);
                    }
                    canCapture = false;
                }

                int heartbeat = GetHeartbeat(config.Id);
                bool captureAck = request && (config.CurrentRequestId == requestId
                    || config.Phase == WorkflowPlcHandshakePhase.Idle && config.LastCompletedRequestId == requestId);
                bool resultValid = config.Phase == WorkflowPlcHandshakePhase.ResultPending;
                await WritePlatformStateAsync(accessor, config.PlcDeviceId, map, heartbeat,
                    (int)_deviceState.Status, config.Phase, canCapture, captureAck,
                    captureAck ? requestId : 0, resultValid,
                    resultValid ? config.CurrentRequestId : 0,
                    resultValid ? config.ResultCode : WorkflowInspectionDecision.None,
                    resultValid ? config.ErrorCode : WorkflowPlcHandshakeErrorCode.None,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _nextAttempt[config.Id] = DateTime.UtcNow.AddSeconds(10);
                _logger.LogWarning(ex, "OPC UA handshake {ConfigId} failed; retry suppressed for 10 seconds", config.Id);
            }
        }
    }

    private int GetHeartbeat(Guid id)
    {
        DateTime now = DateTime.UtcNow;
        if (_nextHeartbeat.GetValueOrDefault(id) <= now)
        { _heartbeats[id] = _heartbeats.GetValueOrDefault(id) == int.MaxValue ? 1 : _heartbeats.GetValueOrDefault(id) + 1; _nextHeartbeat[id] = now.AddSeconds(1); }
        return _heartbeats.GetValueOrDefault(id);
    }

    private static async Task WritePlatformStateAsync(IPlcTagAccessor accessor, Guid deviceId,
        HandshakeTags t, int heartbeat, int deviceStatus, WorkflowPlcHandshakePhase taskStatus,
        bool canCapture, bool captureAck, int ackId, bool resultValid, int resultRequestId,
        WorkflowInspectionDecision result, WorkflowPlcHandshakeErrorCode error, CancellationToken ct)
    {
        var values = new Dictionary<string, object?>();
        // 清除时先撤销有效位，防止 PLC 在同一扫描周期读取到已失效数据。
        if (!captureAck) values[t.CaptureAck.Code] = false;
        if (!resultValid) values[t.ResultValid.Code] = false;
        values[t.Heartbeat.Code] = heartbeat;
        values[t.DeviceStatus.Code] = deviceStatus;
        values[t.TaskStatus.Code] = (int)taskStatus;
        values[t.CanCapture.Code] = canCapture;
        values[t.AckRequestId.Code] = ackId;
        values[t.ResultRequestId.Code] = resultRequestId;
        values[t.ResultCode.Code] = (int)result;
        values[t.ErrorCode.Code] = (int)error;
        // 发布时先写数据与序号，最后写确认/有效位。
        if (captureAck) values[t.CaptureAck.Code] = true;
        if (resultValid) values[t.ResultValid.Code] = true;
        IReadOnlyList<PlcWriteResultDto> written = await accessor.WriteByCodesAsync(deviceId, values, ct);
        PlcWriteResultDto? failed = written.FirstOrDefault(x => !x.Success);
        if (failed is not null) throw new InvalidOperationException(failed.Error ?? "OPC UA handshake write failed.");
    }

    private static (WorkflowInspectionDecision, WorkflowPlcHandshakeErrorCode, string?) ResolveRunResult(WorkflowProjectRun run)
    {
        if (run.Status == WorkflowProjectRunStatus.Canceled)
            return (WorkflowInspectionDecision.Canceled, WorkflowPlcHandshakeErrorCode.Canceled, "任务已取消。");
        if (run.Status == WorkflowProjectRunStatus.Failed)
            return (WorkflowInspectionDecision.Error, WorkflowPlcHandshakeErrorCode.WorkflowFailed, run.ErrorMessage);
        if (run.InspectionDecision == WorkflowInspectionDecision.None)
            return (WorkflowInspectionDecision.Error, WorkflowPlcHandshakeErrorCode.ResultVariableMissing, "任务没有最终判定结果。");
        return (run.InspectionDecision, run.InspectionErrorCode, run.InspectionErrorMessage);
    }

    private static bool AsBool(object? value) => value switch
    { bool b => b, byte b => b != 0, short s => s != 0, int i => i != 0, long l => l != 0, string s when bool.TryParse(s, out bool b) => b, _ => false };
    private static int AsInt(object? value) => value is IConvertible c ? Convert.ToInt32(c) : 0;

    private static bool TryResolve(WorkflowPlcHandshakeConfig c, Dictionary<Guid, PlcTag> tags, out HandshakeTags result)
    {
        Guid[] ids = [c.CaptureRequestTagId, c.RequestIdTagId, c.ResultAckTagId, c.ResultAckIdTagId,
            c.HeartbeatTagId, c.DeviceStatusTagId, c.TaskStatusTagId, c.CanCaptureTagId,
            c.CaptureAckTagId, c.AckRequestIdTagId, c.ResultValidTagId, c.ResultRequestIdTagId,
            c.ResultCodeTagId, c.ErrorCodeTagId];
        if (ids.Any(id => !tags.ContainsKey(id))) { result = null!; return false; }
        result = new HandshakeTags(tags[ids[0]], tags[ids[1]], tags[ids[2]], tags[ids[3]],
            tags[ids[4]], tags[ids[5]], tags[ids[6]], tags[ids[7]], tags[ids[8]], tags[ids[9]],
            tags[ids[10]], tags[ids[11]], tags[ids[12]], tags[ids[13]]);
        return true;
    }

    private sealed record HandshakeTags(PlcTag CaptureRequest, PlcTag RequestId, PlcTag ResultAck,
        PlcTag ResultAckId, PlcTag Heartbeat, PlcTag DeviceStatus, PlcTag TaskStatus,
        PlcTag CanCapture, PlcTag CaptureAck, PlcTag AckRequestId, PlcTag ResultValid,
        PlcTag ResultRequestId, PlcTag ResultCode, PlcTag ErrorCode);
}
