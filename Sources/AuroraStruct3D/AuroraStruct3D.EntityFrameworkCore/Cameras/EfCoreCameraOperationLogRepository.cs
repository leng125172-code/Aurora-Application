using AuroraStruct3D.Cameras;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机操作日志 EFCore 仓储实现
/// </summary>
public class EfCoreCameraOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CameraOperationLog, Guid>,
        ICameraOperationLogRepository
{
    public EfCoreCameraOperationLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<CameraOperationLog>> GetPagedListAsync(
        Guid cameraDeviceId,
        int skipCount,
        int maxResultCount,
        CameraOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CameraOperationLog> query = context
            .CameraOperationLogs.AsNoTracking()
            .Where(l => l.CameraDeviceId == cameraDeviceId);

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
        Guid cameraDeviceId,
        CameraOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CameraOperationLog> query = context
            .CameraOperationLogs.AsNoTracking()
            .Where(l => l.CameraDeviceId == cameraDeviceId);

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
    public async Task DeleteBeforeAsync(
        Guid cameraDeviceId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        await context
            .CameraOperationLogs.Where(l =>
                l.CameraDeviceId == cameraDeviceId && l.OccurredAt < beforeUtc
            )
            .ExecuteDeleteAsync(cancellationToken);
    }
}
