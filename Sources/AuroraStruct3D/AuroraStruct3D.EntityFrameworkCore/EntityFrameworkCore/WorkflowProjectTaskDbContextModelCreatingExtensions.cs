using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目级工作流任务数据库配置扩展。
/// </summary>
public static class WorkflowProjectTaskDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目级工作流任务表结构。
    /// </summary>
    public static void ConfigureWorkflowProjectTask(this ModelBuilder builder)
    {
        builder.Entity<WorkflowProjectTask>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowProjectTasks");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(WorkflowProjectTaskConsts.MaxNameLength);
            b.Property(x => x.HangfireJobId)
                .HasMaxLength(WorkflowProjectTaskConsts.MaxHangfireJobIdLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.StartType).IsRequired();
            b.Property(x => x.DeploymentId);
            b.Property(x => x.DeploymentRevision);
            b.Property(x => x.ContinueOnError).IsRequired();
            b.Property(x => x.UseOnlineVariablePool).IsRequired();
            b.Property(x => x.RuntimeInstanceId);
            b.Property(x => x.VariableReadTimeoutMs).IsRequired();
            b.Property(x => x.WorkflowIdsJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectTaskConsts.MaxWorkflowIdsJsonLength);
            b.Property(x => x.ResultsJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectTaskConsts.MaxResultsJsonLength);
            b.Property(x => x.ExecutedCount).IsRequired();
            b.Property(x => x.SuccessCount).IsRequired();
            b.Property(x => x.FailedCount).IsRequired();
            b.Property(x => x.StartedAt);
            b.Property(x => x.FinishedAt);
            b.Property(x => x.ErrorMessage).HasMaxLength(WorkflowProjectTaskConsts.MaxErrorLength);
            b.Property(x => x.IsCancelRequested).IsRequired();

            b.HasIndex(x => new { x.ProjectId, x.CreationTime });
            b.HasIndex(x => x.DeploymentId);
            b.HasIndex(x => x.HangfireJobId);
            b.HasIndex(x => x.Status);
        });
    }
}
