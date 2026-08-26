using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Plcs;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

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
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IUnitOfWorkManager uowManager =
            scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using IUnitOfWork uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var configs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowPlcHandshakeConfig, Guid>>();
        var runs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectRun, Guid>>();
        var deployments = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectDeployment, Guid>>();
        var taskConfigs = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowProjectTaskConfig, Guid>>();
        var executer = scope.ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>();
        var accessor = scope.ServiceProvider.GetRequiredService<IPlcWorkflowTagAccessor>();
        var runtime = scope.ServiceProvider.GetRequiredService<WorkflowRuntimeAppService>();

        List<WorkflowPlcHandshakeConfig> all = await configs.GetListAsync(cancellationToken: cancellationToken);
        foreach (WorkflowPlcHandshakeConfig config in all)
        {
            if (_nextAttempt.GetValueOrDefault(config.Id) > DateTime.UtcNow) continue;
            try
            {
                WorkflowProjectTaskConfig? taskConfig = config.TaskConfigId == Guid.Empty
                    ? null
                    : await taskConfigs.FindAsync(config.TaskConfigId, cancellationToken: cancellationToken);
                if (taskConfig is not null
                    && (taskConfig.ProjectId != config.ProjectId || !taskConfig.IsEnabled))
                    taskConfig = null;
                if (taskConfig is null) continue;
                HandshakeTags map = Resolve(config);

                if (!config.IsEnabled)
                {
                    await WritePlatformStateAsync(accessor, config.PlcDeviceId, map, 0,
                        (int)_deviceState.Status, WorkflowPlcHandshakePhase.Idle, false, false, 0,
                        false, 0, WorkflowInspectionDecision.None, WorkflowPlcHandshakeErrorCode.None,
                        cancellationToken);
                    continue;
                }

                HandshakePoint[] inputPoints = [map.CaptureRequest, map.RequestId, map.ResultAck, map.ResultAckId];
                IReadOnlyList<PlcValue> inputValues = await accessor.ReadRawAsync(config.PlcDeviceId,
                    inputPoints.Select(x => new PlcReadRequest(x.Key, x.Address, x.DataType)).ToList(), cancellationToken);
                Dictionary<string, object?> inputs = inputValues.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                bool request = AsBool(inputs.GetValueOrDefault(map.CaptureRequest.Key));
                int requestId = AsInt(inputs.GetValueOrDefault(map.RequestId.Key));
                bool resultAck = AsBool(inputs.GetValueOrDefault(map.ResultAck.Key));
                int resultAckId = AsInt(inputs.GetValueOrDefault(map.ResultAckId.Key));

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
                        (await runs.GetQueryableAsync()).Where(x => x.PlcHandshakeConfigId == config.Id
                            && x.PlcRequestId == requestId
                            && (x.Status == WorkflowProjectRunStatus.Queued
                                || x.Status == WorkflowProjectRunStatus.Running))
                            .OrderByDescending(x => x.CreationTime));
                    if (recoveredRun is not null || !hasActiveRun)
                    {
                        Guid runId;
                        if (recoveredRun is not null) runId = recoveredRun.Id;
                        else
                        {
                            long requestSequence = config.AllocateRequestSequence();
                            WorkflowProjectRunEnqueueResultDto enqueued = await runtime.EnqueueProjectRunAsync(
                                new WorkflowProjectRunEnqueueInput { ProjectId = config.ProjectId,
                                    TaskConfigId = taskConfig.Id, Name = runName,
                                    PlcHandshakeConfigId = config.Id, PlcRequestId = requestId,
                                    PlcRequestSequence = requestSequence,
                                    StartType = WorkflowProjectRunStartType.Immediate });
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _nextAttempt[config.Id] = DateTime.UtcNow.AddSeconds(10);
                config.MarkProtocolFault(ex.Message);
                await configs.UpdateAsync(config, autoSave: true, cancellationToken);
                _logger.LogWarning(ex, "OPC UA handshake {ConfigId} failed; retry suppressed for 10 seconds", config.Id);
            }
        }

        // Background services do not receive ABP's request UoW automatically. Keeping one
        // explicit UoW alive for the polling pass prevents repositories from reusing a
        // DbContext that an implicit repository UoW has already disposed.
        await uow.CompleteAsync(cancellationToken);
    }

    private int GetHeartbeat(Guid id)
    {
        DateTime now = DateTime.UtcNow;
        if (_nextHeartbeat.GetValueOrDefault(id) <= now)
        { _heartbeats[id] = _heartbeats.GetValueOrDefault(id) == int.MaxValue ? 1 : _heartbeats.GetValueOrDefault(id) + 1; _nextHeartbeat[id] = now.AddSeconds(1); }
        return _heartbeats.GetValueOrDefault(id);
    }

    private static async Task WritePlatformStateAsync(IPlcWorkflowTagAccessor accessor, Guid deviceId,
        HandshakeTags t, int heartbeat, int deviceStatus, WorkflowPlcHandshakePhase taskStatus,
        bool canCapture, bool captureAck, int ackId, bool resultValid, int resultRequestId,
        WorkflowInspectionDecision result, WorkflowPlcHandshakeErrorCode error, CancellationToken ct)
    {
        // OPC UA 批量 Write 不保证 PLC 扫描侧的跨节点可见顺序，必须分阶段发布。
        await WriteCheckedAsync(accessor, deviceId,
            [(t.CaptureAck, false), (t.ResultValid, false)], ct);
        await WriteCheckedAsync(accessor, deviceId,
            [(t.Heartbeat, heartbeat), (t.DeviceStatus, deviceStatus),
             (t.TaskStatus, (int)taskStatus), (t.CanCapture, canCapture),
             (t.AckRequestId, ackId), (t.ResultRequestId, resultRequestId),
             (t.ResultCode, (int)result), (t.ErrorCode, (int)error)], ct);
        var publish = new List<(HandshakePoint Point, object? Value)>();
        if (captureAck) publish.Add((t.CaptureAck, true));
        if (resultValid) publish.Add((t.ResultValid, true));
        if (publish.Count > 0)
            await WriteCheckedAsync(accessor, deviceId, publish, ct);
    }

    private static async Task WriteCheckedAsync(IPlcWorkflowTagAccessor accessor, Guid deviceId,
        IReadOnlyList<(HandshakePoint Point, object? Value)> values, CancellationToken ct)
    {
        IReadOnlyList<PlcWriteResult> written = await accessor.WriteRawAsync(deviceId,
            values.Select(x => new PlcWriteRequest(x.Point.Key, x.Point.Address, x.Point.DataType, x.Value)).ToList(), ct);
        PlcWriteResult? failed = written.FirstOrDefault(x => !x.Success);
        if (failed is not null)
        {
            HandshakePoint? point = values
                .Select(x => x.Point)
                .FirstOrDefault(x => string.Equals(x.Key, failed.Key, StringComparison.Ordinal));
            string target = point is null
                ? failed.Key
                : $"{point.Key} ({point.Address})";
            throw new InvalidOperationException(
                $"Handshake output '{target}' is not writable: {failed.Error ?? "unknown OPC UA error"}"
            );
        }
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

    private static HandshakeTags Resolve(WorkflowPlcHandshakeConfig c) => new(
        Bool("CaptureRequest", c.CaptureRequestAddress), Int("RequestId", c.RequestIdAddress),
        Bool("ResultAck", c.ResultAckAddress), Int("ResultAckId", c.ResultAckIdAddress),
        Int("Heartbeat", c.HeartbeatAddress), Int("DeviceStatus", c.DeviceStatusAddress),
        Int("TaskStatus", c.TaskStatusAddress), Bool("CanCapture", c.CanCaptureAddress),
        Bool("CaptureAck", c.CaptureAckAddress), Int("AckRequestId", c.AckRequestIdAddress),
        Bool("ResultValid", c.ResultValidAddress), Int("ResultRequestId", c.ResultRequestIdAddress),
        Int("ResultCode", c.ResultCodeAddress), Int("ErrorCode", c.ErrorCodeAddress));

    private static HandshakePoint Bool(string key, string address) => new(key, address, PlcTagDataType.Boolean);
    private static HandshakePoint Int(string key, string address) => new(key, address, PlcTagDataType.Int32);
    private sealed record HandshakePoint(string Key, string Address, PlcTagDataType DataType);
    private sealed record HandshakeTags(HandshakePoint CaptureRequest, HandshakePoint RequestId, HandshakePoint ResultAck,
        HandshakePoint ResultAckId, HandshakePoint Heartbeat, HandshakePoint DeviceStatus, HandshakePoint TaskStatus,
        HandshakePoint CanCapture, HandshakePoint CaptureAck, HandshakePoint AckRequestId, HandshakePoint ResultValid,
        HandshakePoint ResultRequestId, HandshakePoint ResultCode, HandshakePoint ErrorCode);
}
