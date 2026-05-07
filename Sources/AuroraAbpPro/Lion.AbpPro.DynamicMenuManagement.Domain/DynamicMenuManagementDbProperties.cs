namespace Lion.AbpPro.DynamicMenuManagement
{
    public static class DynamicMenuManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "DynamicMenuManagement";
    }
}
