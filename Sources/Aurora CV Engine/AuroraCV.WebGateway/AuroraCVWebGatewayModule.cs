namespace AuroraCV.WebGateway;

[DependsOn(typeof(AbpProAspNetCoreModule))]
public class AuroraCVWebGatewayModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpProHealthChecks();
        context.Services.AddAbpProOcelot();
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        app.UseCorrelationId();
        app.UseRouting();
        app.UseConfiguredEndpoints(endpoints =>
        {
            endpoints.MapHealthChecks("/health");
        });
        app.UseWebSockets();
        app.UseOcelot().Wait();
    }
}
