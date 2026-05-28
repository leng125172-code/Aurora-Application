using AuroraStruct3D.ProductModels;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 产品三维数模模块数据库配置扩展
/// </summary>
public static class ProductModelDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置产品三维数模相关数据库表结构
    /// </summary>
    public static void ConfigureProductModel(this ModelBuilder builder)
    {
        builder.Entity<ProductModel>(b =>
        {
            b.ToTable($"{TablePrefix}ProductModels");
            b.ConfigureByConvention();

            // ── 基本字段 ──────────────────────────────────────────────────────────
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductModelConsts.MaxNameLength);

            b.Property(x => x.OriginalFileName)
                .IsRequired()
                .HasMaxLength(ProductModelConsts.MaxOriginalFileNameLength);

            b.Property(x => x.FileFormat).HasConversion<int>().IsRequired();

            b.Property(x => x.FileSizeBytes).IsRequired();

            // ── BLOB 存储键名 ─────────────────────────────────────────────────────
            b.Property(x => x.OriginalBlobName)
                .IsRequired()
                .HasMaxLength(ProductModelConsts.MaxBlobNameLength);

            b.Property(x => x.ConvertedBlobName).HasMaxLength(ProductModelConsts.MaxBlobNameLength);

            // ── 转换状态 ──────────────────────────────────────────────────────────
            b.Property(x => x.ConversionStatus).HasConversion<int>().IsRequired();

            b.Property(x => x.ConversionErrorMessage)
                .HasMaxLength(ProductModelConsts.MaxErrorMessageLength);

            // ── 索引（常用查询字段） ──────────────────────────────────────────────
            b.HasIndex(x => x.FileFormat);
            b.HasIndex(x => x.ConversionStatus);
            b.HasIndex(x => x.CreationTime);
            b.HasIndex(x => x.CreatorId);
        });
    }
}
