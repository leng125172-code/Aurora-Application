namespace AuroraStruct3D.Plcs;

[Flags]
public enum PlcCapability
{
    None = 0,
    Browse = 1,
    Read = 2,
    Write = 4,
    Subscribe = 8,
    BatchRead = 16,
    BatchWrite = 32,
    SecureChannel = 64,
}

public enum PlcProtocolType
{
    OpcUa = 0,
    SiemensS7 = 1,
    OmronFinsTcp = 2,
    OmronFinsUdp = 3,
    KeyenceMc = 4,
}

public enum PlcConnectionStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4,
}

public enum PlcAuthenticationType
{
    Anonymous = 0,
    UserName = 1,
    Certificate = 2,
}

public enum PlcMessageSecurityMode
{
    None = 0,
    Sign = 1,
    SignAndEncrypt = 2,
}

public enum PlcTagDataType
{
    Boolean,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    String,
    DateTime,
    ByteString,
}

[Flags]
public enum PlcTagAccess
{
    None = 0,
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}

public enum PlcOperationType
{
    Connect,
    Disconnect,
    Reconnect,
    TestConnection,
    TrustCertificate,
    ConfigurationChanged,
    Read,
    Write,
    Subscribe,
    Unsubscribe,
}

public static class PlcDriverIds
{
    public const string OpcUa = "opc-ua";
    public const string SiemensS7 = "siemens-s7";
    public const string OmronFinsTcp = "omron-fins-tcp";
    public const string OmronFinsUdp = "omron-fins-udp";
    public const string KeyenceMc = "keyence-mc";
}

public static class PlcConsts
{
    public const int MaxNameLength = 128;
    public const int MaxCodeLength = 128;
    public const int MaxAddressLength = 512;
    public const int MaxEndpointLength = 512;
    public const int MaxDriverIdLength = 64;
    public const int MaxUserNameLength = 128;
    public const int MaxSecurityPolicyLength = 256;
    public const int MaxUnitLength = 32;
    public const int MaxFormatLength = 32;
    public const int MaxErrorLength = 1024;
    public const int DefaultConnectTimeoutMs = 5000;
    public const int DefaultOperationTimeoutMs = 5000;
    public const int DefaultKeepAliveMs = 5000;
    public const int DefaultReconnectInitialMs = 1000;
    public const int DefaultReconnectMaxMs = 30000;
    public const int DefaultIdleTimeoutMs = 600000;
    public const int DefaultSamplingIntervalMs = 500;
}
