using Lion.AbpPro.Core;
using Lion.AbpPro.Oidc.Github;
using Lion.AbpPro.Oidc.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp.Json;
using Volo.Abp.Settings;

namespace Lion.AbpPro.Oidc.Gitee;

public class GiteeExternalLoginProvider : IExternalLoginProvider
{
    private readonly ISettingProvider _settingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GiteeExternalLoginProvider> _logger;
    private readonly IJsonSerializer _jsonSerializer;

    public GiteeExternalLoginProvider(
        ISettingProvider settingProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<GiteeExternalLoginProvider> logger,
        IJsonSerializer jsonSerializer
    )
    {
        _settingProvider = settingProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _jsonSerializer = jsonSerializer;
    }

    public virtual async Task<GetAccessTokenResult> GetAccessTokenAsync(string code)
    {
        if (code.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(code));
        }

        var clientId = await GetClientIdAsync();
        var clientSecret = await GetClientSecretAsync();
        var redirectUrl = await GetRedirectUrlAsync();

        // https://gitee.com/oauth/token?grant_type=authorization_code&code={code}&client_id={client_id}&redirect_uri={redirect_uri}&client_secret={client_secret}
        var accessTokenUrl =
            $"oauth/token?grant_type=authorization_code&code={code}&client_id={clientId}&redirect_uri={redirectUrl}&client_secret={clientSecret}";
        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.PostAsync(accessTokenUrl, null);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetGithubAccessTokenResponse>(content);
            return new GetAccessTokenResult(res.access_token);
        }

        _logger.LogError($"GetAccessTokenAsync returned {response.StatusCode}: {content}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100005);
    }

    public virtual async Task<GetUserInfoResult> GetUserInfoAsync(string accessToken)
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(accessToken));
        }

        var getUserInfoUrl = $"api/v5/user?access_token={accessToken}";
        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.GetAsync(getUserInfoUrl);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetGithubUserInfoResponse>(content);

            var result = new GetUserInfoResult(
                res.id.ToString(),
                res.avatar_url,
                res.name,
                res.name,
                res.email
            );
            // gitee 邮箱不一定能获取到，需要用户开发
            // 没有获取到邮箱随机生成一个
            if (res.email.IsNullOrWhiteSpace())
            {
                result.Email = EmailHelper.GenerateRandomEmail();
            }

            return result;
        }

        _logger.LogError($"GetAccessTokenAsync returned {response.StatusCode}: {content}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
    }

    protected virtual async Task<HttpClient> CreateHttpClientAsync(
        Dictionary<string, string> headers = null
    )
    {
        var client = _httpClientFactory.CreateClient();
        var url = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.Gitee.HostUrl
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

        client.DefaultRequestHeaders.Add("Accept", $"application/json");
        if (headers == null || headers.Count <= 0)
            return client;
        foreach (var item in headers)
        {
            client.DefaultRequestHeaders.Add(item.Key, item.Value);
        }

        return client;
    }

    protected virtual async Task<string> GetClientIdAsync()
    {
        var clientId = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.Gitee.ClientId
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
            AbpProOidcSettings.OidcSetting.Gitee.ClientSecret
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
            AbpProOidcSettings.OidcSetting.Gitee.ClientName
        );
        if (clientName.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100006);
        }

        return clientName;
    }

    protected virtual async Task<string> GetRedirectUrlAsync()
    {
        var clientName = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.Gitee.RedirectUrl
        );
        if (clientName.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100008);
        }

        return clientName;
    }
}
