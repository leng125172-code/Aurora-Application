using Volo.Abp.Localization.External;

namespace Lion.AbpPro.BasicManagement.ApplicationConfigurations;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAbpApplicationConfigurationAppService))]
public class AbpProApplicationConfigurationAppService
    : ApplicationService,
        IAbpApplicationConfigurationAppService
{
    private readonly AbpLocalizationOptions _localizationOptions;
    private readonly AbpMultiTenancyOptions _multiTenancyOptions;
    private readonly IAbpAuthorizationPolicyProvider _abpAuthorizationPolicyProvider;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly DefaultAuthorizationPolicyProvider _defaultAuthorizationPolicyProvider;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuthorizationService _authorizationService;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly ILanguageProvider _languageProvider;
    private readonly ITimezoneProvider _timezoneProvider;
    private readonly AbpClockOptions _abpClockOptions;
    private readonly ICachedObjectExtensionsDtoService _cachedObjectExtensionsDtoService;
    private const string PermissionPrefix = "AbpTenantManagement";

    public AbpProApplicationConfigurationAppService(
        IOptions<AbpLocalizationOptions> localizationOptions,
        IOptions<AbpMultiTenancyOptions> multiTenancyOptions,
        IAbpAuthorizationPolicyProvider abpAuthorizationPolicyProvider,
        IPermissionDefinitionManager permissionDefinitionManager,
        DefaultAuthorizationPolicyProvider defaultAuthorizationPolicyProvider,
        IPermissionChecker permissionChecker,
        IAuthorizationService authorizationService,
        ICurrentUser currentUser,
        ISettingProvider settingProvider,
        ISettingDefinitionManager settingDefinitionManager,
        IFeatureDefinitionManager featureDefinitionManager,
        ILanguageProvider languageProvider,
        ITimezoneProvider timezoneProvider,
        IOptions<AbpClockOptions> abpClockOptions,
        ICachedObjectExtensionsDtoService cachedObjectExtensionsDtoService
    )
    {
        _abpAuthorizationPolicyProvider = abpAuthorizationPolicyProvider;
        _permissionDefinitionManager = permissionDefinitionManager;
        _defaultAuthorizationPolicyProvider = defaultAuthorizationPolicyProvider;
        _permissionChecker = permissionChecker;
        _authorizationService = authorizationService;
        _currentUser = currentUser;
        _settingProvider = settingProvider;
        _settingDefinitionManager = settingDefinitionManager;
        _featureDefinitionManager = featureDefinitionManager;
        _languageProvider = languageProvider;
        _timezoneProvider = timezoneProvider;
        _abpClockOptions = abpClockOptions.Value;
        _cachedObjectExtensionsDtoService = cachedObjectExtensionsDtoService;
        _localizationOptions = localizationOptions.Value;
        _multiTenancyOptions = multiTenancyOptions.Value;
    }

    public virtual async Task<ApplicationConfigurationDto> GetAsync(
        ApplicationConfigurationRequestOptions options
    )
    {
        //TODO: Optimize & cache..?

        Logger.LogDebug("Executing AbpApplicationConfigurationAppService.GetAsync()...");

        var result = new ApplicationConfigurationDto
        {
            Auth = await GetAuthConfigAsync(),
            Features = await GetFeaturesConfigAsync(),
            //Localization = await GetLocalizationConfigAsync(),
            CurrentUser = GetCurrentUser(),
            Setting = await GetSettingConfigAsync(),
            MultiTenancy = GetMultiTenancy(),
            CurrentTenant = GetCurrentTenant(),
            Timing = await GetTimingConfigAsync(),
            Clock = GetClockConfig(),
            ObjectExtensions = _cachedObjectExtensionsDtoService.Get(),
        };
        if (options.IncludeLocalizationResources)
        {
            result.Localization = await GetLocalizationConfigAsync(options);
        }

        Logger.LogDebug("Executed AbpApplicationConfigurationAppService.GetAsync().");

        return result;
    }

    protected virtual CurrentTenantDto GetCurrentTenant()
    {
        return new CurrentTenantDto()
        {
            Id = CurrentTenant.Id,
            Name = CurrentTenant.Name,
            IsAvailable = CurrentTenant.IsAvailable,
        };
    }

    protected virtual MultiTenancyInfoDto GetMultiTenancy()
    {
        return new MultiTenancyInfoDto { IsEnabled = _multiTenancyOptions.IsEnabled };
    }

    protected virtual CurrentUserDto GetCurrentUser()
    {
        return new CurrentUserDto
        {
            IsAuthenticated = _currentUser.IsAuthenticated,
            Id = _currentUser.Id,
            TenantId = _currentUser.TenantId,
            ImpersonatorUserId = _currentUser.FindImpersonatorUserId(),
            ImpersonatorTenantId = _currentUser.FindImpersonatorTenantId(),
            UserName = _currentUser.UserName,
            SurName = _currentUser.SurName,
            Name = _currentUser.Name,
            Email = _currentUser.Email,
            EmailVerified = _currentUser.EmailVerified,
            PhoneNumber = _currentUser.PhoneNumber,
            PhoneNumberVerified = _currentUser.PhoneNumberVerified,
            Roles = _currentUser.Roles,
        };
    }

    protected virtual async Task<ApplicationAuthConfigurationDto> GetAuthConfigAsync()
    {
        var authConfig = new ApplicationAuthConfigurationDto();

        var policyNames = await _abpAuthorizationPolicyProvider.GetPoliciesNamesAsync();
        var abpPolicyNames = new List<string>();
        var otherPolicyNames = new List<string>();

        foreach (var policyName in policyNames)
        {
            if (
                await _defaultAuthorizationPolicyProvider.GetPolicyAsync(policyName) == null
                && _permissionDefinitionManager.GetOrNullAsync(policyName) != null
            )
            {
                abpPolicyNames.Add(policyName);
            }
            else
            {
                otherPolicyNames.Add(policyName);
            }
        }

        foreach (var policyName in otherPolicyNames)
        {
            //authConfig.Policies[policyName] = true;

            if (await _authorizationService.IsGrantedAsync(policyName))
            {
                authConfig.GrantedPolicies[policyName] = true;
            }
        }

        var result = await _permissionChecker.IsGrantedAsync(abpPolicyNames.ToArray());

        var permissions = result.Result;
        // 过滤租户的权限
        if (!_multiTenancyOptions.IsEnabled)
        {
            permissions = permissions
                .Where(e => !e.Key.StartsWith(PermissionPrefix))
                .ToDictionary();
        }

        foreach (var (key, value) in permissions)
        {
            //authConfig.Policies[key] = true;
            if (value == PermissionGrantResult.Granted)
            {
                authConfig.GrantedPolicies[key] = true;
            }
        }

        var policies = BuildGrantedPolicies(
            authConfig.GrantedPolicies.Select(e => e.Key).ToList(),
            result
        );
        foreach (var item in policies)
        {
            if (authConfig.GrantedPolicies.Any(e => e.Key == item))
                continue;
            authConfig.GrantedPolicies.Add(item, true);
        }

        return authConfig;
    }

    private List<string> BuildGrantedPolicies(
        List<string> grantedPolicies,
        MultiplePermissionGrantResult permissions
    )
    {
        var result = new List<string>();
        foreach (var policy in grantedPolicies)
        {
            result.AddRange(GetPolicy(policy, permissions));
        }

        return result.Distinct().ToList();
    }

    /// <summary>
    /// 获取权限及其父级分组
    /// </summary>
    /// <remarks>
    /// 核心逻辑说明：
    /// 1. ABP权限格式为"分组.权限"，如 AbpIdentity.Roles
    /// 2. 第一级是分组名（如 AbpIdentity），后面是具体权限
    /// 3. 前端菜单显示控制逻辑：
    ///    - 只有当子权限（如 AbpIdentity.Roles）有权限时，才需要返回分组名（AbpIdentity）
    ///    - 如果子权限没有权限，分组名也不应该返回，前端菜单就不显示
    /// 4. 递归处理多级权限，收集所有有权限的节点及其父节点
    ///
    /// 示例场景：
    /// - 有 AbpIdentity.Roles 权限 → 返回 ["AbpIdentity", "AbpIdentity.Roles"]
    /// - 无 AbpIdentity.Roles 权限 → 返回 []（不返回 AbpIdentity 分组）
    /// </remarks>
    /// <param name="policy">当前权限字符串，如 "AbpIdentity.Roles"</param>
    /// <param name="permissions">权限检查结果集合</param>
    /// <returns>有权限的节点及其父节点列表</returns>
    private List<string> GetPolicy(string policy, MultiplePermissionGrantResult permissions)
    {
        // AbpIdentity.Roles.Create
        // AbpIdentity 按照.分割，第一级是分组，剩下的才是权限
        var result = new List<string>();
        var split = policy.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length <= 0)
            return result;
        // 1. 获取当前policy组名
        var groupName = split.First();

        // 这个情况是菜单权限
        if (split.Length == 2)
        {
            var currentPolicyValue = permissions.Result.FirstOrDefault(e => e.Key == policy);

            if (currentPolicyValue.Value != PermissionGrantResult.Granted)
            {
                return result;
            }

            result.Add(groupName);
        }
        else
        {
            var currentPolicy = string.Empty;
            for (int i = 0; i < split.Length - 1; i++)
            {
                if (i == 0)
                {
                    currentPolicy += split[i];
                }
                else
                {
                    currentPolicy += "." + split[i];
                }
            }

            if (!currentPolicy.IsNullOrWhiteSpace())
            {
                var currentPolicyValue = permissions.Result.FirstOrDefault(e =>
                    e.Key == currentPolicy
                );
                if (currentPolicyValue.Value == PermissionGrantResult.Granted)
                {
                    result.Add(currentPolicy);
                    // 获取上级code
                    var parent = currentPolicy.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (parent.Length > 1)
                    {
                        result.Add(parent.First());
                    }
                }

                result.AddRange(GetPolicy(currentPolicy, permissions));
            }
        }

        return result.Distinct().ToList();
    }

    protected virtual async Task<ApplicationLocalizationConfigurationDto> GetLocalizationConfigAsync(
        ApplicationConfigurationRequestOptions options
    )
    {
        var localizationConfig = new ApplicationLocalizationConfigurationDto();

        localizationConfig.Languages.AddRange(await _languageProvider.GetLanguagesAsync());

        if (options.IncludeLocalizationResources)
        {
            var resourceNames = _localizationOptions
                .Resources.Values.Select(x => x.ResourceName)
                .Union(
                    await LazyServiceProvider
                        .LazyGetRequiredService<IExternalLocalizationStore>()
                        .GetResourceNamesAsync()
                );

            foreach (var resourceName in resourceNames)
            {
                var dictionary = new Dictionary<string, string>();

                var localizer = await StringLocalizerFactory.CreateByResourceNameOrNullAsync(
                    resourceName
                );

                if (localizer != null)
                {
                    foreach (var localizedString in await localizer.GetAllStringsAsync())
                    {
                        dictionary[localizedString.Name] = localizedString.Value;
                    }
                }

                localizationConfig.Values[resourceName] = dictionary;
            }
        }

        localizationConfig.CurrentCulture = GetCurrentCultureInfo();

        if (_localizationOptions.DefaultResourceType != null)
        {
            localizationConfig.DefaultResourceName = LocalizationResourceNameAttribute.GetName(
                _localizationOptions.DefaultResourceType
            );
        }

        localizationConfig.LanguagesMap = _localizationOptions.LanguagesMap;
        localizationConfig.LanguageFilesMap = _localizationOptions.LanguageFilesMap;

        return localizationConfig;
    }

    private static CurrentCultureDto GetCurrentCultureInfo()
    {
        return new CurrentCultureDto
        {
            Name = CultureInfo.CurrentUICulture.Name,
            DisplayName = CultureInfo.CurrentUICulture.DisplayName,
            EnglishName = CultureInfo.CurrentUICulture.EnglishName,
            NativeName = CultureInfo.CurrentUICulture.NativeName,
            IsRightToLeft = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft,
            CultureName = CultureInfo.CurrentUICulture.TextInfo.CultureName,
            TwoLetterIsoLanguageName = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            ThreeLetterIsoLanguageName = CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName,
            DateTimeFormat = new DateTimeFormatDto
            {
                CalendarAlgorithmType =
                    CultureInfo.CurrentUICulture.DateTimeFormat.Calendar.AlgorithmType.ToString(),
                DateTimeFormatLong = CultureInfo.CurrentUICulture.DateTimeFormat.LongDatePattern,
                ShortDatePattern = CultureInfo.CurrentUICulture.DateTimeFormat.ShortDatePattern,
                FullDateTimePattern = CultureInfo
                    .CurrentUICulture
                    .DateTimeFormat
                    .FullDateTimePattern,
                DateSeparator = CultureInfo.CurrentUICulture.DateTimeFormat.DateSeparator,
                ShortTimePattern = CultureInfo.CurrentUICulture.DateTimeFormat.ShortTimePattern,
                LongTimePattern = CultureInfo.CurrentUICulture.DateTimeFormat.LongTimePattern,
            },
        };
    }

    private async Task<ApplicationSettingConfigurationDto> GetSettingConfigAsync()
    {
        var result = new ApplicationSettingConfigurationDto
        {
            Values = new Dictionary<string, string>(),
        };

        var settingDefinitions = (await _settingDefinitionManager.GetAllAsync()).Where(x =>
            x.IsVisibleToClients
        );

        var settingValues = await _settingProvider.GetAllAsync(
            settingDefinitions.Select(x => x.Name).ToArray()
        );

        foreach (var settingValue in settingValues)
        {
            result.Values[settingValue.Name] = settingValue.Value;
        }

        return result;
    }

    protected virtual async Task<ApplicationFeatureConfigurationDto> GetFeaturesConfigAsync()
    {
        var result = new ApplicationFeatureConfigurationDto();

        foreach (var featureDefinition in await _featureDefinitionManager.GetAllAsync())
        {
            if (!featureDefinition.IsVisibleToClients)
            {
                continue;
            }

            if (featureDefinition.Name == "SettingManagement.Enable")
            {
                continue;
            }

            if (featureDefinition.Name == "SettingManagement.AllowChangingEmailSettings")
            {
                continue;
            }

            result.Values[featureDefinition.Name] = await FeatureChecker.GetOrNullAsync(
                featureDefinition.Name
            );
        }

        return result;
    }

    protected virtual async Task<TimingDto> GetTimingConfigAsync()
    {
        var windowsTimeZoneId = await _settingProvider.GetOrNullAsync(TimingSettingNames.TimeZone);

        return new TimingDto
        {
            TimeZone = new Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.TimeZone
            {
                Windows = new WindowsTimeZone { TimeZoneId = windowsTimeZoneId },
                Iana = new IanaTimeZone
                {
                    TimeZoneName = windowsTimeZoneId.IsNullOrWhiteSpace()
                        ? null
                        : _timezoneProvider.WindowsToIana(windowsTimeZoneId),
                },
            },
        };
    }

    protected virtual ClockDto GetClockConfig()
    {
        return new ClockDto { Kind = Enum.GetName(typeof(DateTimeKind), _abpClockOptions.Kind) };
    }
}
