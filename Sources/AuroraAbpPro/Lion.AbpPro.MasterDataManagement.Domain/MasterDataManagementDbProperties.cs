namespace Lion.AbpPro.MasterDataManagement
{
    public static class MasterDataManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "MasterDataManagement";
    }
}
