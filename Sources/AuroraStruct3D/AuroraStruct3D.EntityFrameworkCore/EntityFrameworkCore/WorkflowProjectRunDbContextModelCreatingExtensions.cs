using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目级工作流运行实例数据库配置扩展。
/// </summary>
public static class WorkflowProjectRunDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目级工作流运行实例表结构。
    /// </summary>
    public static void ConfigureWorkflowProjectRun(this ModelBuilder builder)
    {
        builder.Entity<WorkflowProjectRun>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowProjectRuns");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.TaskConfigId);
            b.Property(x => x.PlcHandshakeConfigId);
            b.Property(x => x.PlcRequestId);
            b.Property(x => x.PlcRequestSequence);
            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(WorkflowProjectRunConsts.MaxNameLength);
            b.Property(x => x.HangfireJobId)
                .HasMaxLength(WorkflowProjectRunConsts.MaxHangfireJobIdLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.StartType).IsRequired();
            b.Property(x => x.CycleIntervalSeconds);
            b.Property(x => x.DeploymentId);
            b.Property(x => x.DeploymentRevision);
            b.Property(x => x.ContinueOnError).IsRequired();
            b.Property(x => x.WorkflowIdsJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectRunConsts.MaxWorkflowIdsJsonLength);
            b.Property(x => x.ResultsJson)
                .IsRequired()
                .HasMaxLength(WorkflowProjectRunConsts.MaxResultsJsonLength);
            b.Property(x => x.ExecutedCount).IsRequired();
            b.Property(x => x.SuccessCount).IsRequired();
            b.Property(x => x.FailedCount).IsRequired();
            b.Property(x => x.StartedAt);
            b.Property(x => x.FinishedAt);
            b.Property(x => x.ErrorMessage).HasMaxLength(WorkflowProjectRunConsts.MaxErrorLength);
            b.Property(x => x.IsCancelRequested).IsRequired();
            b.Property(x => x.InspectionDecision).IsRequired();
            b.Property(x => x.InspectionErrorCode).IsRequired();
            b.Property(x => x.InspectionErrorMessage).HasMaxLength(WorkflowProjectRunConsts.MaxErrorLength);

            b.HasIndex(x => new { x.ProjectId, x.CreationTime });
            b.HasIndex(x => x.DeploymentId);
            b.HasIndex(x => x.HangfireJobId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.TaskConfigId);
            b.HasIndex(x => x.PlcHandshakeConfigId);
            b.HasIndex(x => new { x.PlcHandshakeConfigId, x.PlcRequestSequence })
                .IsUnique()
                .HasFilter("\"PlcHandshakeConfigId\" IS NOT NULL AND \"PlcRequestSequence\" IS NOT NULL");
        });
    }
}
