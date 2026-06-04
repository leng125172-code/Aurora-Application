using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型 EF Core 仓储实现。
/// </summary>
public class EfCoreAiModelRepository
    : EfCoreRepository<AuroraStruct3DDbContext, AiModel, Guid>,
        IAiModelRepository
{
    public EfCoreAiModelRepository(IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<AiModel>> GetListAsync(
        string? filter = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? creatorId = null,
        string? locationKey = null,
        string? generationCondition = null,
        AiModelConversionPreference? conversionPreference = null,
        AiModelResolvedConversionType? resolvedConversionType = null,
        AiModelFileRole? fileRole = null,
        string? fileFormat = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<AiModel> query = BuildFilteredQuery(
            context,
            filter,
            startTime,
            endTime,
            creatorId,
            locationKey,
            generationCondition,
            conversionPreference,
            resolvedConversionType,
            fileRole,
            fileFormat
        );

        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(x => x.CreationTime)
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
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? creatorId = null,
        string? locationKey = null,
        string? generationCondition = null,
        AiModelConversionPreference? conversionPreference = null,
        AiModelResolvedConversionType? resolvedConversionType = null,
        AiModelFileRole? fileRole = null,
        string? fileFormat = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<AiModel> query = BuildFilteredQuery(
            context,
            filter,
            startTime,
            endTime,
            creatorId,
            locationKey,
            generationCondition,
            conversionPreference,
            resolvedConversionType,
            fileRole,
            fileFormat
        );

        return await query.CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AiModel?> FindByMd5Async(
        string md5,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModels.Join(
                context.AiModelFiles,
                model => model.Id,
                file => file.AiModelId,
                (model, file) => new { model, file }
            )
            .Where(x => x.file.Md5 == md5)
            .Select(x => x.model)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<AiModel> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        string? filter,
        DateTime? startTime,
        DateTime? endTime,
        Guid? creatorId,
        string? locationKey,
        string? generationCondition,
        AiModelConversionPreference? conversionPreference,
        AiModelResolvedConversionType? resolvedConversionType,
        AiModelFileRole? fileRole,
        string? fileFormat
    )
    {
        IQueryable<AiModel> query = context.AiModels;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.Name.Contains(filter)
                || context.AiModelFiles.Any(file =>
                    file.AiModelId == x.Id
                    && (file.OriginalFileName.Contains(filter) || file.DisplayName.Contains(filter))
                )
                || context
                    .AiModelIdentifierLinks.Join(
                        context.AiModelIdentifiers,
                        link => link.IdentifierId,
                        identifier => identifier.Id,
                        (link, identifier) => new { link.AiModelId, identifier.Name }
                    )
                    .Any(item => item.AiModelId == x.Id && item.Name.Contains(filter))
            );
        }

        if (startTime.HasValue)
        {
            query = query.Where(x => x.CreationTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.CreationTime <= endTime.Value);
        }

        if (creatorId.HasValue)
        {
            query = query.Where(x => x.CreatorId == creatorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(locationKey))
        {
            query = query.Where(x => x.LocationKey == locationKey);
        }

        if (!string.IsNullOrWhiteSpace(generationCondition))
        {
            query = query.Where(x =>
                x.GenerationCondition != null && x.GenerationCondition.Contains(generationCondition)
            );
        }

        if (conversionPreference.HasValue)
        {
            query = query.Where(x => x.ConversionPreference == conversionPreference.Value);
        }

        if (resolvedConversionType.HasValue)
        {
            query = query.Where(x => x.ResolvedConversionType == resolvedConversionType.Value);
        }

        if (fileRole.HasValue)
        {
            query = query.Where(x =>
                context.AiModelFiles.Any(file =>
                    file.AiModelId == x.Id && file.FileRole == fileRole.Value
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(fileFormat))
        {
            query = query.Where(x =>
                context.AiModelFiles.Any(file =>
                    file.AiModelId == x.Id && file.FileFormat == fileFormat
                )
            );
        }

        return query;
    }

    private static IQueryable<AiModel> ApplySorting(IQueryable<AiModel> query, string sorting)
    {
        string[] parts = sorting.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool descending =
            parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return parts[0].ToLowerInvariant() switch
        {
            "name" => descending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            "filecount" => descending
                ? query.OrderByDescending(x => x.FileCount)
                : query.OrderBy(x => x.FileCount),
            _ => descending
                ? query.OrderByDescending(x => x.CreationTime)
                : query.OrderBy(x => x.CreationTime),
        };
    }
}

/// <summary>
/// AI 模型文件 EF Core 仓储实现。
/// </summary>
public class EfCoreAiModelFileRepository
    : EfCoreRepository<AuroraStruct3DDbContext, AiModelFile, Guid>,
        IAiModelFileRepository
{
    public EfCoreAiModelFileRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<AiModelFile>> GetListByModelIdsAsync(
        IEnumerable<Guid> aiModelIds,
        CancellationToken cancellationToken = default
    )
    {
        List<Guid> modelIds = aiModelIds.Distinct().ToList();
        if (modelIds.Count == 0)
        {
            return [];
        }

        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModelFiles.Where(x => modelIds.Contains(x.AiModelId))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreationTime)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<AiModelFile>> GetListByModelIdAsync(
        Guid aiModelId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModelFiles.Where(x => x.AiModelId == aiModelId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreationTime)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteByModelIdAsync(
        Guid aiModelId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        List<AiModelFile> files = await context
            .AiModelFiles.Where(x => x.AiModelId == aiModelId)
            .ToListAsync(cancellationToken);
        if (files.Count == 0)
        {
            return;
        }

        context.AiModelFiles.RemoveRange(files);
        await context.SaveChangesAsync(cancellationToken);
    }
}
