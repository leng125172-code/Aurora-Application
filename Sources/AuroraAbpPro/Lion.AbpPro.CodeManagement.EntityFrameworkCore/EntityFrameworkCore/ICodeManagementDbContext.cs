using Lion.AbpPro.CodeManagement.DataTypes.Aggregates;
using Lion.AbpPro.CodeManagement.EntityModels.Aggregates;
using Lion.AbpPro.CodeManagement.EnumTypes.Aggregates;
using Lion.AbpPro.CodeManagement.Projects.Aggregates;
using Lion.AbpPro.CodeManagement.Templates.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore
{
    [ConnectionStringName(CodeManagementDbProperties.ConnectionStringName)]
    public interface ICodeManagementDbContext : IEfCoreDbContext
    {
        DbSet<Template> Templates { get; set; }
        DbSet<Project> Projects { get; set; }
        DbSet<EntityModel> EntityModels { get; set; }
        DbSet<DataType> DataTypes { get; set; }
        DbSet<EnumType> EnumTypes { get; set; }
    }
}
