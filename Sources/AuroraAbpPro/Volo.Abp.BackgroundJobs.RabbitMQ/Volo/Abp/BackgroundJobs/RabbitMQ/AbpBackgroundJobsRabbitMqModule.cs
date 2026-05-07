using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.RabbitMQ;
using Volo.Abp.Threading;

namespace Volo.Abp.BackgroundJobs.RabbitMQ;

[DependsOn(
    typeof(AbpBackgroundJobsAbstractionsModule),
    typeof(AbpRabbitMqModule),
    typeof(AbpThreadingModule)
)]
public class AbpBackgroundJobsRabbitMqModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(typeof(IJobQueue<>), typeof(JobQueue<>));
    }

    public override async Task OnApplicationInitializationAsync(
        ApplicationInitializationContext context
    )
    {
        await StartJobQueueManagerAsync(context);
    }

    public override async Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        await StopJobQueueManagerAsync(context);
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        AsyncHelper.RunSync(() => OnApplicationInitializationAsync(context));
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        AsyncHelper.RunSync(() => OnApplicationShutdownAsync(context));
    }

    private static async Task StartJobQueueManagerAsync(ApplicationInitializationContext context)
    {
        await context.ServiceProvider.GetRequiredService<IJobQueueManager>().StartAsync();
    }

    private static async Task StopJobQueueManagerAsync(ApplicationShutdownContext context)
    {
        await context.ServiceProvider.GetRequiredService<IJobQueueManager>().StopAsync();
    }
}
