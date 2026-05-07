using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.Validation;

namespace Volo.Abp.ObjectExtending;

[DependsOn(typeof(AbpLocalizationAbstractionsModule), typeof(AbpValidationAbstractionsModule))]
public class AbpObjectExtendingModule : AbpModule { }
