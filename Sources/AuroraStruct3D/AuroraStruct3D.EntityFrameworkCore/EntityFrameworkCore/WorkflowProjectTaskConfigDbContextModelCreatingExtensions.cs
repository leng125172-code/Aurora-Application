using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目级工作流任务触发配置数据库配置扩展。
/// </summary>
public static class WorkflowProjectTaskConfigDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目级工作流任务触发配置表结构。
    /// </summary>
    public static void ConfigureWorkflowProjectTaskConfig(this ModelBuilder builder)
    {
        builder.Entity<WorkflowProjectTaskConfig>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowProjectTaskConfigs");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.Name).IsRequired().HasMaxLength(WorkflowProjectTaskConfig.MaxNameLength);
            b.Property(x => x.TaskType).IsRequired();
            b.Property(x => x.CycleIntervalSeconds);
            b.Property(x => x.OnErrorAction).IsRequired();
            b.Property(x => x.IsEnabled).IsRequired();
            b.Property(x => x.ResultWorkflowId);
            b.Property(x => x.ResultVariableName).HasMaxLength(128);

            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => new { x.ProjectId, x.IsEnabled }).IsUnique()
                .HasFilter("\"IsEnabled\" = true AND \"IsDeleted\" = false");
        });
    }
}
