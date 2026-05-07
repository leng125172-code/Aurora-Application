using Humanizer;
using Lion.AbpPro.TemplateManagement.TextTemplates;

namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore
{
    public static class TemplateManagementDbContextModelCreatingExtensions
    {
        public static void ConfigureTemplateManagement(this ModelBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            builder.Entity<TextTemplate>(b =>
            {
                b.ToTable(
                    TemplateManagementDbProperties.DbTablePrefix + nameof(TextTemplate).Pluralize()
                );
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("名称");
                b.Property(e => e.Code).IsRequired().HasMaxLength(128).HasComment("编码");
                b.Property(e => e.Content).IsRequired().HasMaxLength(1024).HasComment("内容");
                b.Property(e => e.CultureName).IsRequired().HasMaxLength(128).HasComment("语言");
                b.HasIndex(e => e.Code);
                b.ConfigureByConvention();
            });
        }
    }
}
