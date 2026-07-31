using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Cameras;

public sealed class CameraDriverRegistry : ICameraDriverRegistry
{
    private readonly ILogger<CameraDriverRegistry> _logger;
    private readonly IReadOnlyDictionary<string, ICameraDriver> _drivers;

    public CameraDriverRegistry(
        IEnumerable<ICameraDriver> drivers,
        ILogger<CameraDriverRegistry> logger
    )
    {
        _logger = logger;
        IGrouping<string, ICameraDriver>? duplicate = drivers
            .GroupBy(x => x.DriverId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"相机驱动标识重复：{duplicate.Key}");

        _drivers = drivers.ToDictionary(x => x.DriverId, StringComparer.OrdinalIgnoreCase);
        Drivers = _drivers.Values.ToList();
    }

    public IReadOnlyList<ICameraDriver> Drivers { get; }

    public bool TryGet(string driverId, out ICameraDriver? driver) =>
        _drivers.TryGetValue(driverId, out driver);

    public ICameraDriver GetRequired(string driverId) =>
        TryGet(driverId, out ICameraDriver? driver)
            ? driver!
            : throw new InvalidOperationException($"未注册相机驱动：{driverId}");

    public async Task<IReadOnlyList<CameraDriverScanResult>> ScanAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        Task<CameraDriverScanResult>[] scans = Drivers.Select(async driver =>
        {
            try
            {
                IReadOnlyList<CameraDiscovery> devices = await driver
                    .ScanAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CameraDriverScanResult(
                    driver.DriverId,
                    driver.DisplayName,
                    devices,
                    null
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "相机驱动 {DriverId} 扫描失败", driver.DriverId);
                return new CameraDriverScanResult(
                    driver.DriverId,
                    driver.DisplayName,
                    [],
                    ex.Message
                );
            }
        }).ToArray();
        return await Task.WhenAll(scans).ConfigureAwait(false);
    }
}
