using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <summary>
    /// 清洗历史工作流数据：将旧包装结构 { ..., graphData: { nodes, edges } }
    /// 迁移为标准 graphData 根结构 { nodes, edges }。
    /// </summary>
    public partial class CleanLegacyWorkflowGraphData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
UPDATE ""AbpProWorkflowDefinitions""
SET ""GraphData"" = ((""GraphData""::jsonb -> 'graphData')::text)
WHERE jsonb_typeof(""GraphData""::jsonb) = 'object'
    AND (""GraphData""::jsonb ? 'graphData')
    AND jsonb_typeof(""GraphData""::jsonb -> 'graphData') = 'object'
    AND (""GraphData""::jsonb -> 'graphData' ? 'nodes')
    AND (""GraphData""::jsonb -> 'graphData' ? 'edges');
"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
UPDATE ""AbpProWorkflowDefinitions""
SET ""GraphData"" = (jsonb_build_object('graphData', ""GraphData""::jsonb)::text)
WHERE jsonb_typeof(""GraphData""::jsonb) = 'object'
    AND (""GraphData""::jsonb ? 'nodes')
    AND (""GraphData""::jsonb ? 'edges')
    AND NOT (""GraphData""::jsonb ? 'graphData');
"
            );
        }
    }
}
