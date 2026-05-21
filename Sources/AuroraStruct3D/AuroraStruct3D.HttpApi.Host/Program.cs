namespace AuroraStruct3D;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Console(new AuroraStruct3D.Services.TagColoredTextFormatter())
            .CreateBootstrapLogger();

        try
        {
            Log.Information("AuroraStruct3D.HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            builder
                .Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog(
                    (context, loggerConfiguration) =>
                    {
                        SerilogToEsExtensions.SetSerilogConfiguration(
                            loggerConfiguration,
                            context.Configuration
                        );
                        // 主 Serilog 使用自定义彩色控制台格式化器（替代 appsettings 中的默认 Console sink）
                        loggerConfiguration.WriteTo.Console(
                            new AuroraStruct3D.Services.TagColoredTextFormatter()
                        );
                    }
                );
            // 设置MaxRequestBodySize
            //builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 52428800);
            await builder.AddApplicationAsync<AuroraStruct3DHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
