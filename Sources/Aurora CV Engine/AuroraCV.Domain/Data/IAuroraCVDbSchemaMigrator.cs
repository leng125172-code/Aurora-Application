namespace AuroraCV.Data
{
    public interface IAuroraCVDbSchemaMigrator
    {
        Task MigrateAsync();
    }
}
