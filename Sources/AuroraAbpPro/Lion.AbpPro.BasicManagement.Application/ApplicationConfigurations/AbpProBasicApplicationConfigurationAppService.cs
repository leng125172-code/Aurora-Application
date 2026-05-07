using Lion.AbpPro.Oidc;
using Lion.AbpPro.Oidc.Settings;
using Microsoft.Extensions.Configuration;

namespace Lion.AbpPro.BasicManagement.ApplicationConfigurations;

public class AbpProBasicApplicationConfigurationAppService
    : ApplicationService,
        IAbpProBasicApplicationConfigurationAppService
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingProvider _settingProvider;
    private readonly AbpMultiTenancyOptions _multiTenancyOptions;
    private readonly IConfiguration _configuration;

    public AbpProBasicApplicationConfigurationAppService(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingProvider settingProvider,
        IOptions<AbpMultiTenancyOptions> multiTenancyOptions,
        IConfiguration configuration
    )
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingProvider = settingProvider;
        _configuration = configuration;
        _multiTenancyOptions = multiTenancyOptions.Value;
    }

    public async Task<AbpProApplicationConfigurationDto> GetAsync()
    {
        var result = new AbpProApplicationConfigurationDto
        {
            OidcConfiguration = await GetOidcConfigurationAsync(),
            MultiTenancy = GetMultiTenancy(),
            Encryption = GetEncryptionConfigurationConfiguration(),
        };

        return result;
    }

    protected virtual MultiTenancyInfoDto GetMultiTenancy()
    {
        return new MultiTenancyInfoDto { IsEnabled = _multiTenancyOptions.IsEnabled };
    }

    protected virtual EncryptionConfiguration GetEncryptionConfigurationConfiguration()
    {
        return new EncryptionConfiguration
        {
            Enabled = _configuration.GetValue<bool>("Encryption:Enabled"),
            PublicKey = _configuration.GetValue<string>("Encryption:PublicKey"),
        };
    }

    protected virtual async Task<ApplicationOidcConfigurationDto> GetOidcConfigurationAsync()
    {
        var settingDefinitions = (await _settingDefinitionManager.GetAllAsync()).Where(x =>
            x.Properties.Any(e => e.Key == AbpProOidcSettings.OidcSetting.Tag)
        );

        var settingValues = await _settingProvider.GetAllAsync(
            settingDefinitions.Select(x => x.Name).ToArray()
        );

        var result = new ApplicationOidcConfigurationDto();

        var enables = settingValues.Where(e =>
            e.Name.StartsWith(AbpProOidcSettings.OidcSetting.Oidc)
            && e.Name.EndsWith(AbpProOidcConsts.Enabled)
        );

        if (enables.Any(e => e?.Value?.ToLower() == "true"))
        {
            result.Enabled = true;
        }

        // 按照Properties 的 key 进行分组进行分组
        var groupedSettings = settingDefinitions
            .SelectMany(s => s.Properties.Values.Select(k => new { Setting = s, Key = k }))
            .GroupBy(x => x.Key, x => x.Setting);

        // 输出分组结果
        foreach (
            var group in groupedSettings.Where(e =>
                e.Key.ToString()!.StartsWith(AbpProOidcSettings.OidcSetting.Oidc)
            )
        )
        {
            var item = new OidcConfiguration { Type = group.Key.ToString() };

            foreach (var setting in group)
            {
                if (setting.Name.EndsWith(AbpProOidcConsts.Enabled))
                {
                    item.Enabled =
                        settingValues.FirstOrDefault(e => e.Name == setting.Name)?.Value?.ToLower()
                        == "true";
                }

                if (setting.Name.EndsWith(AbpProOidcConsts.ClientId))
                {
                    item.ClientId = settingValues
                        .FirstOrDefault(e => e.Name == setting.Name)
                        ?.Value?.ToString();
                }

                if (setting.Name.EndsWith(AbpProOidcConsts.ClientName))
                {
                    item.ClientName = settingValues
                        .FirstOrDefault(e => e.Name == setting.Name)
                        ?.Value?.ToString();
                }

                if (setting.Name.EndsWith(AbpProOidcConsts.Icon))
                {
                    item.Icon = settingValues
                        .FirstOrDefault(e => e.Name == setting.Name)
                        ?.Value?.ToString();
                }

                if (setting.Name.EndsWith(AbpProOidcConsts.AuthUri))
                {
                    item.AuthUri = settingValues
                        .FirstOrDefault(e => e.Name == setting.Name)
                        ?.Value?.ToString();
                }
            }

            result.OidcConfiguration.Add(item);
        }

        return result;
    }
}
