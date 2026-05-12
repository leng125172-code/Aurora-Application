using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Motors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Motors;

/// <summary>
/// PR 路径 EFCore 仓储实现（雷赛 iCL-RS 专用）
/// </summary>
public class EfCoreMotorPrPathRepository
    : EfCoreRepository<AuroraStruct3DDbContext, MotorPrPath, Guid>,
        IMotorPrPathRepository
{
    public EfCoreMotorPrPathRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<MotorPrPath>> GetListByAxisAsync(
        Guid motorAxisId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorPrPaths.AsNoTracking()
            .Where(p => p.MotorAxisId == motorAxisId)
            .OrderBy(p => p.PathIndex)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MotorPrPath?> FindByIndexAsync(
        Guid motorAxisId,
        int pathIndex,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorPrPaths.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.MotorAxisId == motorAxisId && p.PathIndex == pathIndex,
                cancellationToken
            );
    }
}
