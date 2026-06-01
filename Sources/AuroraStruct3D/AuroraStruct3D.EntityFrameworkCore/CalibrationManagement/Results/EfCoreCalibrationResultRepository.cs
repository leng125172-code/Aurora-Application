using AuroraStruct3D.CalibrationManagement;
using AuroraStruct3D.CalibrationManagement.Results;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果 EF Core 仓储实现。
/// </summary>
public class EfCoreCalibrationResultRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CalibrationResult, Guid>,
        ICalibrationResultRepository
{
    public EfCoreCalibrationResultRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<CalibrationResult?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CalibrationResults.Where(x => x.Id == id)
            .Include(x => x.Validations)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<CalibrationResult>> GetListAsync(
        Guid? projectId = null,
        Guid? deviceId = null,
        bool? isActive = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CalibrationResult> query = BuildFilteredQuery(
            context,
            projectId,
            deviceId,
            isActive
        );

        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(x => x.Version)
            : ApplySorting(query, sorting);

        return await query
            .Skip(skipCount)
            .Take(maxResultCount)
            .Include(x => x.Validations)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(
        Guid? projectId = null,
        Guid? deviceId = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await BuildFilteredQuery(context, projectId, deviceId, isActive)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetMaxVersionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CalibrationResults.Where(x => x.CalibrationProjectId == projectId)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;
    }

    /// <inheritdoc/>
    public async Task<List<CalibrationResult>> GetActiveResultsAsync(
        Guid projectId,
        Guid? excludeResultId = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CalibrationResult> query = context.CalibrationResults.Where(x =>
            x.CalibrationProjectId == projectId && x.IsActive
        );
        if (excludeResultId.HasValue)
        {
            query = query.Where(x => x.Id != excludeResultId.Value);
        }
        return await query.ToListAsync(cancellationToken);
    }

    private static IQueryable<CalibrationResult> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        Guid? projectId,
        Guid? deviceId,
        bool? isActive
    )
    {
        IQueryable<CalibrationResult> query = context.CalibrationResults;
        if (projectId.HasValue)
        {
            query = query.Where(x => x.CalibrationProjectId == projectId.Value);
        }
        if (deviceId.HasValue)
        {
            query = query.Where(x => x.CalibrationDeviceId == deviceId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }
        return query;
    }

    private static IQueryable<CalibrationResult> ApplySorting(
        IQueryable<CalibrationResult> query,
        string sorting
    )
    {
        string n = sorting.Trim().ToLowerInvariant();
        return n switch
        {
            "version" or "version asc" => query.OrderBy(x => x.Version),
            "version desc" => query.OrderByDescending(x => x.Version),
            "computedtime" or "computedtime asc" => query.OrderBy(x => x.ComputedTime),
            "computedtime desc" => query.OrderByDescending(x => x.ComputedTime),
            "overallreprojectionerror" => query.OrderBy(x => x.OverallReprojectionError),
            "overallreprojectionerror desc" => query.OrderByDescending(x =>
                x.OverallReprojectionError
            ),
            _ => query.OrderByDescending(x => x.Version),
        };
    }
}
