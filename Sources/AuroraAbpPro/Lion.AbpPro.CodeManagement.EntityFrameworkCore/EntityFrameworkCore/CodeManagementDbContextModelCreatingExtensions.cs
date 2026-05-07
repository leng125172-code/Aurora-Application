using Humanizer;
using Lion.AbpPro.CodeManagement.DataTypes.Aggregates;
using Lion.AbpPro.CodeManagement.EntityModels.Aggregates;
using Lion.AbpPro.CodeManagement.EnumTypes.Aggregates;
using Lion.AbpPro.CodeManagement.Projects.Aggregates;
using Lion.AbpPro.CodeManagement.Templates.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore
{
    public static class CodeManagementDbContextModelCreatingExtensions
    {
        public static void ConfigureCodeManagement(this ModelBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            builder.Entity<Template>(b =>
            {
                b.ToTable(CodeManagementDbProperties.DbTablePrefix + nameof(Template).Pluralize());

                // 租户id
                b.Property(e => e.TenantId).HasComment("租户id");

                // 模板名称 - 必填，最大长度128
                b.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("模板名称");

                // 备注 - 最大长度512
                b.Property(e => e.Remark)
                    .HasMaxLength(CodeManagementConsts.MaxLength512)
                    .HasComment("备注");

                // 模板明细集合 - 一对多关系
                b.HasMany(e => e.TemplateDetails)
                    .WithOne()
                    .HasForeignKey(td => td.TemplateId)
                    .HasConstraintName("FK_TemplateDetail_TemplateId")
                    .IsRequired();

                b.ConfigureByConvention();
            });
            builder.Entity<TemplateDetail>(b =>
            {
                b.ToTable(
                    CodeManagementDbProperties.DbTablePrefix + nameof(TemplateDetail).Pluralize()
                );

                // 模板id - 必填
                b.Property(e => e.TemplateId).IsRequired().HasComment("模板id");

                // 模板类型 - 必填
                b.Property(e => e.TemplateType).IsRequired().HasComment("模板类型");

                // 模板控制类型
                b.Property(e => e.ControlType).HasComment("模板控制类型");

                // 父级id
                b.Property(e => e.ParentId).HasComment("父级id");

                // 模板名称 - 必填，最大长度128
                b.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("模板名称");

                // 描述 - 必填，最大长度128
                b.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                // 模板内容
                b.Property(e => e.Content).HasComment("模板内容");

                b.ConfigureByConvention();
            });

            builder.Entity<Project>(b =>
            {
                b.ToTable(CodeManagementDbProperties.DbTablePrefix + nameof(Project).Pluralize());

                // 租户id
                b.Property(e => e.TenantId).HasComment("租户id");

                // 负责人 - 最大长度128
                b.Property(e => e.Owner)
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("负责人");

                // 公司名称 - 必填，最大长度128
                b.Property(e => e.CompanyName)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("公司名称");

                // 项目名称 - 必填，最大长度128
                b.Property(e => e.ProjectName)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("项目名称");

                // 命名空间 - 必填，最大长度128
                b.Property(e => e.NameSpace)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("命名空间");

                // 备注 - 最大长度512
                b.Property(e => e.Remark)
                    .HasMaxLength(CodeManagementConsts.MaxLength512)
                    .HasComment("备注");

                // 是否支持多租户 - 必填
                b.Property(e => e.SupportTenant).IsRequired().HasComment("是否支持多租户");

                b.ConfigureByConvention();
            });

            builder.Entity<EntityModel>(b =>
            {
                b.ToTable(
                    CodeManagementDbProperties.DbTablePrefix + nameof(EntityModel).Pluralize()
                );

                // 租户id
                b.Property(e => e.TenantId).HasComment("租户id");

                // 项目id - 必填
                b.Property(e => e.ProjectId).IsRequired().HasComment("项目id");

                // 编码 - 必填，最大长度128，建立索引
                b.Property(e => e.Code)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("编码");
                b.HasIndex(e => e.Code);

                // 描述 - 必填，最大长度128
                b.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                // 实体关系
                b.Property(e => e.RelationalType).HasComment("实体关系");

                // 父类Id
                b.Property(e => e.ParentId).HasComment("父类Id");

                // 聚合根Id - 必填
                b.Property(e => e.AggregateId).IsRequired().HasComment("聚合根Id");

                // 实体模型属性集合 - 一对多关系
                b.HasMany(e => e.EntityModelProperties)
                    .WithOne()
                    .HasForeignKey(emp => emp.EntityModelId)
                    .HasConstraintName("FK_EntityModelProperty_EntityModelId")
                    .IsRequired();

                b.ConfigureByConvention();
            });

            builder.Entity<EntityModelProperty>(b =>
            {
                b.ToTable(
                    CodeManagementDbProperties.DbTablePrefix
                        + nameof(EntityModelProperty).Pluralize()
                );

                // 属性编码 - 必填，最大长度128
                b.Property(e => e.Code)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("属性编码");

                // 描述 - 必填，最大长度128
                b.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                // 必填 - 必填
                b.Property(e => e.IsRequired).IsRequired().HasComment("必填");

                // 字符串最大长度
                b.Property(e => e.MaxLength).HasComment("字符串最大长度");

                // 字符串最小长度
                b.Property(e => e.MinLength).HasComment("字符串最小长度");

                // 小数位数精度 (18,4) 中的18
                b.Property(e => e.DecimalPrecision).HasComment("小数位数精度");

                // 小数位数刻度 (18,4) 中的4
                b.Property(e => e.DecimalScale).HasComment("小数位数刻度");

                // 枚举类型Id
                b.Property(e => e.EnumTypeId).HasComment("枚举类型Id");

                // 数据类型Id
                b.Property(e => e.DataTypeId).HasComment("数据类型Id");

                // 实体模型Id - 必填
                b.Property(e => e.EntityModelId).IsRequired().HasComment("实体模型Id");

                // 允许作为查询条件 - 必填
                b.Property(e => e.AllowSearch).IsRequired().HasComment("允许作为查询条件");

                // 允许添加 - 必填
                b.Property(e => e.AllowAdd).IsRequired().HasComment("允许添加");

                // 允许编辑 - 必填
                b.Property(e => e.AllowEdit).IsRequired().HasComment("允许编辑");

                b.ConfigureByConvention();
            });

            builder.Entity<DataType>(b =>
            {
                b.ToTable(CodeManagementDbProperties.DbTablePrefix + nameof(DataType));

                // 编码 - 必填，最大长度128
                b.Property(e => e.Code)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("编码");

                // 描述 - 必填，最大长度128
                b.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                b.ConfigureByConvention();
            });

            builder.Entity<EnumType>(b =>
            {
                b.ToTable(CodeManagementDbProperties.DbTablePrefix + nameof(EnumType));

                // 租户id
                b.Property(e => e.TenantId).HasComment("租户id");

                // 编码 - 必填，最大长度128，建立索引
                b.Property(e => e.Code)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("编码");
                b.HasIndex(e => e.Code);

                // 描述 - 最大长度128
                b.Property(e => e.Description)
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                // 实体模型Id - 必填
                b.Property(e => e.EntityModelId).IsRequired().HasComment("实体模型Id");

                // 项目Id - 必填
                b.Property(e => e.ProjectId).IsRequired().HasComment("项目Id");

                // 枚举类型属性集合 - 一对多关系
                b.HasMany(e => e.EnumTypeProperties)
                    .WithOne()
                    .HasForeignKey(etp => etp.EnumTypeId)
                    .HasConstraintName("FK_EnumTypeProperty_EnumTypeId")
                    .IsRequired();

                b.ConfigureByConvention();
            });

            builder.Entity<EnumTypeProperty>(b =>
            {
                b.ToTable(CodeManagementDbProperties.DbTablePrefix + nameof(EnumTypeProperty));

                // 枚举id - 必填
                b.Property(e => e.EnumTypeId).IsRequired().HasComment("枚举id");

                // 编码 - 必填，最大长度128，建立索引
                b.Property(e => e.Code)
                    .IsRequired()
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("编码");
                b.HasIndex(e => e.Code);

                // 枚举值 - 必填
                b.Property(e => e.Value).IsRequired().HasComment("枚举值");

                // 描述 - 最大长度128
                b.Property(e => e.Description)
                    .HasMaxLength(CodeManagementConsts.MaxLength128)
                    .HasComment("描述");

                b.ConfigureByConvention();
            });
        }
    }
}
