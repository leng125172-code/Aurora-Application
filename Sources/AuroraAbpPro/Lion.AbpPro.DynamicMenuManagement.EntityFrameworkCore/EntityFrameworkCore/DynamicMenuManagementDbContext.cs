using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore
{
    [ConnectionStringName(DynamicMenuManagementDbProperties.ConnectionStringName)]
    public class DynamicMenuManagementDbContext
        : AbpDbContext<DynamicMenuManagementDbContext>,
            IDynamicMenuManagementDbContext
    {
        /* Add DbSet for each Aggregate Root here. Example:
         * public DbSet<Question> Questions { get; set; }
         */
        public DbSet<Menu> Menus { get; set; }

        public DynamicMenuManagementDbContext(
            DbContextOptions<DynamicMenuManagementDbContext> options
        )
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureDynamicMenuManagement();
        }
    }
}
