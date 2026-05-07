namespace Lion.AbpPro.TwoFactory;

public interface ITwoFactorProvider
{
    /// <summary>
    /// 获取二维码
    /// </summary>
    /// <param name="userName">用户名(最好不要使用中文)</param>
    Task<GetQRCodeDto> GetQRCodeAsync(string userName);

    /// <summary>
    /// 验证二维码
    /// </summary>
    Task<bool> VerifyCodeAsync(string secret, string code);
}
