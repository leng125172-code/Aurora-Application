using Microsoft.Extensions.Caching.Distributed;
using OtpNet;
using QRCoder;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace Lion.AbpPro.TwoFactory;

public class TwoFactorProvider : ITwoFactorProvider, ITransientDependency
{
    private readonly IDistributedCache<TwoFactoryCacheItem> _distributedCache;
    private readonly ISettingProvider _settingProvider;

    public TwoFactorProvider(
        IDistributedCache<TwoFactoryCacheItem> distributedCache,
        ISettingProvider settingProvider
    )
    {
        _distributedCache = distributedCache;
        _settingProvider = settingProvider;
    }

    public async Task<GetQRCodeDto> GetQRCodeAsync(string userName)
    {
        var result = new GetQRCodeDto();
        var secret = Base32Encoding.ToString(OtpNet.KeyGeneration.GenerateRandomKey());
        var size = await _settingProvider.GetOrNullAsync(TwoFactorySettings.Group.QRCodeSize);
        var issuer = await _settingProvider.GetOrNullAsync(TwoFactorySettings.Group.Issuer);
        var keyUri = new OtpUri(
            OtpType.Totp,
            Base32Encoding.ToBytes(secret),
            userName,
            issuer
        ).ToString();
        var image = PngByteQRCodeHelper.GetQRCode(
            keyUri,
            QRCodeGenerator.ECCLevel.Q,
            Convert.ToInt32(size)
        );

        return new GetQRCodeDto() { QRCode = image, Secret = secret };
    }

    public async Task<bool> VerifyCodeAsync(string secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        if (
            totp.VerifyTotp(
                code,
                out var timeStepMatched,
                VerificationWindow.RfcSpecifiedNetworkDelay
            )
        )
        {
            // 我们应该对用户输入的 OTP（一次性密码）进行记录，防止重复使用
            var cache = await _distributedCache.GetAsync(
                TwoFactoryCacheItem.CalculateCacheKey(timeStepMatched)
            );
            if (cache?.IsValid == true)
            {
                return false;
            }
            else
            {
                // 在同一个时间步长窗口下，在验证成功后会写入一个时间步长匹配（timeStepMatched）数值。该数值包含了对应那次时间步长窗口的固定数值，你可以通过这个数值来判断一次性密码（OTP）是否已经验证过了。
                await _distributedCache.SetAsync(
                    TwoFactoryCacheItem.CalculateCacheKey(timeStepMatched),
                    new TwoFactoryCacheItem(),
                    new DistributedCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
                    }
                );
                return true;
            }
        }
        else
        {
            return false;
        }
    }
}
