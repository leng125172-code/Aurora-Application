using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.BasicManagement.UserRefreshTokens;

public interface IUserRefreshTokenRepository : IBasicRepository<UserRefreshToken, Guid>
{
    Task<List<UserRefreshToken>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    Task<long> GetCountAsync(DateTime? startDateTime = null, DateTime? endDateTime = null);

    Task<UserRefreshToken> FindByRefreshTokenAsync(string refreshToken);
}
