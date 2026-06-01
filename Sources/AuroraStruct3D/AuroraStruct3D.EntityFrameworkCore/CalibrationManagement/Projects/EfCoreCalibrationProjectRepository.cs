using AuroraStruct3D.CalibrationManagement;
using AuroraStruct3D.CalibrationManagement.Projects;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程 EF Core 仓储实现。
/// </summary>
public class EfCoreCalibrationProjectRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CalibrationProject, Guid>,
        ICalibrationProjectRepository
{
    public EfCoreCalibrationProjectRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<CalibrationProject?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CalibrationProjects.Where(x => x.Id == id)
            .Include(x => x.Frames)
            .ThenInclude(f => f.Images)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<CalibrationProject>> GetListAsync(
        string? filter = null,
        Guid? calibrationDeviceId = null,
        CalibrationProjectStatus? status = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CalibrationProject> query = BuildFilteredQuery(
            context,
            filter,
            calibrationDeviceId,
            status
        );

        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(x => x.CreationTime)
            : ApplySorting(query, sorting);

        return await query
            .Skip(skipCount)
            .Take(maxResultCount)
            .Include(x => x.Frames)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(
        string? filter = null,
        Guid? calibrationDeviceId = null,
        CalibrationProjectStatus? status = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await BuildFilteredQuery(context, filter, calibrationDeviceId, status)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetMaxFrameIndexAsync(
        Guid projectId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CalibrationCaptureFrames.Where(x => x.CalibrationProjectId == projectId)
            .Select(x => (int?)x.FrameIndex)
            .MaxAsync(cancellationToken) ?? 0;
    }

    private static IQueryable<CalibrationProject> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        string? filter,
        Guid? calibrationDeviceId,
        CalibrationProjectStatus? status
    )
    {
        IQueryable<CalibrationProject> query = context.CalibrationProjects;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            string keyword = filter.Trim();
            query = query.Where(x =>
                x.Name.Contains(keyword)
                || (x.Description != null && x.Description.Contains(keyword))
            );
        }
        if (calibrationDeviceId.HasValue)
        {
            query = query.Where(x => x.CalibrationDeviceId == calibrationDeviceId.Value);
        }
        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }
        return query;
    }

    private static IQueryable<CalibrationProject> ApplySorting(
        IQueryable<CalibrationProject> query,
        string sorting
    )
    {
        string n = sorting.Trim().ToLowerInvariant();
        return n switch
        {
            "name" or "name asc" => query.OrderBy(x => x.Name),
            "name desc" => query.OrderByDescending(x => x.Name),
            "status" or "status asc" => query.OrderBy(x => x.Status),
            "status desc" => query.OrderByDescending(x => x.Status),
            "creationtime" or "creationtime asc" => query.OrderBy(x => x.CreationTime),
            "creationtime desc" => query.OrderByDescending(x => x.CreationTime),
            _ => query.OrderByDescending(x => x.CreationTime),
        };
    }
}
