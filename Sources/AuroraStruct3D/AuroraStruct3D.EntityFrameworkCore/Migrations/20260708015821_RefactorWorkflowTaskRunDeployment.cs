using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <summary>
    /// 工作流四层重构迁移：
    /// <para>1) 现运行表 AbpProWorkflowProjectTasks 重命名为 AbpProWorkflowProjectRuns（保留数据）；</para>
    /// <para>2) 现绑定表 AbpProWorkflowProjectBindings 重命名为 AbpProWorkflowProjectTasks（作为任务配置，保留数据）；</para>
    /// <para>3) 任务配置表新增 TaskType/CycleIntervalSeconds；运行表新增 CycleIntervalSeconds；</para>
    /// <para>4) 部署表新增冻结列 FrozenGraphsJson/FrozenVariablesJson。</para>
    /// </summary>
    public partial class RefactorWorkflowTaskRunDeployment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 步骤 1：现运行表 Tasks → Runs（先腾出 Tasks 表名）───────────────────────
            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpProWorkflowProjectTasks",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.RenameTable(
                name: "AbpProWorkflowProjectTasks",
                newName: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpProWorkflowProjectRuns",
                table: "AbpProWorkflowProjectRuns",
                column: "Id"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_ProjectId_CreationTime",
                newName: "IX_AbpProWorkflowProjectRuns_ProjectId_CreationTime",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_DeploymentId",
                newName: "IX_AbpProWorkflowProjectRuns_DeploymentId",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_HangfireJobId",
                newName: "IX_AbpProWorkflowProjectRuns_HangfireJobId",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_Status",
                newName: "IX_AbpProWorkflowProjectRuns_Status",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.AddColumn<int>(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectRuns",
                type: "integer",
                nullable: true
            );

            // ── 步骤 2：现绑定表 Bindings → Tasks（作为任务配置）─────────────────────────
            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpProWorkflowProjectBindings",
                table: "AbpProWorkflowProjectBindings"
            );

            migrationBuilder.RenameTable(
                name: "AbpProWorkflowProjectBindings",
                newName: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpProWorkflowProjectTasks",
                table: "AbpProWorkflowProjectTasks",
                column: "Id"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectBindings_ProjectId_IsEnabled_OrderNo",
                newName: "IX_AbpProWorkflowProjectTasks_ProjectId_IsEnabled_OrderNo",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectBindings_ProjectId_WorkflowId",
                newName: "IX_AbpProWorkflowProjectTasks_ProjectId_WorkflowId",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: true
            );

            // ── 步骤 3：部署表新增冻结列 ───────────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "FrozenGraphsJson",
                table: "AbpProWorkflowProjectDeployments",
                type: "character varying(4194304)",
                maxLength: 4194304,
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<string>(
                name: "FrozenVariablesJson",
                table: "AbpProWorkflowProjectDeployments",
                type: "character varying(1048576)",
                maxLength: 1048576,
                nullable: false,
                defaultValue: "[]"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── 反步骤 3：移除部署冻结列 ───────────────────────────────────────────────
            migrationBuilder.DropColumn(
                name: "FrozenVariablesJson",
                table: "AbpProWorkflowProjectDeployments"
            );

            migrationBuilder.DropColumn(
                name: "FrozenGraphsJson",
                table: "AbpProWorkflowProjectDeployments"
            );

            // ── 反步骤 2：Tasks（配置）→ Bindings（先腾出 Tasks 表名）────────────────────
            migrationBuilder.DropColumn(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.DropColumn(name: "TaskType", table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpProWorkflowProjectTasks",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_ProjectId_WorkflowId",
                newName: "IX_AbpProWorkflowProjectBindings_ProjectId_WorkflowId",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectTasks_ProjectId_IsEnabled_OrderNo",
                newName: "IX_AbpProWorkflowProjectBindings_ProjectId_IsEnabled_OrderNo",
                table: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.RenameTable(
                name: "AbpProWorkflowProjectTasks",
                newName: "AbpProWorkflowProjectBindings"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpProWorkflowProjectBindings",
                table: "AbpProWorkflowProjectBindings",
                column: "Id"
            );

            // ── 反步骤 1：Runs → Tasks（运行表）───────────────────────────────────────
            migrationBuilder.DropColumn(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpProWorkflowProjectRuns",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectRuns_Status",
                newName: "IX_AbpProWorkflowProjectTasks_Status",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectRuns_HangfireJobId",
                newName: "IX_AbpProWorkflowProjectTasks_HangfireJobId",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectRuns_DeploymentId",
                newName: "IX_AbpProWorkflowProjectTasks_DeploymentId",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameIndex(
                name: "IX_AbpProWorkflowProjectRuns_ProjectId_CreationTime",
                newName: "IX_AbpProWorkflowProjectTasks_ProjectId_CreationTime",
                table: "AbpProWorkflowProjectRuns"
            );

            migrationBuilder.RenameTable(
                name: "AbpProWorkflowProjectRuns",
                newName: "AbpProWorkflowProjectTasks"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpProWorkflowProjectTasks",
                table: "AbpProWorkflowProjectTasks",
                column: "Id"
            );
        }
    }
}
