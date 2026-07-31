using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuroraStruct3D.Plcs;

public class PlcDriverRegistryTests
{
    [Fact]
    public void Registry_exposes_installed_and_planned_drivers()
    {
        var registry = new PlcDriverRegistry([new FakeDriver()]);

        Assert.True(registry.TryGet(PlcDriverIds.OpcUa, out _));
        Assert.Contains(
            registry.Drivers,
            x => x.DriverId == PlcDriverIds.SiemensS7 && !x.IsInstalled
        );
        Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequired(PlcDriverIds.OmronFinsTcp)
        );
    }

    [Fact]
    public void Registry_rejects_duplicate_driver_ids()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PlcDriverRegistry([new FakeDriver(), new FakeDriver()])
        );
    }

    private sealed class FakeDriver : IPlcDriver
    {
        public PlcDriverDescriptor Descriptor { get; } =
            new(
                PlcDriverIds.OpcUa,
                PlcProtocolType.OpcUa,
                "Fake",
                PlcCapability.Read,
                true
            );

        public Task<IPlcConnection> ConnectAsync(
            PlcConnectionOptions options,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task ValidateAddressAsync(
            string address,
            PlcTagDataType dataType,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<IReadOnlyList<PlcValue>> ReadAsync(
            IPlcConnection connection,
            IReadOnlyList<PlcReadRequest> requests,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<PlcWriteResult>> WriteAsync(
            IPlcConnection connection,
            IReadOnlyList<PlcWriteRequest> requests,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();
    }
}
