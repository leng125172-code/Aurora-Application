namespace Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore
{
    public class DynamicMenuManagementModelBuilderConfigurationOptions
        : AbpModelBuilderConfigurationOptions
    {
        public DynamicMenuManagementModelBuilderConfigurationOptions(
            [NotNull] string tablePrefix = "",
            [CanBeNull] string schema = null
        )
            : base(tablePrefix, schema) { }
    }
}
