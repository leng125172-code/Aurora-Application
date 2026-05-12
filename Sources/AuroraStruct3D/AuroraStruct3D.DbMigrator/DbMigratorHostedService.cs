using Volo.Abp.Data;

namespace AuroraStruct3D.DbMigrator
{
    public class DbMigratorHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfiguration _configuration;
        private readonly MigratorOptions _migratorOptions;

        public DbMigratorHostedService(
            IHostApplicationLifetime hostApplicationLifetime,
            IConfiguration configuration,
            MigratorOptions migratorOptions
        )
        {
            _hostApplicationLifetime = hostApplicationLifetime;
            _configuration = configuration;
            _migratorOptions = migratorOptions;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using (
                var application =
                    await AbpApplicationFactory.CreateAsync<AuroraStruct3DDbMigratorModule>(
                        options =>
                        {
                            options.Services.ReplaceConfiguration(_configuration);
                            options.UseAutofac();
                            options.Services.AddLogging(c => c.AddSerilog());
                            // https://github.com/abpframework/abp/pull/15208
                            options.AddDataMigrationEnvironment();
                        }
                    )
            )
            {
                await application.InitializeAsync();

                var migrationService = application
                    .ServiceProvider.GetRequiredService<AuroraStruct3DDbMigrationService>();

                // 根据运行模式选择操作
                if (_migratorOptions.Mode == MigratorMode.Rebuild)
                {
                    await migrationService.RebuildAsync();
                }
                else
                {
                    await migrationService.MigrateAsync();
                }

                await application.ShutdownAsync();

                _hostApplicationLifetime.StopApplication();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
