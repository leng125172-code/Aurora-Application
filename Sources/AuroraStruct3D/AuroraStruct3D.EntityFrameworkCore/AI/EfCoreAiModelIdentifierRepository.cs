using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型标识 EF Core 仓储实现。
/// </summary>
public class EfCoreAiModelIdentifierRepository
    : EfCoreRepository<AuroraStruct3DDbContext, AiModelIdentifier, Guid>,
        IAiModelIdentifierRepository
{
    public EfCoreAiModelIdentifierRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<AiModelIdentifier>> GetListAsync(
        string? filter = null,
        int maxResultCount = 100,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<AiModelIdentifier> query = context.AiModelIdentifiers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x => x.Name.Contains(filter));
        }

        return await query.OrderBy(x => x.Name).Take(maxResultCount).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<AiModelIdentifier>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        List<Guid> idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModelIdentifiers.Where(x => idList.Contains(x.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<AiModelIdentifier>> GetByNamesAsync(
        IEnumerable<string> normalizedNames,
        CancellationToken cancellationToken = default
    )
    {
        List<string> nameList = normalizedNames.Distinct().ToList();
        if (nameList.Count == 0)
        {
            return [];
        }

        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModelIdentifiers.Where(x => nameList.Contains(x.NormalizedName))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default)
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();

        List<AiModelIdentifier> unusedIdentifiers = await context
            .AiModelIdentifiers.Where(identifier =>
                !context.AiModelIdentifierLinks.Any(link => link.IdentifierId == identifier.Id)
            )
            .ToListAsync(cancellationToken);

        if (unusedIdentifiers.Count == 0)
        {
            return 0;
        }

        context.AiModelIdentifiers.RemoveRange(unusedIdentifiers);
        await context.SaveChangesAsync(cancellationToken);
        return unusedIdentifiers.Count;
    }
}
