namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore
{
    [ConnectionStringName(MasterDataManagementDbProperties.ConnectionStringName)]
    public class MasterDataManagementDbContext
        : AbpDbContext<MasterDataManagementDbContext>,
            IMasterDataManagementDbContext
    {
        /// <summary>
        /// 主数据属性 DbSet
        /// </summary>
        public DbSet<MasterDataAttribute> MasterDataAttributes { get; set; }

        /// <summary>
        /// 主数据 DbSet
        /// </summary>
        public DbSet<MasterData> MasterDatas { get; set; }

        /// <summary>
        /// 主数据类型 DbSet
        /// </summary>
        public DbSet<MasterDataType> MasterDataTypes { get; set; }

        /// <summary>
        /// 主数据值 DbSet
        /// </summary>
        public DbSet<MasterDataValue> MasterDataValues { get; set; }

        public MasterDataManagementDbContext(
            DbContextOptions<MasterDataManagementDbContext> options
        )
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureMasterDataManagement();
        }
    }
}
