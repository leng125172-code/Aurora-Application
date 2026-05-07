using System.Text;
using System.Text.Json;
using Lion.AbpPro.Core;
using Lion.AbpPro.Oidc.Settings;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp.Caching;
using Volo.Abp.Json;
using Volo.Abp.Settings;

namespace Lion.AbpPro.Oidc.WorkWechat;

public class WorkWechatExternalLoginProvider : IExternalLoginProvider
{
    private readonly ISettingProvider _settingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkWechatExternalLoginProvider> _logger;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IDistributedCache<WorkWechatAccessTokenCacheItem> _distributedCache;

    public WorkWechatExternalLoginProvider(
        ISettingProvider settingProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<WorkWechatExternalLoginProvider> logger,
        IJsonSerializer jsonSerializer,
        IDistributedCache<WorkWechatAccessTokenCacheItem> distributedCache
    )
    {
        _settingProvider = settingProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _jsonSerializer = jsonSerializer;
        _distributedCache = distributedCache;
    }

    /// <summary>
    /// 获取企业微信 access_token
    /// https://developer.work.weixin.qq.com/document/path/91039
    /// </summary>
    public virtual async Task<GetAccessTokenResult> GetAccessTokenAsync(string code)
    {
        if (code.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(code));
        }

        var corpId = await GetClientIdAsync(); // 企业ID
        var corpSecret = await GetClientSecretAsync(); // 应用Secret

        var accessToken = await _distributedCache.GetAsync(
            WorkWechatAccessTokenCacheItem.CalculateCacheKey(corpId)
        );
        if (accessToken != null)
        {
            return new GetAccessTokenResult(accessToken + "|" + code);
        }

        // 企业微信获取access_token的API
        var accessTokenUrl = $"cgi-bin/gettoken?corpid={corpId}&corpsecret={corpSecret}";

        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.GetAsync(accessTokenUrl);

        var responseContent = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetWorkWechatAccessTokenResponse>(
                responseContent
            );
            if (res.errcode == 0)
            {
                // 获取成功 把 code 拼接出去
                await _distributedCache.SetAsync(
                    WorkWechatAccessTokenCacheItem.CalculateCacheKey(corpId),
                    new WorkWechatAccessTokenCacheItem() { AccessToken = res.access_token },
                    new DistributedCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(
                            res.expires_in - 1200
                        ),
                    }
                );
                return new GetAccessTokenResult(res.access_token + "|" + code);
            }

            _logger.LogError(
                $"GetAccessTokenAsync failed with errcode {res.errcode}: {res.errmsg}"
            );
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100005);
        }

        _logger.LogError($"GetAccessTokenAsync returned {response.StatusCode}: {responseContent}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100005);
    }

    public virtual async Task<GetUserInfoResult> GetUserInfoAsync(string accessToken)
    {
        // 获取token 和code
        var token = accessToken.Split("|")[0];
        var code = accessToken.Split("|")[1];
        var res = await GetWorkWechatUserInfoAsync(token, code);
        var userInfo = await GetWorkWechatUserDetailAsync(token, res.UserId);
        return userInfo;
    }

    /// <summary>
    /// 获取用户基本信息（通过code获取userId）
    /// </summary>
    public virtual async Task<GetWorkWechatUserInfoResponse> GetWorkWechatUserInfoAsync(
        string accessToken,
        string code
    )
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(accessToken));
        }

        if (code.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(code));
        }

        // 企业微信获取用户基本信息的API
        var getUserInfoUrl = $"cgi-bin/auth/getuserinfo?access_token={accessToken}&code={code}";

        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.GetAsync(getUserInfoUrl);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetWorkWechatUserInfoResponse>(content);
            if (res.errcode == 0)
            {
                return res;
            }

            _logger.LogError(
                $"GetWorkWechatUserInfoAsync failed with errcode {res.errcode}: {res.errmsg}"
            );
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
        }

        _logger.LogError($"GetWorkWechatUserInfoAsync returned {response.StatusCode}: {content}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
    }

    /// <summary>
    /// 获取用户详细信息
    /// </summary>
    public virtual async Task<GetUserInfoResult> GetWorkWechatUserDetailAsync(
        string accessToken,
        string userId
    )
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(accessToken));
        }

        if (userId.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
        }

        // 企业微信获取用户详细信息的API
        var getUserDetailUrl = $"/cgi-bin/user/get?access_token={accessToken}&userid={userId}";

        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.GetAsync(getUserDetailUrl);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetWorkWechatUserDetailResponse>(content);
            if (res.errcode == 0)
            {
                var result = new GetUserInfoResult(
                    userId,
                    res.avatar,
                    res.mobile,
                    res.name,
                    res.email
                )
                {
                    Mobile = res.mobile,
                };

                // 企业微信可能不返回邮箱，如果没有邮箱则随机生成一个
                if (res.email.IsNullOrWhiteSpace())
                {
                    result.Email = EmailHelper.GenerateRandomEmail();
                }

                return result;
            }

            _logger.LogError(
                $"GetWorkWechatUserDetailAsync failed with errcode {res.errcode}: {res.errmsg}"
            );
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
        }

        _logger.LogError($"GetWorkWechatUserDetailAsync returned {response.StatusCode}: {content}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
    }

    protected virtual async Task<HttpClient> CreateHttpClientAsync(
        Dictionary<string, string> headers = null
    )
    {
        var client = _httpClientFactory.CreateClient();
        var url = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.HostUrl
        );
        if (url.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100001);
        }

        try
        {
            var uri = new Uri(url.TrimEnd('/') + "/");
            client.BaseAddress = uri;
        }
        catch
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100002);
        }

        client.DefaultRequestHeaders.Add("Accept", "application/json");
        if (headers != null && headers.Count > 0)
        {
            foreach (var item in headers)
            {
                client.DefaultRequestHeaders.Add(item.Key, item.Value);
            }
        }

        return client;
    }

    protected virtual async Task<string> GetClientIdAsync()
    {
        var clientId = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.ClientId
        );
        if (clientId.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100003);
        }

        return clientId;
    }

    protected virtual async Task<string> GetClientSecretAsync()
    {
        var clientSecret = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.ClientSecret
        );
        if (clientSecret.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100004);
        }

        return clientSecret;
    }

    protected virtual async Task<string> GetClientNameAsync()
    {
        var clientName = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.ClientName
        );
        if (clientName.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100006);
        }

        return clientName;
    }

    protected virtual async Task<string> GetRedirectUrlAsync()
    {
        var redirectUrl = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.RedirectUrl
        );
        if (redirectUrl.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100008);
        }

        return redirectUrl;
    }

    /// <summary>
    /// 获取AgentId
    /// </summary>
    protected virtual async Task<string> GetAgentIdAsync()
    {
        var agentId = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.WorkWechat.AgentId
        );
        if (agentId.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100004);
        }

        return agentId;
    }
}
