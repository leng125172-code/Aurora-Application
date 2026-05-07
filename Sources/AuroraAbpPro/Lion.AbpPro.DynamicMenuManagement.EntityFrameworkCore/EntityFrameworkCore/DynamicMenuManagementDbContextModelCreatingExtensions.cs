using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore
{
    public static class DynamicMenuManagementDbContextModelCreatingExtensions
    {
        public static void ConfigureDynamicMenuManagement(this ModelBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            builder.Entity<Menu>(b =>
            {
                b.ToTable(DynamicMenuManagementDbProperties.DbTablePrefix + "Menus");
                b.Property(e => e.Name).IsRequired().HasMaxLength(128).HasComment("唯一编码");
                b.Property(e => e.Title).IsRequired().HasMaxLength(128).HasComment("标题");
                b.Property(e => e.Icon).HasComment("图标");
                b.Property(e => e.KeepAlive).HasComment("是否缓存");
                b.Property(e => e.HideInMenu).HasComment("是否显示");
                b.Property(e => e.Order).HasComment("排序");
                b.Property(e => e.Path).IsRequired().HasMaxLength(512).HasComment("路由/接口地址");
                b.Property(e => e.MenuType).HasComment("菜单类型");
                b.Property(e => e.OpenType).HasComment("打开类型");
                b.Property(e => e.Url).HasComment("内外链地址");
                b.Property(e => e.DisplayTitle).HasMaxLength(128).HasComment("标准多语言");
                b.Property(e => e.Component).HasMaxLength(512).HasComment("组件地址");
                b.Property(e => e.Policy).HasMaxLength(128).HasComment("授权策略名称");
                b.ConfigureByConvention();
            });
        }
    }
}
