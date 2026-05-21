using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Projectors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 投影机设备 EFCore 仓储实现
/// </summary>
public class EfCoreProjectorDeviceRepository
    : EfCoreRepository<AuroraStruct3DDbContext, ProjectorDevice, Guid>,
        IProjectorDeviceRepository
{
    public EfCoreProjectorDeviceRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<ProjectorDevice?> FindByIpAddressAsync(
        string ipAddress,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .ProjectorDevices.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IpAddress == ipAddress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProjectorDevice?> FindByHidAsync(
        int deviceIndex = 0,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .ProjectorDevices.AsNoTracking()
            .FirstOrDefaultAsync(
                p =>
                    p.ConnectionType == ProjectorConnectionType.UsbHid
                    && p.HidDeviceIndex == deviceIndex,
                cancellationToken
            );
    }

    /// <inheritdoc/>
    public async Task<List<ProjectorDevice>> GetEnabledListAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .ProjectorDevices.AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.DeviceIndex)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<ProjectorDevice>> GetListOrderedAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .ProjectorDevices.AsNoTracking()
            .OrderBy(p => p.DeviceIndex)
            .ToListAsync(cancellationToken);
    }
}
