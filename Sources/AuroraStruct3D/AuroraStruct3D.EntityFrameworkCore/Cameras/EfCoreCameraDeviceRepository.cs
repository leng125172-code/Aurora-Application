using AuroraStruct3D.Cameras;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备 EFCore 仓储实现
/// </summary>
public class EfCoreCameraDeviceRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CameraDevice, Guid>,
        ICameraDeviceRepository
{
    public EfCoreCameraDeviceRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<CameraDevice?> FindByDeviceIndexAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraDevices.AsNoTracking()
            .FirstOrDefaultAsync(c => c.DeviceIndex == deviceIndex, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<CameraDevice>> GetEnabledListAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraDevices.AsNoTracking()
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DeviceIndex)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<CameraDevice>> GetListAsync(
        int skipCount,
        int maxResultCount,
        string? filter = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CameraDevice> query = context.CameraDevices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(c =>
                c.Name.Contains(filter)
                || (c.Model != null && c.Model.Contains(filter))
                || (c.SerialNumber != null && c.SerialNumber.Contains(filter))
            );
        }

        return await query
            .OrderBy(c => c.DeviceIndex)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        string? filter = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CameraDevice> query = context.CameraDevices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(c =>
                c.Name.Contains(filter)
                || (c.Model != null && c.Model.Contains(filter))
                || (c.SerialNumber != null && c.SerialNumber.Contains(filter))
            );
        }

        return await query.LongCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CameraDevice?> GetWithParameterSetsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraDevices.Include(c => c.ParameterSets)
                .ThenInclude(s => s.Parameters)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetDeviceIndexByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        // AsNoTracking 投影查询，仅获取 DeviceIndex，不加载或跟踪完整实体
        // 避免在长时间 SDK 操作期间 EF Core 变更跟踪引发并发冲突
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraDevices.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.DeviceIndex)
            .FirstAsync(cancellationToken);
    }
}
