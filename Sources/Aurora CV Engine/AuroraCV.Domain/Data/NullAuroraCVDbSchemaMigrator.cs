namespace AuroraCV.Data
{
    /* This is used if database provider does't define
     * IAuroraCVDbSchemaMigrator implementation.
     */
    public class NullAuroraCVDbSchemaMigrator : IAuroraCVDbSchemaMigrator, ITransientDependency
    {
        public Task MigrateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
