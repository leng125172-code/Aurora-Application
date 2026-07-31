using AuroraStruct3D.Cameras;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Cameras;

public class CameraDriverRegistryTests
{
    [Fact]
    public async Task ScanAllAsync_keeps_same_runtime_index_isolated_by_driver()
    {
        var first = new FakeDriver(
            "first",
            [new CameraDiscovery("first", "SERIAL", "M1", "USB", CameraCapability.Preview, 0)]
        );
        var second = new FakeDriver(
            "second",
            [new CameraDiscovery("second", "SERIAL", "M2", "GigE", CameraCapability.Preview, 0)]
        );
        var registry = new CameraDriverRegistry(
            [first, second],
            NullLogger<CameraDriverRegistry>.Instance
        );

        IReadOnlyList<CameraDriverScanResult> result = await registry.ScanAllAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Single(x.Devices));
        Assert.Equal(0, result[0].Devices[0].RuntimeIndex);
        Assert.Equal(0, result[1].Devices[0].RuntimeIndex);
        Assert.NotSame(registry.GetRequired("first"), registry.GetRequired("second"));
    }

    [Fact]
    public async Task ScanAllAsync_isolates_driver_failure()
    {
        var healthy = new FakeDriver("healthy", []);
        var failed = new FakeDriver("failed", [], new InvalidOperationException("offline"));
        var registry = new CameraDriverRegistry(
            [healthy, failed],
            NullLogger<CameraDriverRegistry>.Instance
        );

        IReadOnlyList<CameraDriverScanResult> result = await registry.ScanAllAsync();

        Assert.Null(result.Single(x => x.DriverId == "healthy").Error);
        Assert.Contains("offline", result.Single(x => x.DriverId == "failed").Error);
    }

    [Fact]
    public void Constructor_rejects_duplicate_driver_id()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                new CameraDriverRegistry(
                    [new FakeDriver("same", []), new FakeDriver("SAME", [])],
                    NullLogger<CameraDriverRegistry>.Instance
                )
        );
    }

    private sealed class FakeDriver(
        string driverId,
        IReadOnlyList<CameraDiscovery> devices,
        Exception? scanError = null
    ) : ICameraDriver
    {
        public string DriverId => driverId;
        public string DisplayName => driverId;

        public Task<IReadOnlyList<CameraDiscovery>> ScanAsync(
            CancellationToken cancellationToken = default
        ) =>
            scanError == null
                ? Task.FromResult(devices)
                : Task.FromException<IReadOnlyList<CameraDiscovery>>(scanError);

        public Task OpenAsync(string hardwareId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task CloseAsync(string hardwareId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task StartCaptureAsync(
            string hardwareId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
        public Task StopCaptureAsync(
            string hardwareId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
        public Task<CameraDriverFrame> GrabFrameAsync(
            string hardwareId,
            int timeoutMs = 3000,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new CameraDriverFrame());
        public Task<byte[]> GrabJpegAsync(
            string hardwareId,
            int timeoutMs = 8000,
            int imageRotationAngle = 0,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Array.Empty<byte>());
        public Task SoftwareTriggerAsync(
            string hardwareId,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
        public bool IsOpen(string hardwareId) => false;
        public bool IsCapturing(string hardwareId) => false;
        public bool TryGetRuntimeIndex(string hardwareId, out int runtimeIndex)
        {
            CameraDiscovery? device = devices.FirstOrDefault(x => x.HardwareId == hardwareId);
            runtimeIndex = device?.RuntimeIndex ?? -1;
            return device != null;
        }
    }
}
