using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Plcs;

public interface IPlcDeviceAppService : IApplicationService
{
    Task<ListResultDto<PlcDriverDescriptor>> GetDriversAsync();
    Task<ListResultDto<PlcDeviceDto>> GetListAsync();
    Task<PlcDeviceDto> GetAsync(Guid id);
    Task<PlcDeviceDto> CreateAsync(SavePlcDeviceDto input);
    Task<PlcDeviceDto> UpdateAsync(Guid id, SavePlcDeviceDto input);
    Task DeleteAsync(Guid id);
    Task<PlcConnectionTestResultDto> TestConnectionAsync(Guid id);
    Task ConnectAsync(Guid id);
    Task DisconnectAsync(Guid id);
    Task ConfirmServerCertificateAsync(Guid id, ConfirmPlcCertificateInput input);
    Task<PlcBrowseResult> BrowseAsync(Guid id, PlcBrowseInput input);
    Task<PlcBrowseTreeResultDto> BrowseTreeAsync(Guid id, PlcBrowseTreeInput input);
    Task<ListResultDto<PlcTagDto>> GetTagsAsync(Guid id);
    Task<PlcTagDto> CreateTagAsync(Guid id, SavePlcTagDto input);
    Task<PlcTagDto> UpdateTagAsync(Guid id, Guid tagId, SavePlcTagDto input);
    Task DeleteTagAsync(Guid id, Guid tagId);
    Task<ListResultDto<PlcTagValueDto>> ReadAsync(Guid id, PlcReadInput input);
    Task<ListResultDto<PlcWriteResultDto>> WriteAsync(Guid id, PlcWriteInput input);
    Task<PlcSubscriptionResultDto> SubscribeAsync(Guid id, PlcSubscribeInput input);
    Task UnsubscribeAsync(Guid id, string subscriptionId);
}

public interface IPlcTagAccessor
{
    Task<IReadOnlyList<PlcTagValueDto>> ReadByCodesAsync(
        Guid plcDeviceId,
        IReadOnlyList<string> tagCodes,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<PlcWriteResultDto>> WriteByCodesAsync(
        Guid plcDeviceId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Restricted PLC access surface for workflow infrastructure operators.</summary>
public interface IPlcWorkflowTagAccessor
{
    Task<IReadOnlyList<PlcTagValueDto>> ReadByCodesAsync(Guid plcDeviceId, IReadOnlyList<string> tagCodes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlcWriteResultDto>> WriteByCodesAsync(Guid plcDeviceId, IReadOnlyDictionary<string, object?> values, WorkflowPlcOperationContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlcValue>> ReadRawAsync(Guid plcDeviceId, IReadOnlyList<PlcReadRequest> requests, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlcWriteResult>> WriteRawAsync(Guid plcDeviceId, IReadOnlyList<PlcWriteRequest> requests, CancellationToken cancellationToken = default);
}

public sealed record WorkflowPlcOperationContext(Guid? ProjectRunId, Guid ExecutionId, string? NodeId);

public interface IPlcRealtimeNotifier
{
    Task ValueChangedAsync(string connectionId, PlcTagValueDto value);
    Task ConnectionStateChangedAsync(Guid deviceId, PlcConnectionStatus status, string? error);
}

public sealed class NullPlcRealtimeNotifier : IPlcRealtimeNotifier
{
    public Task ValueChangedAsync(string connectionId, PlcTagValueDto value) => Task.CompletedTask;
    public Task ConnectionStateChangedAsync(
        Guid deviceId,
        PlcConnectionStatus status,
        string? error
    ) => Task.CompletedTask;
}

public sealed record PlcTrackedSubscription(Guid DeviceId, string SubscriptionId);

public interface IPlcSubscriptionTracker
{
    void Add(string connectionId, Guid deviceId, string subscriptionId);
    void Remove(string subscriptionId);
    IReadOnlyList<PlcTrackedSubscription> TakeByConnection(string connectionId);
}
