using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模块服务注册扩展。
/// 一期先注册统一运行时抽象，供应用层与后续平台实现复用。
/// </summary>
public static class AiModule
{
    /// <summary>
    /// 注册 AI 基础服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddAiServices(this IServiceCollection services)
    {
        services.AddSingleton<IAiRuntimeService, DefaultAiRuntimeService>();
        services.AddSingleton<IAiModelUploadSessionManager, AiModelUploadSessionManager>();
        services.AddTransient<IAiModelConversionService, DefaultAiModelConversionService>();
        services.AddTransient<
            IAiModelPythonConversionExecutor,
            ProcessAiModelPythonConversionExecutor
        >();
        return services;
    }
}
