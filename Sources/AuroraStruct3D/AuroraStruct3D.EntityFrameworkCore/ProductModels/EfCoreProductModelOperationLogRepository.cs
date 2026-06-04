using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.ProductModels;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 三维数模操作日志 EF Core 仓储实现。
/// </summary>
public class EfCoreProductModelOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, ProductModelOperationLog, Guid>,
        IProductModelOperationLogRepository
{
    public EfCoreProductModelOperationLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<ProductModelOperationLog>> GetPagedListAsync(
        Guid? productModelId = null,
        string? filter = null,
        ProductModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int skipCount = 0,
        int maxResultCount = 20,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<ProductModelOperationLog> query = BuildFilteredQuery(
            context,
            productModelId,
            filter,
            operationType,
            onlyFailures,
            startTime,
            endTime
        );

        return await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(skipCount)
            .Take(maxResultCount)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        Guid? productModelId = null,
        string? filter = null,
        ProductModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<ProductModelOperationLog> query = BuildFilteredQuery(
            context,
            productModelId,
            filter,
            operationType,
            onlyFailures,
            startTime,
            endTime
        );

        return await query.LongCountAsync(cancellationToken);
    }

    private static IQueryable<ProductModelOperationLog> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        Guid? productModelId,
        string? filter,
        ProductModelOperationType? operationType,
        bool onlyFailures,
        DateTime? startTime,
        DateTime? endTime
    )
    {
        IQueryable<ProductModelOperationLog> query = context.ProductModelOperationLogs;

        if (productModelId.HasValue)
        {
            query = query.Where(x => x.ProductModelId == productModelId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.ModelName.Contains(filter)
                || (x.OriginalFileName != null && x.OriginalFileName.Contains(filter))
                || (x.ParameterSummary != null && x.ParameterSummary.Contains(filter))
            );
        }

        if (operationType.HasValue)
        {
            query = query.Where(x => x.OperationType == operationType.Value);
        }

        if (onlyFailures)
        {
            query = query.Where(x => !x.IsSuccess);
        }

        if (startTime.HasValue)
        {
            query = query.Where(x => x.OccurredAt >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.OccurredAt <= endTime.Value);
        }

        return query;
    }
}
