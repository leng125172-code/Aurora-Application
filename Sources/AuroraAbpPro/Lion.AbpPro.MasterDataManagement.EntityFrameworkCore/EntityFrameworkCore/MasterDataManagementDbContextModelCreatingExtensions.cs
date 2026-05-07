namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore
{
    public static class MasterDataManagementDbContextModelCreatingExtensions
    {
        public static void ConfigureMasterDataManagement(this ModelBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));
            builder.Entity<MasterDataType>(b =>
            {
                b.ToTable(MasterDataManagementDbProperties.DbTablePrefix + "MasterDataTypes");
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("名称");
                b.Property(e => e.Code).IsRequired().HasMaxLength(128).HasComment("编码");
                b.HasIndex(e => e.Code);
                b.ConfigureByConvention();
            });

            builder.Entity<MasterData>(b =>
            {
                b.ToTable(MasterDataManagementDbProperties.DbTablePrefix + "MasterDatas");
                b.Property(e => e.MasterDataTypeId).HasComment("主数据类型Id");
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("名称");
                b.Property(e => e.Code).IsRequired().HasMaxLength(128).HasComment("编码");
                b.Property(e => e.Enabled).HasComment("是否启用");
                b.HasIndex(e => e.Code);
                b.ConfigureByConvention();
            });

            builder.Entity<MasterDataAttribute>(b =>
            {
                b.ToTable(MasterDataManagementDbProperties.DbTablePrefix + "MasterDataAttributes");
                b.Property(e => e.MasterDataTypeId).HasComment("主数据类型Id");
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("名称");
                b.Property(e => e.Code).IsRequired().HasMaxLength(128).HasComment("编码");
                b.Property(e => e.AttributeType)
                    .IsRequired()
                    .HasMaxLength(128)
                    .HasComment("属性类型(string,date,long等)");
                b.HasIndex(e => e.Code);
                b.ConfigureByConvention();
            });

            builder.Entity<MasterDataValue>(b =>
            {
                b.ToTable(MasterDataManagementDbProperties.DbTablePrefix + "MasterDataValues");
                b.Property(e => e.MasterDataId).HasComment("主数据Id(MasterData的Id)");
                b.Property(e => e.MasterDataAttributeId)
                    .HasComment("主属性属性Id(MasterDataAttribute表的主键Id)");
                b.Property(e => e.Value).IsRequired().HasMaxLength(1024).HasComment("属性值");
                b.HasIndex(e => e.MasterDataId);
                b.HasIndex(e => e.MasterDataAttributeId);
                b.ConfigureByConvention();
            });
        }
    }
}
