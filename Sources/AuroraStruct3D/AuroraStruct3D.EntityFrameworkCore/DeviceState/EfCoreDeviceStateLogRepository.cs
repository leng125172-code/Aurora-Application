using AuroraStruct3D.DeviceState;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态切换日志 EFCore 仓储实现
/// </summary>
public class EfCoreDeviceStateLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, DeviceStateLog, Guid>,
        IDeviceStateLogRepository
{
    public EfCoreDeviceStateLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<DeviceStateLog>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        StateChangeTrigger? trigger = null,
        DeviceStatus? status = null,
        bool onlyStatusChanges = false,
        bool onlyModeChanges = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? isSuccessful = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<DeviceStateLog> query = context.DeviceStateLogs.AsNoTracking();

        if (trigger.HasValue)
            query = query.Where(l => l.Trigger == trigger.Value);

        if (status.HasValue)
            query = query.Where(l => l.NewStatus == status.Value);

        if (onlyStatusChanges)
            query = query.Where(l => l.IsStatusChange);

        if (onlyModeChanges)
            query = query.Where(l => l.IsModeChange);

        if (startTime.HasValue)
            query = query.Where(l => l.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OccurredAt <= endTime.Value);

        if (isSuccessful.HasValue)
            query = query.Where(l => l.IsSuccessful == isSuccessful.Value);

        return await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        StateChangeTrigger? trigger = null,
        DeviceStatus? status = null,
        bool onlyStatusChanges = false,
        bool onlyModeChanges = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? isSuccessful = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<DeviceStateLog> query = context.DeviceStateLogs.AsNoTracking();

        if (trigger.HasValue)
            query = query.Where(l => l.Trigger == trigger.Value);

        if (status.HasValue)
            query = query.Where(l => l.NewStatus == status.Value);

        if (onlyStatusChanges)
            query = query.Where(l => l.IsStatusChange);

        if (onlyModeChanges)
            query = query.Where(l => l.IsModeChange);

        if (startTime.HasValue)
            query = query.Where(l => l.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OccurredAt <= endTime.Value);

        if (isSuccessful.HasValue)
            query = query.Where(l => l.IsSuccessful == isSuccessful.Value);

        return await query.LongCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteBeforeAsync(
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        await context
            .DeviceStateLogs.Where(l => l.OccurredAt < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
