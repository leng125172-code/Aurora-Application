namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore
{
    public class MasterDataManagementModelBuilderConfigurationOptions
        : AbpModelBuilderConfigurationOptions
    {
        public MasterDataManagementModelBuilderConfigurationOptions(
            [NotNull] string tablePrefix = "",
            [CanBeNull] string schema = null
        )
            : base(tablePrefix, schema) { }
    }
}
