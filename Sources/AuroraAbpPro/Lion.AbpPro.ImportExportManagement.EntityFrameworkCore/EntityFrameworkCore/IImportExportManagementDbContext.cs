using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExportManagement.Import;

namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore
{
    [ConnectionStringName(ImportExportManagementDbProperties.ConnectionStringName)]
    public interface IImportExportManagementDbContext : IEfCoreDbContext
    {
        /* Add DbSet for each Aggregate Root here. Example:
         * DbSet<Question> Questions { get; }
         */
        public DbSet<ImportRecord> ImportRecords { get; set; }
    }
}
