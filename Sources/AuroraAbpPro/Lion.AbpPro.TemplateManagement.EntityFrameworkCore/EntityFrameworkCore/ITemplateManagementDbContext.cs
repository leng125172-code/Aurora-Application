using Lion.AbpPro.TemplateManagement.TextTemplates;

namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore
{
    [ConnectionStringName(TemplateManagementDbProperties.ConnectionStringName)]
    public interface ITemplateManagementDbContext : IEfCoreDbContext
    {
        DbSet<TextTemplate> TextTemplates { get; set; }
    }
}
