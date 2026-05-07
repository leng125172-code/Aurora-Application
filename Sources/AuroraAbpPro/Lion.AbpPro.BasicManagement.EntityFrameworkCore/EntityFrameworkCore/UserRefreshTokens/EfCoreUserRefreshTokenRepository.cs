using Lion.AbpPro.BasicManagement.UserRefreshTokens;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;

namespace Lion.AbpPro.BasicManagement.EntityFrameworkCore.UserRefreshTokens;

/// <summary>
/// 用户token 仓储Ef core 实现
/// </summary>
public class EfCoreUserRefreshTokenRepository
    : EfCoreRepository<IBasicManagementDbContext, UserRefreshToken, Guid>,
        IUserRefreshTokenRepository
{
    public EfCoreUserRefreshTokenRepository(
        IDbContextProvider<IBasicManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<UserRefreshToken>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .OrderByDescending(e => e.CreationTime)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .CountAsync();
    }

    public async Task<UserRefreshToken> FindByRefreshTokenAsync(string refreshToken)
    {
        return await (await GetDbSetAsync()).FirstOrDefaultAsync(e =>
            e.RefreshToken == refreshToken
        );
    }
}
