using AuroraStruct3D.Cameras;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Cameras;

public class CameraScanMatchingTests
{
    [Fact]
    public void Stable_identity_is_driver_and_hardware_id_not_runtime_index()
    {
        var first = new CameraDevice(Guid.NewGuid(), "USB", 0);
        first.UpdateDriverBinding("tucam", "SERIAL-1", "USB index 0", CameraCapability.Preview);
        var second = new CameraDevice(Guid.NewGuid(), "GigE", 0);
        second.UpdateDriverBinding("dahua-gige", "SERIAL-1", "192.168.1.10", CameraCapability.Preview);

        Assert.Equal(first.DeviceIndex, second.DeviceIndex);
        Assert.NotEqual(first.DriverId, second.DriverId);
        Assert.Equal(first.HardwareId, second.HardwareId);
    }

    [Fact]
    public void Empty_hardware_id_remains_unbound()
    {
        var camera = new CameraDevice(Guid.NewGuid(), "Legacy", 0);

        camera.UpdateDriverBinding("tucam", "  ", null, CameraCapability.None);

        Assert.Null(camera.HardwareId);
        Assert.Equal(CameraCapability.None, camera.Capabilities);
    }

    [Fact]
    public void Binding_refresh_always_replaces_capability_snapshot()
    {
        var camera = new CameraDevice(Guid.NewGuid(), "Camera", 0);
        camera.UpdateDriverBinding("tucam", "SERIAL", "USB index 0", CameraCapability.Preview);

        camera.UpdateDriverBinding(
            "tucam",
            "SERIAL",
            "USB index 1",
            CameraCapability.Preview | CameraCapability.Snapshot
        );

        Assert.Equal("USB index 1", camera.ConnectionSummary);
        Assert.True((camera.Capabilities & CameraCapability.Snapshot) != 0);
    }
}
