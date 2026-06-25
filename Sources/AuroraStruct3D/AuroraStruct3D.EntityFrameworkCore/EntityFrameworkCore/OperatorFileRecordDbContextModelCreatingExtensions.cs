using AuroraStruct3D.OperatorFile;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 算子文件记录模块数据库配置扩展。
/// </summary>
public static class OperatorFileRecordDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置算子文件记录相关数据库表结构。
    /// </summary>
    public static void ConfigureOperatorFileRecord(this ModelBuilder builder)
    {
        builder.Entity<OperatorFileRecord>(b =>
        {
            b.ToTable($"{TablePrefix}OperatorFileRecords");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.OperatorId).IsRequired();
            b.Property(x => x.OperatorDisplayName).IsRequired().HasMaxLength(256);
            b.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(512);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(1024);
            b.Property(x => x.PreviewBlobNames).HasMaxLength(4096);
            b.Property(x => x.FileSizeBytes).IsRequired();
            b.Property(x => x.Md5).IsRequired().HasMaxLength(64);
            b.Property(x => x.CreatedAt).IsRequired();

            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => x.ExpiresAt);
            b.HasIndex(x => new { x.IsUsed, x.ExpiresAt });
        });
    }
}
