using Lion.AbpPro.Core;
using Lion.AbpPro.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace Lion.AbpPro.TwoFactory
{
    public class TwoFactorSettingDefinitionProvider : SettingDefinitionProvider
    {
        public override void Define(ISettingDefinitionContext context)
        {
            context.Add(
                new SettingDefinition(
                    TwoFactorySettings.Group.Issuer,
                    "Lion.AbpPro",
                    L("Lion.AbpPro:TwoFactory.Issuer"),
                    L("Lion.AbpPro:TwoFactory.Issuer")
                )
                    .WithProperty(
                        TwoFactorySettings.Group.Default,
                        TwoFactorySettings.Group.TwoFactory
                    )
                    .WithProperty(
                        AbpProSettingConsts.ControlType.Default,
                        AbpProSettingConsts.ControlType.TypeText
                    )
            );

            context.Add(
                new SettingDefinition(
                    TwoFactorySettings.Group.QRCodeSize,
                    "3",
                    L("Lion.AbpPro:TwoFactory.QRCodeSize"),
                    L("Lion.AbpPro:TwoFactory.QRCodeSize")
                )
                    .WithProperty(
                        TwoFactorySettings.Group.Default,
                        TwoFactorySettings.Group.TwoFactory
                    )
                    .WithProperty(
                        AbpProSettingConsts.ControlType.Default,
                        AbpProSettingConsts.ControlType.Number
                    )
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<AbpProLocalizationResource>(name);
        }
    }
}
