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
            b.Property(x => x.SourceCode).IsRequired(false);
            b.Property(x => x.SourceHash).HasMaxLength(64).IsRequired(false);
            b.Property(x => x.SemanticHash).HasMaxLength(64).IsRequired(false);
            b.Property(x => x.ProgramHash).HasMaxLength(64).IsRequired(false);
            b.Property(x => x.OperatorContractHash).HasMaxLength(64).IsRequired(false);
            b.Property(x => x.LanguageVersion).IsRequired();
            b.Property(x => x.SourceRevision).IsRequired();

            // 输出变量配置 JSON，长度不限（PostgreSQL text）。
            b.Property(x => x.OutputVariables).IsRequired(false);

            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => new { x.ProjectId, x.Name });
        });

        builder.Entity<WorkflowSourceDraft>(b =>
        {
            b.ToTable("AbpProWorkflowSourceDrafts");
            b.ConfigureByConvention();
            b.Property(x => x.SourceCode).IsRequired();
            b.Property(x => x.GraphData).IsRequired();
            b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.WorkflowId, x.UserId }).IsUnique();
        });

        builder.Entity<WorkflowSourceVersion>(b =>
        {
            b.ToTable("AbpProWorkflowSourceVersions");
            b.ConfigureByConvention();
            b.Property(x => x.SourceCode).IsRequired();
            b.Property(x => x.GraphData).IsRequired();
            b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.SemanticHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ProgramHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.WorkflowId, x.Revision }).IsUnique();
        });

        builder.Entity<WorkflowMigrationBatch>(b =>
        {
            b.ToTable("AbpProWorkflowMigrationBatches");
            b.ConfigureByConvention();
            b.Property(x => x.ResultsJson).IsRequired();
            b.HasIndex(x => new { x.Status, x.CreationTime });
        });
    }
}
