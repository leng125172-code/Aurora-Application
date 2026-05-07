using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Lion.AbpPro.BasicManagement.UserRefreshTokens;

/// <summary>
/// 用户token
/// </summary>
public class UserRefreshToken : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private UserRefreshToken() { }

    public UserRefreshToken(
        Guid id,
        string refreshToken,
        Guid userId,
        bool isUsed,
        DateTime expirationTime,
        string token,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetRefreshToken(refreshToken);
        SetUserId(userId);
        SetIsUsed(isUsed);
        SetExpirationTime(expirationTime);
        SetToken(token);
        TenantId = tenantId;
    }

    /// <summary>
    /// 用户id
    /// </summary>
    public Guid UserId { get; private set; }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 刷新token
    /// </summary>
    public string RefreshToken { get; private set; }

    /// <summary>
    /// Token
    /// </summary>
    public string Token { get; private set; }

    /// <summary>
    /// 是否使用
    /// </summary>
    public bool IsUsed { get; private set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpirationTime { get; private set; }

    /// <summary>
    /// 设置刷新token
    /// </summary>
    private void SetRefreshToken(string refreshToken)
    {
        Guard.NotNullOrWhiteSpace(refreshToken, nameof(refreshToken), 128, 0);
        RefreshToken = refreshToken;
    }

    /// <summary>
    /// 设置用户id
    /// </summary>
    private void SetUserId(Guid userId)
    {
        UserId = userId;
    }

    /// <summary>
    /// 设置是否使用
    /// </summary>
    public void SetIsUsed(bool isUsed)
    {
        IsUsed = isUsed;
    }

    /// <summary>
    /// 设置过期时间
    /// </summary>
    private void SetExpirationTime(DateTime expirationTime)
    {
        ExpirationTime = expirationTime;
    }

    /// <summary>
    /// 设置Token
    /// </summary>
    private void SetToken(string token)
    {
        Guard.NotNullOrWhiteSpace(token, nameof(token), 1024, 0);
        Token = token;
    }

    /// <summary>
    /// 更新用户token
    /// </summary>
    public void Update(
        string refreshToken,
        Guid userId,
        bool isUsed,
        DateTime expirationTime,
        string token
    )
    {
        SetRefreshToken(refreshToken);
        SetUserId(userId);
        SetIsUsed(isUsed);
        SetExpirationTime(expirationTime);
        SetToken(token);
    }
}
