using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore
{
    [ConnectionStringName(DynamicMenuManagementDbProperties.ConnectionStringName)]
    public interface IDynamicMenuManagementDbContext : IEfCoreDbContext
    {
        DbSet<Menu> Menus { get; set; }
    }
}
