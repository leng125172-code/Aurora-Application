using AuroraStruct3D.Projects;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目管理模块数据库配置扩展
/// </summary>
public static class ProjectInfoDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目主表数据库结构
    /// </summary>
    public static void ConfigureProjectInfo(this ModelBuilder builder)
    {
        builder.Entity<ProjectInfo>(b =>
        {
            b.ToTable($"{TablePrefix}ProjectInfos");
            b.ConfigureByConvention();

            // ── 基本字段 ──────────────────────────────────────────────────────────
            b.Property(x => x.ProjectCode)
                .IsRequired()
                .HasMaxLength(ProjectInfoConsts.MaxProjectCodeLength);

            b.Property(x => x.Name).IsRequired().HasMaxLength(ProjectInfoConsts.MaxNameLength);

            b.Property(x => x.Version)
                .IsRequired()
                .HasMaxLength(ProjectInfoConsts.MaxVersionLength);

            b.Property(x => x.Description).HasMaxLength(ProjectInfoConsts.MaxDescriptionLength);

            b.Property(x => x.Status).HasConversion<int>().IsRequired();

            // ── 索引 ──────────────────────────────────────────────────────────────
            b.HasIndex(x => x.ProjectCode).IsUnique();
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.CreationTime);
            b.HasIndex(x => x.CreatorId);
        });
    }
}
