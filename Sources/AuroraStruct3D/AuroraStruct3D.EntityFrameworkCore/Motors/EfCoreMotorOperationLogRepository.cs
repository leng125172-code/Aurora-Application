using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Motors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机操作日志 EFCore 仓储实现
/// </summary>
public class EfCoreMotorOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, MotorOperationLog, Guid>,
        IMotorOperationLogRepository
{
  public EfCoreMotorOperationLogRepository(
      IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
  )
      : base(dbContextProvider) { }

  /// <inheritdoc/>
  public async Task<List<MotorOperationLog>> GetPagedListAsync(
      Guid motorAxisId,
      int skipCount,
      int maxResultCount,
      MotorOperationType? operationType = null,
      bool onlyFailures = false,
      CancellationToken cancellationToken = default
  )
  {
    AuroraStruct3DDbContext context = await GetDbContextAsync();
    IQueryable<MotorOperationLog> query = context
        .MotorOperationLogs.AsNoTracking()
        .Where(l => l.MotorAxisId == motorAxisId);

    if (operationType.HasValue)
      query = query.Where(l => l.OperationType == operationType.Value);

    if (onlyFailures)
      query = query.Where(l => !l.IsSuccess);

    return await query
        .OrderByDescending(l => l.OccurredAt)
        .Skip(skipCount)
        .Take(maxResultCount)
        .ToListAsync(cancellationToken);
  }

  /// <inheritdoc/>
  public async Task<long> GetCountAsync(
      Guid motorAxisId,
      MotorOperationType? operationType = null,
      bool onlyFailures = false,
      CancellationToken cancellationToken = default
  )
  {
    AuroraStruct3DDbContext context = await GetDbContextAsync();
    IQueryable<MotorOperationLog> query = context
        .MotorOperationLogs.AsNoTracking()
        .Where(l => l.MotorAxisId == motorAxisId);

    if (operationType.HasValue)
      query = query.Where(l => l.OperationType == operationType.Value);

    if (onlyFailures)
      query = query.Where(l => !l.IsSuccess);

    return await query.LongCountAsync(cancellationToken);
  }

  /// <inheritdoc/>
  public async Task<int> DeleteBeforeAsync(
      Guid motorAxisId,
      DateTime beforeUtc,
      CancellationToken cancellationToken = default
  )
  {
    AuroraStruct3DDbContext context = await GetDbContextAsync();
    return await context
        .MotorOperationLogs.Where(l =>
            l.MotorAxisId == motorAxisId && l.OccurredAt < beforeUtc
        )
        .ExecuteDeleteAsync(cancellationToken);
  }
}
