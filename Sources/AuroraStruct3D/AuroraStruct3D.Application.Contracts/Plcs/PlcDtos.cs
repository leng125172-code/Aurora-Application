using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Plcs;

public class PlcDeviceDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public PlcProtocolType Protocol { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public bool DriverInstalled { get; set; }
    public PlcCapability Capabilities { get; set; }
    public bool IsEnabled { get; set; }
    public string EndpointUrl { get; set; } = string.Empty;
    public PlcAuthenticationType AuthenticationType { get; set; }
    public string? UserName { get; set; }
    public bool HasPassword { get; set; }
    public string? ClientCertificatePath { get; set; }
    public bool HasPrivateKey { get; set; }
    public string SecurityPolicy { get; set; } = "None";
    public PlcMessageSecurityMode MessageSecurityMode { get; set; }
    public bool AutoTrustServerCertificate { get; set; }
    public int ConnectTimeoutMs { get; set; }
    public int OperationTimeoutMs { get; set; }
    public int SessionTimeoutMs { get; set; }
    public int KeepAliveMs { get; set; }
    public int ReconnectInitialMs { get; set; }
    public int ReconnectMaxMs { get; set; }
    public int IdleTimeoutMs { get; set; }
    public string? ExtensionJson { get; set; }
    public PlcConnectionStatus ConnectionStatus { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public string? LastError { get; set; }
}

public class SavePlcDeviceDto
{
    [Required, StringLength(PlcConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
    public PlcProtocolType Protocol { get; set; }
    [Required, StringLength(PlcConsts.MaxDriverIdLength)]
    public string DriverId { get; set; } = PlcDriverIds.OpcUa;
    public bool IsEnabled { get; set; } = true;
    [Required, StringLength(PlcConsts.MaxEndpointLength)]
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";
    public PlcAuthenticationType AuthenticationType { get; set; }
    [StringLength(PlcConsts.MaxUserNameLength)]
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ClientCertificatePath { get; set; }
    public string? ClientCertificatePassword { get; set; }
    public string SecurityPolicy { get; set; } = "None";
    public PlcMessageSecurityMode MessageSecurityMode { get; set; }
    public bool AutoTrustServerCertificate { get; set; } = true;
    [Range(100, 300000)]
    public int ConnectTimeoutMs { get; set; } = PlcConsts.DefaultConnectTimeoutMs;
    [Range(100, 300000)]
    public int OperationTimeoutMs { get; set; } = PlcConsts.DefaultOperationTimeoutMs;
    [Range(1000, 3600000)]
    public int SessionTimeoutMs { get; set; } = 60000;
    [Range(100, 300000)]
    public int KeepAliveMs { get; set; } = PlcConsts.DefaultKeepAliveMs;
    [Range(100, 300000)]
    public int ReconnectInitialMs { get; set; } = PlcConsts.DefaultReconnectInitialMs;
    [Range(100, 300000)]
    public int ReconnectMaxMs { get; set; } = PlcConsts.DefaultReconnectMaxMs;
    [Range(1000, 86400000)]
    public int IdleTimeoutMs { get; set; } = PlcConsts.DefaultIdleTimeoutMs;
    public string? ExtensionJson { get; set; }
}

public class PlcTagDto : FullAuditedEntityDto<Guid>
{
    public Guid PlcDeviceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public PlcTagDataType DataType { get; set; }
    public PlcTagAccess Access { get; set; }
    public bool IsEnabled { get; set; }
    public int SamplingIntervalMs { get; set; }
    public double? Deadband { get; set; }
    public double Scale { get; set; }
    public double Offset { get; set; }
    public string? Unit { get; set; }
    public string? DisplayFormat { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
}

public class SavePlcTagDto
{
    [Required, StringLength(PlcConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(PlcConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(PlcConsts.MaxAddressLength)]
    public string Address { get; set; } = string.Empty;
    public PlcTagDataType DataType { get; set; }
    public PlcTagAccess Access { get; set; } = PlcTagAccess.Read;
    public bool IsEnabled { get; set; } = true;
    [Range(50, 3600000)]
    public int SamplingIntervalMs { get; set; } = PlcConsts.DefaultSamplingIntervalMs;
    public double? Deadband { get; set; }
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    [StringLength(PlcConsts.MaxUnitLength)]
    public string? Unit { get; set; }
    [StringLength(PlcConsts.MaxFormatLength)]
    public string? DisplayFormat { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
}

public class PlcReadInput
{
    public List<Guid> TagIds { get; set; } = [];
}

public class PlcWriteItemDto
{
    public Guid TagId { get; set; }
    public object? Value { get; set; }
}

public class PlcWriteInput
{
    public List<PlcWriteItemDto> Items { get; set; } = [];
}

public class PlcTagValueDto
{
    public Guid TagId { get; set; }
    public string Code { get; set; } = string.Empty;
    public object? RawValue { get; set; }
    public object? EngineeringValue { get; set; }
    public PlcTagDataType DataType { get; set; }
    public string Quality { get; set; } = string.Empty;
    public DateTime? SourceTimestamp { get; set; }
    public DateTime? ServerTimestamp { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? Error { get; set; }
}

public class PlcWriteResultDto
{
    public Guid TagId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class PlcBrowseInput
{
    public string? ParentAddress { get; set; }
    public string? ContinuationToken { get; set; }
    [Range(1, 1000)]
    public int MaxResults { get; set; } = 200;
}

public class PlcBrowseTreeInput
{
    public string? RootAddress { get; set; }
    [Range(1, 16)]
    public int MaxDepth { get; set; } = 8;
    [Range(1, 10000)]
    public int MaxNodes { get; set; } = 3000;
}

public class PlcBrowseTreeNodeDto
{
    public string Address { get; set; } = string.Empty;
    public string BrowseName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NodeClass { get; set; } = string.Empty;
    public PlcTagDataType? DataType { get; set; }
    public PlcTagAccess Access { get; set; }
    public bool HasChildren { get; set; }
    public List<PlcBrowseTreeNodeDto> Children { get; set; } = [];
}

public class PlcBrowseTreeResultDto
{
    public List<PlcBrowseTreeNodeDto> Items { get; set; } = [];
    public int NodeCount { get; set; }
    public bool Truncated { get; set; }
}

public class PlcConnectionTestResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}

public class PlcSubscribeInput
{
    [Required]
    public string ConnectionId { get; set; } = string.Empty;
    public List<Guid> TagIds { get; set; } = [];
}

public class PlcSubscriptionResultDto
{
    public string SubscriptionId { get; set; } = string.Empty;
}

public class ConfirmPlcCertificateInput
{
    [Required, StringLength(128)]
    public string Thumbprint { get; set; } = string.Empty;
}
