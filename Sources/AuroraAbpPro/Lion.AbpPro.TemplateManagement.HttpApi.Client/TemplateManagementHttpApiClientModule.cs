namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(typeof(TemplateManagementApplicationContractsModule), typeof(AbpHttpClientModule))]
    public class TemplateManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "TemplateManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(TemplateManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
