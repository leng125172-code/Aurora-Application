using Lion.AbpPro.ImportExport.Import;

namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore
{
    public static class ImportExportManagementDbContextModelCreatingExtensions
    {
        public static void ConfigureImportExportManagement(this ModelBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            builder.Entity<ImportRecord>(b =>
            {
                b.ToTable(ImportExportManagementDbProperties.DbTablePrefix + "ImportRecords");
                b.Property(e => e.Contributor)
                    .IsRequired()
                    .HasMaxLength(128)
                    .HasComment("导入贡献者");
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("导入贡献者名称");
                b.Property(e => e.BlobName).IsRequired().HasMaxLength(256).HasComment("文件名称");
                b.Property(e => e.CultureName).IsRequired().HasMaxLength(36).HasComment("多语言");
                b.Property(e => e.Remark).HasMaxLength(2048).HasComment("备注");
                b.Property(e => e.Status).HasComment("状态");
                b.ConfigureByConvention();
            });
        }
    }
}
