namespace AuroraCV.EntityFrameworkCore
{
    public class EntityFrameworkCoreAuroraCVDbSchemaMigrator
        : IAuroraCVDbSchemaMigrator,
            ITransientDependency
    {
        private readonly IServiceProvider _serviceProvider;

        public EntityFrameworkCoreAuroraCVDbSchemaMigrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task MigrateAsync()
        {
            /* We intentionally resolving the AuroraCVMigrationsDbContext
             * from IServiceProvider (instead of directly injecting it)
             * to properly get the connection string of the current tenant in the
             * current scope.
             */

            await _serviceProvider.GetRequiredService<AuroraCVDbContext>().Database.MigrateAsync();
        }
    }
}
