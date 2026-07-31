namespace AuroraStruct3D.Plcs;

public sealed record PlcConnectionOptions(
    Guid DeviceId,
    string EndpointUrl,
    PlcAuthenticationType AuthenticationType,
    string? UserName,
    string? Password,
    string? ClientCertificatePath,
    string? ClientCertificatePassword,
    string SecurityPolicy,
    PlcMessageSecurityMode MessageSecurityMode,
    bool AutoTrustServerCertificate,
    int ConnectTimeoutMs,
    int OperationTimeoutMs,
    int SessionTimeoutMs,
    int KeepAliveMs,
    int ReconnectInitialMs,
    int ReconnectMaxMs,
    int IdleTimeoutMs,
    string? ExtensionJson
);

public sealed record PlcDriverDescriptor(
    string DriverId,
    PlcProtocolType Protocol,
    string DisplayName,
    PlcCapability Capabilities,
    bool IsInstalled
);

public sealed record PlcReadRequest(string Key, string Address, PlcTagDataType DataType);
public sealed record PlcWriteRequest(
    string Key,
    string Address,
    PlcTagDataType DataType,
    object? Value
);

public sealed record PlcValue(
    string Key,
    object? Value,
    PlcTagDataType DataType,
    string Quality,
    DateTime? SourceTimestamp,
    DateTime? ServerTimestamp,
    DateTime ReceivedAt,
    string? Error = null
);

public sealed record PlcWriteResult(string Key, bool Success, string? Error);

public sealed record PlcBrowseRequest(
    string? ParentAddress,
    string? ContinuationToken,
    int MaxResults = 200
);

public sealed record PlcBrowseNode(
    string Address,
    string BrowseName,
    string DisplayName,
    string NodeClass,
    PlcTagDataType? DataType,
    PlcTagAccess Access,
    bool HasChildren
);

public sealed record PlcBrowseResult(
    IReadOnlyList<PlcBrowseNode> Items,
    string? ContinuationToken
);

public sealed record PlcSubscriptionItem(
    string Key,
    string Address,
    PlcTagDataType DataType,
    int SamplingIntervalMs,
    double? Deadband
);

public interface IPlcConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    string? ServerCertificateThumbprint { get; }
    string? ServerCertificateSubject { get; }
    DateTime? ServerCertificateNotBefore { get; }
    DateTime? ServerCertificateNotAfter { get; }
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

public interface IPlcDriver
{
    PlcDriverDescriptor Descriptor { get; }
    Task<IPlcConnection> ConnectAsync(
        PlcConnectionOptions options,
        CancellationToken cancellationToken = default
    );
    Task ValidateAddressAsync(
        string address,
        PlcTagDataType dataType,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<PlcValue>> ReadAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcReadRequest> requests,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<PlcWriteResult>> WriteAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcWriteRequest> requests,
        CancellationToken cancellationToken = default
    );
}

public interface IPlcBrowsableDriver
{
    Task<PlcBrowseResult> BrowseAsync(
        IPlcConnection connection,
        PlcBrowseRequest request,
        CancellationToken cancellationToken = default
    );
}

public interface IPlcSubscriptionDriver
{
    Task<string> SubscribeAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcSubscriptionItem> items,
        Func<PlcValue, Task> onValue,
        CancellationToken cancellationToken = default
    );
    Task UnsubscribeAsync(
        IPlcConnection connection,
        string subscriptionId,
        CancellationToken cancellationToken = default
    );
}

public interface IPlcDriverRegistry
{
    IReadOnlyList<PlcDriverDescriptor> Drivers { get; }
    bool TryGet(string driverId, out IPlcDriver? driver);
    IPlcDriver GetRequired(string driverId);
}

public interface IPlcConnectionManager
{
    Task<IPlcConnection> GetOrConnectAsync(
        PlcConnectionOptions options,
        string driverId,
        CancellationToken cancellationToken = default
    );
    Task DisconnectAsync(Guid deviceId, CancellationToken cancellationToken = default);
    PlcConnectionStatus GetStatus(Guid deviceId);
    void Touch(Guid deviceId);
}
