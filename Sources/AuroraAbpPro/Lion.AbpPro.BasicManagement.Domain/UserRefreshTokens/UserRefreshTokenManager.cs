using Lion.AbpPro.UserRefreshTokens;
using Mapster;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.ObjectMapping;

namespace Lion.AbpPro.BasicManagement.UserRefreshTokens;

public class UserRefreshTokenManager : DomainService
{
    private readonly IUserRefreshTokenRepository _userRefreshTokenRepository;
    private readonly ICurrentTenant _currentTenant;

    public UserRefreshTokenManager(
        IUserRefreshTokenRepository userRefreshTokenRepository,
        ICurrentTenant currentTenant
    )
    {
        _userRefreshTokenRepository = userRefreshTokenRepository;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 创建用户token
    /// </summary>
    public async Task<UserRefreshTokenDto> CreateAsync(
        Guid id,
        string refreshToken,
        Guid userId,
        bool isUsed,
        DateTime expirationTime,
        string token
    )
    {
        var entity = new UserRefreshToken(
            id,
            refreshToken,
            userId,
            isUsed,
            expirationTime,
            token,
            _currentTenant.Id
        );
        entity = await _userRefreshTokenRepository.InsertAsync(entity);
        return entity.Adapt<UserRefreshTokenDto>();
    }

    public async Task SetUseAsync(string refreshToken)
    {
        var entity = await _userRefreshTokenRepository.FindByRefreshTokenAsync(refreshToken);
        if (entity == null)
            throw new UserFriendlyException($"用户token不存在");
        entity.SetIsUsed(true);
        await _userRefreshTokenRepository.UpdateAsync(entity);
    }

    public async Task<UserRefreshTokenDto> FindByRefreshTokenAsync(string refreshToken)
    {
        var entity = await _userRefreshTokenRepository.FindByRefreshTokenAsync(refreshToken);
        if (entity == null)
            throw new UserFriendlyException($"用户token不存在");
        return entity.Adapt<UserRefreshTokenDto>();
    }

    /// <summary>
    /// 删除用户token
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _userRefreshTokenRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"用户token不存在");
        await _userRefreshTokenRepository.DeleteAsync(entity);
    }
}
