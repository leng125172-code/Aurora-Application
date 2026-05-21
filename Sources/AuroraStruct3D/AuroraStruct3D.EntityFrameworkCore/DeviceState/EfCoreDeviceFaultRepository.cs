using AuroraStruct3D.DeviceState;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备故障记录 EFCore 仓储实现
/// </summary>
public class EfCoreDeviceFaultRepository
    : EfCoreRepository<AuroraStruct3DDbContext, DeviceFault, Guid>,
        IDeviceFaultRepository
{
    public EfCoreDeviceFaultRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<DeviceFault>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        DeviceFaultLevel? faultLevel = null,
        bool? isResolved = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<DeviceFault> query = context.DeviceFaults.AsNoTracking();

        if (faultLevel.HasValue)
            query = query.Where(f => f.FaultLevel == faultLevel.Value);

        if (isResolved.HasValue)
            query = query.Where(f => f.IsResolved == isResolved.Value);

        if (startTime.HasValue)
            query = query.Where(f => f.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(f => f.OccurredAt <= endTime.Value);

        return await query
            .OrderByDescending(f => f.OccurredAt)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        DeviceFaultLevel? faultLevel = null,
        bool? isResolved = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<DeviceFault> query = context.DeviceFaults.AsNoTracking();

        if (faultLevel.HasValue)
            query = query.Where(f => f.FaultLevel == faultLevel.Value);

        if (isResolved.HasValue)
            query = query.Where(f => f.IsResolved == isResolved.Value);

        if (startTime.HasValue)
            query = query.Where(f => f.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(f => f.OccurredAt <= endTime.Value);

        return await query.LongCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<DeviceFault>> GetUnresolvedListAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .DeviceFaults.AsNoTracking()
            .Where(f => !f.IsResolved)
            .OrderByDescending(f => f.FaultLevel)
            .ThenByDescending(f => f.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteResolvedBeforeAsync(
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        await context
            .DeviceFaults.Where(f => f.IsResolved && f.OccurredAt < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
