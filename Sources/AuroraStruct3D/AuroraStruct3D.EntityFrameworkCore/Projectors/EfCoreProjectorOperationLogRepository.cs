using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Projectors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 投影仪操作日志 EFCore 仓储实现
/// </summary>
public class EfCoreProjectorOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, ProjectorOperationLog, Guid>,
        IProjectorOperationLogRepository
{
    public EfCoreProjectorOperationLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<ProjectorOperationLog>> GetPagedListAsync(
        Guid projectorDeviceId,
        int skipCount,
        int maxResultCount,
        ProjectorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<ProjectorOperationLog> query = context
            .ProjectorOperationLogs.AsNoTracking()
            .Where(l => l.ProjectorDeviceId == projectorDeviceId);

        if (operationType.HasValue)
            query = query.Where(l => l.OperationType == operationType.Value);

        if (onlyFailures)
            query = query.Where(l => !l.IsSuccess);

        if (startTime.HasValue)
            query = query.Where(l => l.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OccurredAt <= endTime.Value);

        return await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        Guid projectorDeviceId,
        ProjectorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<ProjectorOperationLog> query = context
            .ProjectorOperationLogs.AsNoTracking()
            .Where(l => l.ProjectorDeviceId == projectorDeviceId);

        if (operationType.HasValue)
            query = query.Where(l => l.OperationType == operationType.Value);

        if (onlyFailures)
            query = query.Where(l => !l.IsSuccess);

        if (startTime.HasValue)
            query = query.Where(l => l.OccurredAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OccurredAt <= endTime.Value);

        return await query.LongCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteBeforeAsync(
        Guid projectorDeviceId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .ProjectorOperationLogs.Where(l =>
                l.ProjectorDeviceId == projectorDeviceId && l.OccurredAt < beforeUtc
            )
            .ExecuteDeleteAsync(cancellationToken);
    }
}
