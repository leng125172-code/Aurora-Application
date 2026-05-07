namespace Lion.AbpPro.CacheManagement
{
    public static class CacheManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "CacheManagement";
    }
}
