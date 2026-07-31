namespace AuroraStruct3D.Cameras;

public sealed record CameraDiscovery(
    string DriverId,
    string HardwareId,
    string Model,
    string? ConnectionSummary,
    CameraCapability Capabilities,
    int RuntimeIndex
);

public sealed class CameraDriverFrame
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int BitDepth { get; init; }
    public int Channels { get; init; }
    public uint FrameIndex { get; init; }
    public byte[] Data { get; init; } = [];
}

/// <summary>
/// 厂商无关的相机驱动边界。HardwareId 是跨扫描稳定的身份；
/// RuntimeIndex 只能由驱动内部维护，禁止作为上层控制依据。
/// </summary>
public interface ICameraDriver
{
    string DriverId { get; }
    string DisplayName { get; }

    Task<IReadOnlyList<CameraDiscovery>> ScanAsync(CancellationToken cancellationToken = default);
    Task OpenAsync(string hardwareId, CancellationToken cancellationToken = default);
    Task CloseAsync(string hardwareId, CancellationToken cancellationToken = default);
    Task StartCaptureAsync(string hardwareId, CancellationToken cancellationToken = default);
    Task StopCaptureAsync(string hardwareId, CancellationToken cancellationToken = default);
    Task<CameraDriverFrame> GrabFrameAsync(
        string hardwareId,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default
    );
    Task<byte[]> GrabJpegAsync(
        string hardwareId,
        int timeoutMs = 8000,
        int imageRotationAngle = 0,
        CancellationToken cancellationToken = default
    );
    Task SoftwareTriggerAsync(string hardwareId, CancellationToken cancellationToken = default);
    bool IsOpen(string hardwareId);
    bool IsCapturing(string hardwareId);
    bool TryGetRuntimeIndex(string hardwareId, out int runtimeIndex);
}

/// <summary>驱动可选的参数节点访问能力；节点值使用稳定字符串表示。</summary>
public interface ICameraParameterNodeProvider
{
    Task<string?> ReadNodeAsync(
        string hardwareId,
        string nodeName,
        string dataType,
        CancellationToken cancellationToken = default
    );
    Task WriteNodeAsync(
        string hardwareId,
        string nodeName,
        string dataType,
        string value,
        CancellationToken cancellationToken = default
    );
    Task ExecuteNodeCommandAsync(
        string hardwareId,
        string nodeName,
        CancellationToken cancellationToken = default
    );
}

public interface ICameraDriverRegistry
{
    IReadOnlyList<ICameraDriver> Drivers { get; }
    bool TryGet(string driverId, out ICameraDriver? driver);
    ICameraDriver GetRequired(string driverId);
    Task<IReadOnlyList<CameraDriverScanResult>> ScanAllAsync(
        CancellationToken cancellationToken = default
    );
}

public sealed record CameraDriverScanResult(
    string DriverId,
    string DisplayName,
    IReadOnlyList<CameraDiscovery> Devices,
    string? Error
);
