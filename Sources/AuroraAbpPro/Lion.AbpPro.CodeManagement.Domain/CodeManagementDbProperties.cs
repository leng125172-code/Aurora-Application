namespace Lion.AbpPro.CodeManagement
{
    public static class CodeManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "CodeManagement";
    }
}
