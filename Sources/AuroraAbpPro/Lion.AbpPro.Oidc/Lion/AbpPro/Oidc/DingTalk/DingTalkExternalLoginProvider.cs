using Lion.AbpPro.Core;
using Lion.AbpPro.Oidc.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp.Json;
using Volo.Abp.Settings;

namespace Lion.AbpPro.Oidc.DingTalk;

public class DingTalkExternalLoginProvider : IExternalLoginProvider
{
    private readonly ISettingProvider _settingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DingTalkExternalLoginProvider> _logger;
    private readonly IJsonSerializer _jsonSerializer;

    public DingTalkExternalLoginProvider(
        ISettingProvider settingProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<DingTalkExternalLoginProvider> logger,
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

        // 钉钉获取access_token的API
        var accessTokenUrl = "oauth2/userAccessToken";

        var requestBody = new
        {
            clientId = clientId,
            clientSecret = clientSecret,
            code = code,
            grantType = "authorization_code",
        };

        using var httpClient = await CreateHttpClientAsync();
        var jsonContent = _jsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(accessTokenUrl, content);

        var responseContent = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetDingTalkAccessTokenResponse>(responseContent);
            return new GetAccessTokenResult(res.accessToken);
        }

        _logger.LogError($"GetAccessTokenAsync returned {response.StatusCode}: {responseContent}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100005);
    }

    public virtual async Task<GetUserInfoResult> GetUserInfoAsync(string accessToken)
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(accessToken));
        }

        // 钉钉获取用户信息的API
        var getUserInfoUrl = "contact/users/me";

        var headers = new Dictionary<string, string>
        {
            { "x-acs-dingtalk-access-token", accessToken },
        };

        using var httpClient = await CreateHttpClientAsync(headers);
        var response = await httpClient.GetAsync(getUserInfoUrl);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetDingTalkUserInfoResponse>(content);

            var result = new GetUserInfoResult(
                res.openId,
                res.avatarUrl,
                res.mobile,
                res.nick,
                res.email
            )
            {
                Mobile = res.mobile,
            };
            // 钉钉可能不返回邮箱，如果没有邮箱则随机生成一个
            if (res.email.IsNullOrWhiteSpace())
            {
                result.Email = EmailHelper.GenerateRandomEmail();
            }

            return result;
        }

        _logger.LogError($"GetUserInfoAsync returned {response.StatusCode}: {content}");
        throw new OidcException(AbpProOidcErrorCodes.ErrorCode100007);
    }

    protected virtual async Task<HttpClient> CreateHttpClientAsync(
        Dictionary<string, string> headers = null
    )
    {
        var client = _httpClientFactory.CreateClient();
        var url = await _settingProvider.GetOrNullAsync(
            AbpProOidcSettings.OidcSetting.DingTalk.HostUrl
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
            AbpProOidcSettings.OidcSetting.DingTalk.ClientId
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
            AbpProOidcSettings.OidcSetting.DingTalk.ClientSecret
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
            AbpProOidcSettings.OidcSetting.DingTalk.ClientName
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
            AbpProOidcSettings.OidcSetting.DingTalk.RedirectUrl
        );
        if (redirectUrl.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100008);
        }

        return redirectUrl;
    }
}
