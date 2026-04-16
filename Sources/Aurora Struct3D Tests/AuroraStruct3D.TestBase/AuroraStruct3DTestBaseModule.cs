namespace AuroraStruct3D
{
    [DependsOn(
        typeof(AbpAutofacModule),
        typeof(AbpTestBaseModule),
        typeof(AbpAuthorizationModule),
        typeof(AuroraStruct3DDomainModule)
    )]
    public class AuroraStruct3DTestBaseModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpBackgroundJobOptions>(options =>
            {
                options.IsJobExecutionEnabled = false;
            });

            context.Services.AddAlwaysAllowAuthorization();
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            SeedTestData(context);
        }

        private static void SeedTestData(ApplicationInitializationContext context)
        {
            AsyncHelper.RunSync(async () =>
            {
                using (var scope = context.ServiceProvider.CreateScope())
                {
                    await scope.ServiceProvider.GetRequiredService<IDataSeeder>().SeedAsync();
                }
            });
        }
    }
}
