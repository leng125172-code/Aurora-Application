using Lion.AbpPro.CodeManagement.DataTypes.Aggregates;
using Lion.AbpPro.CodeManagement.EntityModels.Aggregates;
using Lion.AbpPro.CodeManagement.EnumTypes.Aggregates;
using Lion.AbpPro.CodeManagement.Projects.Aggregates;
using Lion.AbpPro.CodeManagement.Templates.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore
{
    [ConnectionStringName(CodeManagementDbProperties.ConnectionStringName)]
    public class CodeManagementDbContext
        : AbpDbContext<CodeManagementDbContext>,
            ICodeManagementDbContext
    {
        public DbSet<Template> Templates { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<EntityModel> EntityModels { get; set; }
        public DbSet<DataType> DataTypes { get; set; }
        public DbSet<EnumType> EnumTypes { get; set; }

        public CodeManagementDbContext(DbContextOptions<CodeManagementDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureCodeManagement();
        }
    }
}
