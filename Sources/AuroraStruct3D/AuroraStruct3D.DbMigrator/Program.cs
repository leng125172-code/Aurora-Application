namespace AuroraStruct3D.DbMigrator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 解析命令行参数，确定运行模式
            var mode = ParseMode(args);

            // -help：显示帮助信息后退出
            if (args.Contains("-help", StringComparer.OrdinalIgnoreCase))
            {
                PrintHelp();
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
                .MinimumLevel.Override("AuroraStruct3D", LogEventLevel.Debug)
#else
                .MinimumLevel.Override("AuroraStruct3D", LogEventLevel.Information)
#endif
                .Enrich.FromLogContext()
                .WriteTo.Async(c => c.File("Logs/logs.txt"))
                .WriteTo.Async(c => c.Console())
                .CreateLogger();

            await CreateHostBuilder(args, mode).RunConsoleAsync();
        }

        /// <summary>
        /// 解析命令行参数，返回运行模式
        /// </summary>
        private static MigratorMode ParseMode(string[] args)
        {
            if (args.Contains("-rebuild", StringComparer.OrdinalIgnoreCase))
                return MigratorMode.Rebuild;

            // 无参数或 -update 均为更新模式
            return MigratorMode.Update;
        }

        /// <summary>
        /// 输出帮助及版本信息
        /// </summary>
        private static void PrintHelp()
        {
            var version =
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";

            Console.WriteLine();
            Console.WriteLine($"AuroraStruct3D.DbMigrator  v{version}");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  AuroraStruct3D.DbMigrator [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  （无参数）   更新数据库（等同于 -update）");
            Console.WriteLine("  -update      执行待执行的迁移并填充种子数据");
            Console.WriteLine("  -rebuild     删除现有数据库后重新创建并填充种子数据");
            Console.WriteLine("  -help        显示此帮助信息");
            Console.WriteLine();
        }

        public static IHostBuilder CreateHostBuilder(string[] args, MigratorMode mode) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) => logging.ClearProviders())
                .ConfigureAppConfiguration(otpions =>
                {
                    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                    var appSettingFileName = "appsettings.json";
                    if (!environment.IsNullOrWhiteSpace())
                        appSettingFileName = $"appsettings.{environment}.json";

                    otpions.AddJsonFile(appSettingFileName, optional: true);
                })
                .ConfigureServices(
                    (hostContext, services) =>
                    {
                        // 注册迁移选项，供 HostedService 使用
                        services.AddSingleton(new MigratorOptions { Mode = mode });
                        services.AddHostedService<DbMigratorHostedService>();
                    }
                );
    }
}
