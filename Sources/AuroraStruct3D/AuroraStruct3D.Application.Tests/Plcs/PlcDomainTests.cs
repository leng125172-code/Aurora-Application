using Xunit;

namespace AuroraStruct3D.Plcs;

public class PlcDomainTests
{
    [Fact]
    public void Tag_rejects_invalid_engineering_configuration()
    {
        var tag = new PlcTag(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "temperature",
            "温度",
            "ns=2;s=Temperature",
            PlcTagDataType.Double,
            PlcTagAccess.ReadWrite
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tag.ConfigureEngineering(500, null, 0, 0, "°C", "F2", null, null)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tag.ConfigureEngineering(500, null, 1, 0, "°C", "F2", 100, 0)
        );
    }

    [Fact]
    public void Device_rejects_invalid_reconnect_window()
    {
        var device = new PlcDevice(
            Guid.NewGuid(),
            "PLC-1",
            PlcProtocolType.OpcUa,
            PlcDriverIds.OpcUa,
            "opc.tcp://localhost:4840"
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.ConfigureRuntime(5000, 5000, 60000, 5000, 30000, 1000, 600000, null)
        );
    }
}
