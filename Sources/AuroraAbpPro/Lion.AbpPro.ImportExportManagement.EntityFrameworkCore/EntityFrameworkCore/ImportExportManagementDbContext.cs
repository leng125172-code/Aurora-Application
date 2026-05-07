using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExportManagement.Import;

namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore
{
    [ConnectionStringName(ImportExportManagementDbProperties.ConnectionStringName)]
    public class ImportExportManagementDbContext
        : AbpDbContext<ImportExportManagementDbContext>,
            IImportExportManagementDbContext
    {
        /* Add DbSet for each Aggregate Root here. Example:
         * public DbSet<Question> Questions { get; set; }
         */
        public DbSet<ImportRecord> ImportRecords { get; set; }

        public ImportExportManagementDbContext(
            DbContextOptions<ImportExportManagementDbContext> options
        )
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureImportExportManagement();
        }
    }
}
