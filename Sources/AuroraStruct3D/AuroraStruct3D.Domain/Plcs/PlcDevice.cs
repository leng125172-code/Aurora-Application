using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp;

namespace AuroraStruct3D.Plcs;

public class PlcDevice : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public PlcProtocolType Protocol { get; private set; }
    public string DriverId { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public string EndpointUrl { get; private set; } = string.Empty;
    public PlcAuthenticationType AuthenticationType { get; private set; }
    public string? UserName { get; private set; }
    public string? EncryptedPassword { get; private set; }
    public string? ClientCertificatePath { get; private set; }
    public string? EncryptedClientCertificatePassword { get; private set; }
    public string SecurityPolicy { get; private set; } = "None";
    public PlcMessageSecurityMode MessageSecurityMode { get; private set; }
    public bool AutoTrustServerCertificate { get; private set; } = true;
    public int ConnectTimeoutMs { get; private set; } = PlcConsts.DefaultConnectTimeoutMs;
    public int OperationTimeoutMs { get; private set; } = PlcConsts.DefaultOperationTimeoutMs;
    public int SessionTimeoutMs { get; private set; } = 60000;
    public int KeepAliveMs { get; private set; } = PlcConsts.DefaultKeepAliveMs;
    public int ReconnectInitialMs { get; private set; } = PlcConsts.DefaultReconnectInitialMs;
    public int ReconnectMaxMs { get; private set; } = PlcConsts.DefaultReconnectMaxMs;
    public int IdleTimeoutMs { get; private set; } = PlcConsts.DefaultIdleTimeoutMs;
    public string? ExtensionJson { get; private set; }
    public PlcConnectionStatus ConnectionStatus { get; private set; }
    public DateTime? LastConnectedAt { get; private set; }
    public DateTime? LastFailedAt { get; private set; }
    public string? LastError { get; private set; }

    protected PlcDevice() { }

    public PlcDevice(
        Guid id,
        string name,
        PlcProtocolType protocol,
        string driverId,
        string endpointUrl
    ) : base(id)
    {
        SetConfiguration(name, protocol, driverId, endpointUrl);
    }

    public void SetConfiguration(
        string name,
        PlcProtocolType protocol,
        string driverId,
        string endpointUrl
    )
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), PlcConsts.MaxNameLength);
        DriverId = Check.NotNullOrWhiteSpace(
            driverId,
            nameof(driverId),
            PlcConsts.MaxDriverIdLength
        );
        EndpointUrl = Check.NotNullOrWhiteSpace(
            endpointUrl,
            nameof(endpointUrl),
            PlcConsts.MaxEndpointLength
        );
        Protocol = protocol;
    }

    public void ConfigureSecurity(
        PlcAuthenticationType authenticationType,
        string? userName,
        string? encryptedPassword,
        string? certificatePath,
        string? encryptedCertificatePassword,
        string securityPolicy,
        PlcMessageSecurityMode messageSecurityMode,
        bool autoTrust
    )
    {
        AuthenticationType = authenticationType;
        UserName = userName;
        if (encryptedPassword != null)
            EncryptedPassword = encryptedPassword;
        ClientCertificatePath = certificatePath;
        if (encryptedCertificatePassword != null)
            EncryptedClientCertificatePassword = encryptedCertificatePassword;
        SecurityPolicy = Check.NotNullOrWhiteSpace(
            securityPolicy,
            nameof(securityPolicy),
            PlcConsts.MaxSecurityPolicyLength
        );
        MessageSecurityMode = messageSecurityMode;
        AutoTrustServerCertificate = autoTrust;
    }

    public void ConfigureRuntime(
        int connectTimeoutMs,
        int operationTimeoutMs,
        int sessionTimeoutMs,
        int keepAliveMs,
        int reconnectInitialMs,
        int reconnectMaxMs,
        int idleTimeoutMs,
        string? extensionJson
    )
    {
        if (
            connectTimeoutMs <= 0
            || operationTimeoutMs <= 0
            || sessionTimeoutMs <= 0
            || keepAliveMs <= 0
            || reconnectInitialMs <= 0
            || reconnectMaxMs < reconnectInitialMs
            || idleTimeoutMs <= 0
        )
            throw new ArgumentOutOfRangeException(nameof(connectTimeoutMs), "PLC 超时和重连参数无效");

        ConnectTimeoutMs = connectTimeoutMs;
        OperationTimeoutMs = operationTimeoutMs;
        SessionTimeoutMs = sessionTimeoutMs;
        KeepAliveMs = keepAliveMs;
        ReconnectInitialMs = reconnectInitialMs;
        ReconnectMaxMs = reconnectMaxMs;
        IdleTimeoutMs = idleTimeoutMs;
        ExtensionJson = extensionJson;
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    public void SetConnectionState(PlcConnectionStatus status, string? error = null)
    {
        ConnectionStatus = status;
        LastError = error?.Length > PlcConsts.MaxErrorLength
            ? error[..PlcConsts.MaxErrorLength]
            : error;
        if (status == PlcConnectionStatus.Connected)
            LastConnectedAt = DateTime.UtcNow;
        if (status == PlcConnectionStatus.Faulted)
            LastFailedAt = DateTime.UtcNow;
    }
}
