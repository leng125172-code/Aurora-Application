namespace AuroraStruct3D.EntityFrameworkCore
{
    public class EntityFrameworkCoreAuroraStruct3DDbSchemaMigrator
        : IAuroraStruct3DDbSchemaMigrator, ITransientDependency
    {
        private readonly IServiceProvider _serviceProvider;

        public EntityFrameworkCoreAuroraStruct3DDbSchemaMigrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task MigrateAsync()
        {
            /* We intentionally resolving the AuroraStruct3DMigrationsDbContext
             * from IServiceProvider (instead of directly injecting it)
             * to properly get the connection string of the current tenant in the
             * current scope.
             */

            await _serviceProvider
                .GetRequiredService<AuroraStruct3DDbContext>()
                .Database
                .MigrateAsync();
        }
    }
}