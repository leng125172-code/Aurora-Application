using AuroraStruct3D.Workflow;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 项目工作流绑定数据库配置扩展。
/// </summary>
public static class WorkflowProjectBindingDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置项目工作流绑定表结构。
    /// </summary>
    public static void ConfigureWorkflowProjectBinding(this ModelBuilder builder)
    {
        builder.Entity<WorkflowProjectBinding>(b =>
        {
            b.ToTable($"{TablePrefix}WorkflowProjectBindings");
            b.ConfigureByConvention();

            b.Property(x => x.ProjectId).IsRequired();
            b.Property(x => x.WorkflowId).IsRequired();
            b.Property(x => x.IsEnabled).IsRequired();
            b.Property(x => x.OrderNo).IsRequired();

            b.HasIndex(x => new { x.ProjectId, x.WorkflowId }).IsUnique();
            b.HasIndex(x => new
            {
                x.ProjectId,
                x.IsEnabled,
                x.OrderNo,
            });
        });
    }
}
