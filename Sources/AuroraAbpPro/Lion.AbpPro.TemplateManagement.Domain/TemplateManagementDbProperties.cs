namespace Lion.AbpPro.TemplateManagement
{
    public static class TemplateManagementDbProperties
    {
        public static string DbTablePrefix { get; set; } = "AbpPro";

        public static string DbSchema { get; set; } = null;

        public const string ConnectionStringName = "TemplateManagement";
    }
}
