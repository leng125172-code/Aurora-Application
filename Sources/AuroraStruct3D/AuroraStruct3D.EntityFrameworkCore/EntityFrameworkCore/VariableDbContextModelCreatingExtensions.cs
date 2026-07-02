using AuroraStruct3D.Variables;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 变量模块数据库配置扩展。
/// </summary>
public static class VariableDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置变量定义与在线变量池表结构。
    /// </summary>
    public static void ConfigureVariables(this ModelBuilder builder)
    {
        builder.Entity<VariableDefinition>(b =>
        {
            b.ToTable($"{TablePrefix}VariableDefinitions");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.OwnerWorkflowId).IsRequired();
            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(VariableDefinitionConsts.MaxVariableNameLength);
            b.Property(x => x.TypeName)
                .IsRequired()
                .HasMaxLength(VariableDefinitionConsts.MaxTypeNameLength);
            b.Property(x => x.DefaultValueJson)
                .HasMaxLength(VariableDefinitionConsts.MaxDefaultValueLength);
            b.Property(x => x.Visibility).IsRequired();
            b.Property(x => x.Mutability).IsRequired();
            b.Property(x => x.IsRequiredInit).IsRequired();
            b.Property(x => x.SnapshotVersion).IsRequired();

            b.HasIndex(x => new
                {
                    x.ProjectId,
                    x.OwnerWorkflowId,
                    x.Name,
                })
                .IsUnique();
            b.HasIndex(x => new
            {
                x.ProjectId,
                x.OwnerWorkflowId,
                x.Visibility,
            });
        });

        builder.Entity<VariableInstanceValue>(b =>
        {
            b.ToTable($"{TablePrefix}VariableInstanceValues");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.InstanceId).IsRequired();
            b.Property(x => x.VariableDefinitionId).IsRequired();
            b.Property(x => x.OwnerWorkflowId).IsRequired();
            b.Property(x => x.VariableName)
                .IsRequired()
                .HasMaxLength(VariableDefinitionConsts.MaxVariableNameLength);
            b.Property(x => x.TypeName)
                .IsRequired()
                .HasMaxLength(VariableDefinitionConsts.MaxTypeNameLength);
            b.Property(x => x.State).IsRequired();
            b.Property(x => x.ValueJson);
            b.Property(x => x.ValueVersion).IsRequired();
            b.Property(x => x.LastWriterNodeId).HasMaxLength(128);

            b.HasIndex(x => new
                {
                    x.ProjectId,
                    x.InstanceId,
                    x.OwnerWorkflowId,
                    x.VariableName,
                })
                .IsUnique();
            b.HasIndex(x => new
            {
                x.ProjectId,
                x.InstanceId,
                x.VariableDefinitionId,
            });
            b.HasIndex(x => new
            {
                x.ProjectId,
                x.InstanceId,
                x.State,
            });
        });
    }
}
