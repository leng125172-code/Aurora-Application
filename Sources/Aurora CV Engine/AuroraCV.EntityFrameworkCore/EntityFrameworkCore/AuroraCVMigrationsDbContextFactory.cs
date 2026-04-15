namespace AuroraCV.EntityFrameworkCore
{
    /* This class is needed for EF Core console commands
     * (like Add-Migration and Update-Database commands) */
    public class AuroraCVMigrationsDbContextFactory : IDesignTimeDbContextFactory<AuroraCVDbContext>
    {
        public AuroraCVDbContext CreateDbContext(string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            AuroraCVEfCoreEntityExtensionMappings.Configure();

            var configuration = BuildConfiguration();

            var builder = new DbContextOptionsBuilder<AuroraCVDbContext>().UseNpgsql(
                configuration.GetConnectionString("Default") ?? string.Empty
            );

            return new AuroraCVDbContext(builder.Options);
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(
                    Path.Combine(Directory.GetCurrentDirectory(), "../AuroraCV.DbMigrator/")
                )
                .AddJsonFile("appsettings.json", false);

            return builder.Build();
        }
    }
}
