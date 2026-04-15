namespace AuroraCV.Settings
{
    public class AuroraCVSettingDefinitionProvider : SettingDefinitionProvider
    {
        public override void Define(ISettingDefinitionContext context)
        {
            //Define your own settings here. Example:
            //context.Add(new SettingDefinition(AuroraCVSettings.MySetting1));
            OverrideDefalutSettings(context);
        }

        /// <summary>
        /// 重写默认setting添加自定义属性
        /// </summary>
        private static void OverrideDefalutSettings(ISettingDefinitionContext context) { }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<AuroraCVResource>(name);
        }
    }
}
