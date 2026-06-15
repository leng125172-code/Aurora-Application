using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Projects;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目主表 EF Core 仓储实现。
/// 在标准 ABP CRUD 基础上扩展多条件过滤查询。
/// </summary>
public class EfCoreProjectInfoRepository
    : EfCoreRepository<AuroraStruct3DDbContext, ProjectInfo, Guid>,
        IProjectInfoRepository
{
    /// <summary>
    /// 初始化 EfCoreProjectInfoRepository 实例
    /// </summary>
    public EfCoreProjectInfoRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<ProjectInfo>> GetListAsync(
        int skipCount,
        int maxResultCount,
        string? filter = null,
        ProjectStatus? status = null,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        IQueryable<ProjectInfo> query = ApplyFilter(context.Set<ProjectInfo>(), filter, status);

        // 排序（默认按创建时间倒序）
        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(p => p.CreationTime)
            : query.OrderByDescending(p => p.CreationTime); // 可扩展为动态排序

        return await query
            .AsNoTracking()
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        string? filter = null,
        ProjectStatus? status = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        return await ApplyFilter(context.Set<ProjectInfo>(), filter, status)
            .LongCountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ProjectCodeExistsAsync(
        string projectCode,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        IQueryable<ProjectInfo> query = context
            .Set<ProjectInfo>()
            .Where(p => p.ProjectCode == projectCode);

        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// 构建基础过滤查询条件
    /// </summary>
    private static IQueryable<ProjectInfo> ApplyFilter(
        IQueryable<ProjectInfo> query,
        string? filter,
        ProjectStatus? status
    )
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(p => p.ProjectCode.Contains(filter) || p.Name.Contains(filter));
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        return query;
    }
}
