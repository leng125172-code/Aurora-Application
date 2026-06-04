using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型标识关联 EF Core 仓储实现。
/// </summary>
public class EfCoreAiModelIdentifierLinkRepository
    : EfCoreRepository<AuroraStruct3DDbContext, AiModelIdentifierLink, Guid>,
        IAiModelIdentifierLinkRepository
{
    public EfCoreAiModelIdentifierLinkRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<AiModelIdentifierLink>> GetListByModelIdsAsync(
        IEnumerable<Guid> aiModelIds,
        CancellationToken cancellationToken = default
    )
    {
        List<Guid> modelIdList = aiModelIds.Distinct().ToList();
        if (modelIdList.Count == 0)
        {
            return [];
        }

        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .AiModelIdentifierLinks.Where(x => modelIdList.Contains(x.AiModelId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ReplaceAsync(
        Guid aiModelId,
        IEnumerable<Guid> identifierIds,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        List<AiModelIdentifierLink> existing = await context
            .AiModelIdentifierLinks.Where(x => x.AiModelId == aiModelId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            context.AiModelIdentifierLinks.RemoveRange(existing);
        }

        List<Guid> distinctIds = identifierIds.Distinct().ToList();
        if (distinctIds.Count > 0)
        {
            List<AiModelIdentifierLink> links = distinctIds
                .Select(identifierId => new AiModelIdentifierLink(
                    Guid.NewGuid(),
                    aiModelId,
                    identifierId
                ))
                .ToList();
            await context.AiModelIdentifierLinks.AddRangeAsync(links, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteByModelIdAsync(
        Guid aiModelId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        List<AiModelIdentifierLink> existing = await context
            .AiModelIdentifierLinks.Where(x => x.AiModelId == aiModelId)
            .ToListAsync(cancellationToken);

        if (existing.Count == 0)
        {
            return;
        }

        context.AiModelIdentifierLinks.RemoveRange(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}
