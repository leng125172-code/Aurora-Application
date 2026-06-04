using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型操作日志 EF Core 仓储实现。
/// </summary>
public class EfCoreAiModelOperationLogRepository
    : EfCoreRepository<AuroraStruct3DDbContext, AiModelOperationLog, Guid>,
        IAiModelOperationLogRepository
{
    public EfCoreAiModelOperationLogRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<AiModelOperationLog>> GetPagedListAsync(
        Guid? aiModelId = null,
        string? filter = null,
        AiModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int skipCount = 0,
        int maxResultCount = 20,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<AiModelOperationLog> query = BuildFilteredQuery(
            context,
            aiModelId,
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
        Guid? aiModelId = null,
        string? filter = null,
        AiModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<AiModelOperationLog> query = BuildFilteredQuery(
            context,
            aiModelId,
            filter,
            operationType,
            onlyFailures,
            startTime,
            endTime
        );

        return await query.LongCountAsync(cancellationToken);
    }

    private static IQueryable<AiModelOperationLog> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        Guid? aiModelId,
        string? filter,
        AiModelOperationType? operationType,
        bool onlyFailures,
        DateTime? startTime,
        DateTime? endTime
    )
    {
        IQueryable<AiModelOperationLog> query = context.AiModelOperationLogs;

        if (aiModelId.HasValue)
        {
            query = query.Where(x => x.AiModelId == aiModelId.Value);
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
