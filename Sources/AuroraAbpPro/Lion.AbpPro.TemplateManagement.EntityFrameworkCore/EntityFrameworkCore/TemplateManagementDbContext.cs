using Lion.AbpPro.TemplateManagement.TextTemplates;

namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore
{
    [ConnectionStringName(TemplateManagementDbProperties.ConnectionStringName)]
    public class TemplateManagementDbContext
        : AbpDbContext<TemplateManagementDbContext>,
            ITemplateManagementDbContext
    {
        public DbSet<TextTemplate> TextTemplates { get; set; }

        public TemplateManagementDbContext(DbContextOptions<TemplateManagementDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureTemplateManagement();
        }
    }
}
