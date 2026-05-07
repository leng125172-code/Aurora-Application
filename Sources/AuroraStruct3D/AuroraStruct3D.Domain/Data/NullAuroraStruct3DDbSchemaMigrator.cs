namespace AuroraStruct3D.Data
{
    /* This is used if database provider does't define
     * IAuroraStruct3DDbSchemaMigrator implementation.
     */
    public class NullAuroraStruct3DDbSchemaMigrator
        : IAuroraStruct3DDbSchemaMigrator,
            ITransientDependency
    {
        public Task MigrateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
