using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.ProductModels;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模 EF Core 仓储实现。
/// 在标准 ABP CRUD 基础上扩展多条件过滤查询。
/// </summary>
public class EfCoreProductModelRepository
    : EfCoreRepository<AuroraStruct3DDbContext, ProductModel, Guid>,
        IProductModelRepository
{
    public EfCoreProductModelRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<ProductModel>> GetListAsync(
        string? filter = null,
        ProductModelFormat? fileFormat = null,
        ProductModelConversionStatus? conversionStatus = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? uploaderUserId = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        IQueryable<ProductModel> query = BuildFilteredQuery(
            context,
            filter,
            fileFormat,
            conversionStatus,
            startTime,
            endTime,
            uploaderUserId
        );

        // 排序（默认按创建时间倒序）
        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(m => m.CreationTime)
            : ApplySorting(query, sorting);

        return await query
            .AsNoTracking()
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(
        string? filter = null,
        ProductModelFormat? fileFormat = null,
        ProductModelConversionStatus? conversionStatus = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? uploaderUserId = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        IQueryable<ProductModel> query = BuildFilteredQuery(
            context,
            filter,
            fileFormat,
            conversionStatus,
            startTime,
            endTime,
            uploaderUserId
        );

        return await query.CountAsync(cancellationToken);
    }

    // ─────────────────────────── 私有辅助方法 ───────────────────────────

    /// <summary>
    /// 构建多条件过滤查询（所有条件均为可选）。
    /// </summary>
    private static IQueryable<ProductModel> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        string? filter,
        ProductModelFormat? fileFormat,
        ProductModelConversionStatus? conversionStatus,
        DateTime? startTime,
        DateTime? endTime,
        Guid? uploaderUserId
    )
    {
        IQueryable<ProductModel> query = context.ProductModels;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(m =>
                m.Name.Contains(filter) || m.OriginalFileName.Contains(filter)
            );
        }

        if (fileFormat.HasValue)
        {
            query = query.Where(m => m.FileFormat == fileFormat.Value);
        }

        if (conversionStatus.HasValue)
        {
            query = query.Where(m => m.ConversionStatus == conversionStatus.Value);
        }

        if (startTime.HasValue)
        {
            query = query.Where(m => m.CreationTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(m => m.CreationTime <= endTime.Value);
        }

        if (uploaderUserId.HasValue)
        {
            query = query.Where(m => m.CreatorId == uploaderUserId.Value);
        }

        return query;
    }

    /// <summary>
    /// 应用排序（支持 "CreationTime desc"、"Name asc" 等格式）。
    /// </summary>
    private static IQueryable<ProductModel> ApplySorting(
        IQueryable<ProductModel> query,
        string sorting
    )
    {
        string[] parts = sorting.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool descending =
            parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return parts[0].ToLowerInvariant() switch
        {
            "name" => descending
                ? query.OrderByDescending(m => m.Name)
                : query.OrderBy(m => m.Name),
            "filesizebytes" => descending
                ? query.OrderByDescending(m => m.FileSizeBytes)
                : query.OrderBy(m => m.FileSizeBytes),
            "fileformat" => descending
                ? query.OrderByDescending(m => m.FileFormat)
                : query.OrderBy(m => m.FileFormat),
            "conversionstatus" => descending
                ? query.OrderByDescending(m => m.ConversionStatus)
                : query.OrderBy(m => m.ConversionStatus),
            _ => descending
                ? query.OrderByDescending(m => m.CreationTime)
                : query.OrderBy(m => m.CreationTime),
        };
    }
}
