using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuroraStruct3D.Plcs;

public static class PlcModule
{
    public static IServiceCollection AddPlcCommunication(this IServiceCollection services)
    {
        services.AddSingleton<OpcUaPlcDriver>();
        services.AddSingleton<IPlcDriver>(sp => sp.GetRequiredService<OpcUaPlcDriver>());
        services.AddSingleton<IPlcDriverRegistry, PlcDriverRegistry>();
        services.AddSingleton<IPlcConnectionManager, PlcConnectionManager>();
        return services;
    }
}
