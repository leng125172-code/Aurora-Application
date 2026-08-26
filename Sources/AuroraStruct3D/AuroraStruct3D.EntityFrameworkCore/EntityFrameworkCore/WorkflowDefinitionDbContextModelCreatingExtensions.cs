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
        builder.Entity<WorkflowPlcHandshakeConfig>(b =>
        {
            b.ToTable("AbpProWorkflowPlcHandshakeConfigs");
            b.ConfigureByConvention();
            b.Property(x => x.LastError).HasMaxLength(1024);
            b.Property(x => x.RequestSequence).IsRequired();
            b.Property(x => x.CaptureRequestAddress).HasMaxLength(1024);
            b.Property(x => x.RequestIdAddress).HasMaxLength(1024);
            b.Property(x => x.ResultAckAddress).HasMaxLength(1024);
            b.Property(x => x.ResultAckIdAddress).HasMaxLength(1024);
            b.Property(x => x.HeartbeatAddress).HasMaxLength(1024);
            b.Property(x => x.DeviceStatusAddress).HasMaxLength(1024);
            b.Property(x => x.TaskStatusAddress).HasMaxLength(1024);
            b.Property(x => x.CanCaptureAddress).HasMaxLength(1024);
            b.Property(x => x.CaptureAckAddress).HasMaxLength(1024);
            b.Property(x => x.AckRequestIdAddress).HasMaxLength(1024);
            b.Property(x => x.ResultValidAddress).HasMaxLength(1024);
            b.Property(x => x.ResultRequestIdAddress).HasMaxLength(1024);
            b.Property(x => x.ResultCodeAddress).HasMaxLength(1024);
            b.Property(x => x.ErrorCodeAddress).HasMaxLength(1024);
            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => x.TaskConfigId).IsUnique();
            b.HasIndex(x => x.PlcDeviceId);
            b.HasIndex(x => x.CurrentRunId);
        });
        builder.Entity<WorkflowPlcTrigger>(b =>
        {
            b.ToTable("AbpProWorkflowPlcTriggers"); b.ConfigureByConvention();
            b.Property(x => x.ExpectedValueJson).IsRequired().HasMaxLength(2048);
            b.Property(x => x.LastSkipReason).HasMaxLength(1024);
            b.HasIndex(x => new { x.PlcDeviceId, x.PlcTagId, x.IsEnabled });
            b.HasIndex(x => x.ProjectId);
        });
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
        builder.Entity<WorkflowMigrationSnapshot>(b =>
        {
            b.ToTable("AbpProWorkflowMigrationSnapshots");
            b.ConfigureByConvention();
            b.Property(x => x.WorkflowName).IsRequired().HasMaxLength(256);
            b.Property(x => x.GraphData).IsRequired().HasColumnType("text");
            b.Property(x => x.SourceCode).HasColumnType("text");
            b.HasIndex(x => new { x.BatchId, x.WorkflowId }).IsUnique();
            b.HasIndex(x => x.WorkflowId);
        });
    }
}
