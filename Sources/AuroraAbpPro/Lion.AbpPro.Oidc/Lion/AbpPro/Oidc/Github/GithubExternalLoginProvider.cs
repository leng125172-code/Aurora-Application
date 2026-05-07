using Lion.AbpPro.Oidc.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp.Json;
using Volo.Abp.Settings;

namespace Lion.AbpPro.Oidc.Github;

public class GithubExternalLoginProvider : IExternalLoginProvider
{
    private readonly ISettingProvider _settingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GithubExternalLoginProvider> _logger;
    private readonly IJsonSerializer _jsonSerializer;

    public GithubExternalLoginProvider(
        ISettingProvider settingProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<GithubExternalLoginProvider> logger,
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
        var accessTokenUrl =
            $"login/oauth/access_token?client_id={clientId}&client_secret={clientSecret}&code={code}";
        using var httpClient = await CreateHttpClientAsync();
        var response = await httpClient.GetAsync(accessTokenUrl);

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

        var headers = new Dictionary<string, string>
        {
            { "Authorization", $"token {accessToken}" },
        };
        headers.Add("User-Agent", await GetClientNameAsync());
        var getUserInfoUrl = $"user";
        using var httpClient = await CreateApiHttpClientAsync(headers);
        var response = await httpClient.GetAsync(getUserInfoUrl);

        var content = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var res = _jsonSerializer.Deserialize<GetGithubUserInfoResponse>(content);

            return new GetUserInfoResult(
                res.id.ToString(),
                res.avatar_url,
                res.name,
                res.name,
                res.email
            );
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
            AbpProOidcSettings.OidcSetting.Github.HostUrl
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

    protected virtual async Task<HttpClient> CreateApiHttpClientAsync(
        Dictionary<string, string> headers = null
    )
    {
        await Task.CompletedTask;
        var client = _httpClientFactory.CreateClient();
        var uri = new Uri("https://api.github.com/");
        client.BaseAddress = uri;
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
            AbpProOidcSettings.OidcSetting.Github.ClientId
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
            AbpProOidcSettings.OidcSetting.Github.ClientSecret
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
            AbpProOidcSettings.OidcSetting.Github.ClientName
        );
        if (clientName.IsNullOrWhiteSpace())
        {
            throw new OidcException(AbpProOidcErrorCodes.ErrorCode100006);
        }

        return clientName;
    }
}
