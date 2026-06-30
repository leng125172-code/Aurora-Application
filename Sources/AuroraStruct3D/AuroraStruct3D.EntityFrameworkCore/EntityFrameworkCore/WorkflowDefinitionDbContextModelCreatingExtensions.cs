using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 工作流模块数据库配置扩展。
/// </summary>
public static class WorkflowDefinitionDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置工作流定义表结构。
    /// </summary>
    public static void ConfigureWorkflowDefinition(this ModelBuilder builder)
    {
        builder.Entity<WorkflowDefinition>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowDefinitions");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(WorkflowDefinitionConsts.MaxNameLength);

            // 工作流画布数据 JSON，长度不限（PostgreSQL text）。
            b.Property(x => x.GraphData).IsRequired();

            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => new { x.ProjectId, x.Name });
        });
    }
}
