namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore
{
    public class CodeManagementModelBuilderConfigurationOptions
        : AbpModelBuilderConfigurationOptions
    {
        public CodeManagementModelBuilderConfigurationOptions(
            [NotNull] string tablePrefix = "",
            [CanBeNull] string schema = null
        )
            : base(tablePrefix, schema) { }
    }
}
