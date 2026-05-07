namespace Lion.AbpPro.ImportExportManagement
{
    public static class ImportExportManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "ImportExportManagement";
    }
}
