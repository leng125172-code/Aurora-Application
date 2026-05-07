namespace AuroraStruct3D.EntityFrameworkCore
{
    /* This class is needed for EF Core console commands
     * (like Add-Migration and Update-Database commands) */
    public class AuroraStruct3DMigrationsDbContextFactory
        : IDesignTimeDbContextFactory<AuroraStruct3DDbContext>
    {
        public AuroraStruct3DDbContext CreateDbContext(string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            AuroraStruct3DEfCoreEntityExtensionMappings.Configure();

            var configuration = BuildConfiguration();

            var builder = new DbContextOptionsBuilder<AuroraStruct3DDbContext>().UseNpgsql(
                configuration.GetConnectionString("Default") ?? string.Empty
            );

            return new AuroraStruct3DDbContext(builder.Options);
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(
                    Path.Combine(Directory.GetCurrentDirectory(), "../AuroraStruct3D.DbMigrator/")
                )
                .AddJsonFile("appsettings.json", false);

            return builder.Build();
        }
    }
}
