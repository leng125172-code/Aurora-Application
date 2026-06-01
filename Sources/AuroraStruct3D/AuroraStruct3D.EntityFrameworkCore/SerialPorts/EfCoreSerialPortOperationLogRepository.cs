using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.SerialPorts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口操作日志 EFCore 仓储实现
/// </summary>
public class EfCoreSerialPortOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, SerialPortOperationLog, Guid>,
        ISerialPortOperationLogRepository
{
    public EfCoreSerialPortOperationLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<SerialPortOperationLog>> GetPagedListAsync(
        Guid serialPortConfigId,
        int skipCount,
        int maxResultCount,
        string? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<SerialPortOperationLog> query = context
            .SerialPortOperationLogs.AsNoTracking()
            .Where(l => l.SerialPortConfigId == serialPortConfigId);

        if (!string.IsNullOrWhiteSpace(operationType))
            query = query.Where(l => l.OperationType == operationType);

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
        Guid serialPortConfigId,
        string? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<SerialPortOperationLog> query = context
            .SerialPortOperationLogs.AsNoTracking()
            .Where(l => l.SerialPortConfigId == serialPortConfigId);

        if (!string.IsNullOrWhiteSpace(operationType))
            query = query.Where(l => l.OperationType == operationType);

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
        Guid serialPortConfigId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .SerialPortOperationLogs.Where(l =>
                l.SerialPortConfigId == serialPortConfigId && l.OccurredAt < beforeUtc
            )
            .ExecuteDeleteAsync(cancellationToken);
    }
}
