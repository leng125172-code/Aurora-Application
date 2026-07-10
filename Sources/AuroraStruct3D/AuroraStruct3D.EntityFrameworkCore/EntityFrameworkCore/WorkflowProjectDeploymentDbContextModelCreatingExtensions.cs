using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目部署快照数据库配置扩展。
/// </summary>
public static class WorkflowProjectDeploymentDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目部署快照表结构。
    /// </summary>
    public static void ConfigureWorkflowProjectDeployment(this ModelBuilder builder)
    {
        builder.Entity<WorkflowProjectDeployment>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowProjectDeployments");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.Revision).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.SnapshotJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectDeploymentConsts.MaxSnapshotJsonLength);
            b.Property(x => x.SnapshotHash)
                .IsRequired()
                .HasMaxLength(WorkflowProjectDeploymentConsts.MaxSnapshotHashLength);
            b.Property(x => x.FrozenGraphsJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectDeploymentConsts.MaxFrozenGraphsJsonLength);
            b.Property(x => x.FrozenVariablesJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectDeploymentConsts.MaxFrozenVariablesJsonLength);
            b.Property(x => x.ActivatedAt);
            b.Property(x => x.ActivatedBy);

            b.HasIndex(x => new { x.ProjectId, x.Revision }).IsUnique();
            b.HasIndex(x => new { x.ProjectId, x.Status });
        });
    }
}
