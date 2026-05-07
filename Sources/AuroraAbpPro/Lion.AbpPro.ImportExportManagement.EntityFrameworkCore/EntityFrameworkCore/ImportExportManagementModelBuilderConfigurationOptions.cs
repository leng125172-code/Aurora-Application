namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore
{
    public class ImportExportManagementModelBuilderConfigurationOptions
        : AbpModelBuilderConfigurationOptions
    {
        public ImportExportManagementModelBuilderConfigurationOptions(
            [NotNull] string tablePrefix = "",
            [CanBeNull] string schema = null
        )
            : base(tablePrefix, schema) { }
    }
}
