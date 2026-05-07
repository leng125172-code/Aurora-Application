using Lion.AbpPro.Core;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;
using Volo.Abp.Settings;

namespace Lion.AbpPro.TwoFactory;

[DependsOn(typeof(AbpProCoreModule), typeof(AbpCachingModule), typeof(AbpSettingsModule))]
public class AbpProTwoFactoryModule : AbpModule { }
