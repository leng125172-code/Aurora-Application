namespace AuroraStruct3D.Plcs;

public sealed class PlcDriverRegistry : IPlcDriverRegistry
{
    private static readonly PlcDriverDescriptor[] PlannedDrivers =
    [
        new(
            PlcDriverIds.SiemensS7,
            PlcProtocolType.SiemensS7,
            "Siemens S7",
            PlcCapability.None,
            false
        ),
        new(
            PlcDriverIds.OmronFinsTcp,
            PlcProtocolType.OmronFinsTcp,
            "Omron FINS TCP",
            PlcCapability.None,
            false
        ),
        new(
            PlcDriverIds.OmronFinsUdp,
            PlcProtocolType.OmronFinsUdp,
            "Omron FINS UDP",
            PlcCapability.None,
            false
        ),
        new(
            PlcDriverIds.KeyenceMc,
            PlcProtocolType.KeyenceMc,
            "Keyence MC/SLMP",
            PlcCapability.None,
            false
        ),
    ];

    private readonly IReadOnlyDictionary<string, IPlcDriver> _installed;

    public PlcDriverRegistry(IEnumerable<IPlcDriver> drivers)
    {
        IGrouping<string, IPlcDriver>? duplicate = drivers
            .GroupBy(x => x.Descriptor.DriverId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"PLC 驱动标识重复：{duplicate.Key}");

        _installed = drivers.ToDictionary(
            x => x.Descriptor.DriverId,
            StringComparer.OrdinalIgnoreCase
        );
        Drivers = _installed.Values.Select(x => x.Descriptor).Concat(PlannedDrivers).ToArray();
    }

    public IReadOnlyList<PlcDriverDescriptor> Drivers { get; }

    public bool TryGet(string driverId, out IPlcDriver? driver) =>
        _installed.TryGetValue(driverId, out driver);

    public IPlcDriver GetRequired(string driverId) =>
        TryGet(driverId, out IPlcDriver? driver)
            ? driver!
            : throw new InvalidOperationException($"PLC 驱动未安装：{driverId}");
}
