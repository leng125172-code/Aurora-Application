namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore
{
    public class TemplateManagementModelBuilderConfigurationOptions
        : AbpModelBuilderConfigurationOptions
    {
        public TemplateManagementModelBuilderConfigurationOptions(
            [NotNull] string tablePrefix = "",
            [CanBeNull] string schema = null
        )
            : base(tablePrefix, schema) { }
    }
}
