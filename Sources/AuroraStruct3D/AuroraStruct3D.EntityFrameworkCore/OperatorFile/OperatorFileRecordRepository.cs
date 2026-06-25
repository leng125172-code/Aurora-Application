using AuroraStruct3D.OperatorFile;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 算子文件记录仓储 EfCore 实现。
/// </summary>
public class OperatorFileRecordRepository
    : EfCoreRepository<AuroraStruct3DDbContext, OperatorFileRecord, Guid>,
        IOperatorFileRecordRepository
{
    public OperatorFileRecordRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<OperatorFileRecord>> GetExpiredUnusedAsync(
        DateTime now,
        CancellationToken cancellationToken = default
    )
    {
        DbSet<OperatorFileRecord> dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(r => !r.IsUsed)
            .Where(r => r.ExpiresAt.HasValue && r.ExpiresAt.Value < now)
            .ToListAsync(cancellationToken);
    }
}
