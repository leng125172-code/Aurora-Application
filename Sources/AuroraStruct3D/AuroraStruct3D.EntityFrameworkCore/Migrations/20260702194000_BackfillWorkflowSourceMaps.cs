using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <summary>
    /// 清洗历史工作流节点：为缺失的 paramSources / inputBindingSources / outputBindingSources 回填来源标记。
    /// </summary>
    public partial class BackfillWorkflowSourceMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
UPDATE ""AbpProWorkflowDefinitions"" AS w
SET ""GraphData"" = cleaned.cleaned_graph::text
FROM (
    SELECT
        s.""Id"",
        jsonb_set(
            s.graph,
            '{nodes}',
            COALESCE(
                (
                    SELECT jsonb_agg(
                        CASE
                            WHEN jsonb_typeof(node) <> 'object' THEN node
                            ELSE jsonb_set(
                                jsonb_set(
                                    jsonb_set(
                                        node,
                                        '{properties,paramSources}',
                                        CASE
                                            WHEN (node->'properties' ? 'paramSources') THEN node->'properties'->'paramSources'
                                            WHEN jsonb_typeof(node->'properties'->'params') = 'object' THEN (
                                                SELECT COALESCE(
                                                    jsonb_object_agg(
                                                        p.key,
                                                        CASE
                                                            WHEN jsonb_typeof(p.value) = 'object' AND (p.value ? '$var')
                                                                THEN to_jsonb('variable'::text)
                                                            ELSE to_jsonb('literal'::text)
                                                        END
                                                    ),
                                                    '{}'::jsonb
                                                )
                                                FROM jsonb_each(node->'properties'->'params') AS p
                                            )
                                            ELSE '{}'::jsonb
                                        END,
                                        true
                                    ),
                                    '{properties,inputBindingSources}',
                                    CASE
                                        WHEN (node->'properties' ? 'inputBindingSources') THEN node->'properties'->'inputBindingSources'
                                        WHEN jsonb_typeof(node->'properties'->'inputBindings') = 'object' THEN (
                                            SELECT COALESCE(
                                                jsonb_object_agg(
                                                    i.key,
                                                    CASE
                                                        WHEN i.key = 'point_cloud_path'
                                                            OR i.key ILIKE '%_path'
                                                            OR (
                                                                jsonb_typeof(i.value) = 'string'
                                                                AND (
                                                                    trim(BOTH '""' FROM i.value::text) LIKE '%/%'
                                                                    OR trim(BOTH '""' FROM i.value::text) LIKE '%.%'
                                                                )
                                                            )
                                                            THEN to_jsonb('literal'::text)
                                                        ELSE to_jsonb('variable'::text)
                                                    END
                                                ),
                                                '{}'::jsonb
                                            )
                                            FROM jsonb_each(node->'properties'->'inputBindings') AS i
                                        )
                                        ELSE '{}'::jsonb
                                    END,
                                    true
                                ),
                                '{properties,outputBindingSources}',
                                CASE
                                    WHEN (node->'properties' ? 'outputBindingSources') THEN node->'properties'->'outputBindingSources'
                                    WHEN jsonb_typeof(node->'properties'->'outputBindings') = 'object' THEN (
                                        SELECT COALESCE(
                                            jsonb_object_agg(k.key, to_jsonb('variable'::text)),
                                            '{}'::jsonb
                                        )
                                        FROM jsonb_each(node->'properties'->'outputBindings') AS k
                                    )
                                    ELSE '{}'::jsonb
                                END,
                                true
                            )
                        END
                    )
                    FROM jsonb_array_elements(
                        CASE
                            WHEN jsonb_typeof(s.graph->'nodes') = 'array' THEN s.graph->'nodes'
                            ELSE '[]'::jsonb
                        END
                    ) AS node
                ),
                '[]'::jsonb
            ),
            true
        ) AS cleaned_graph
    FROM (
        SELECT
            ""Id"",
            CASE
                WHEN jsonb_typeof(""GraphData""::jsonb) = 'object'
                    AND (""GraphData""::jsonb ? 'graphData')
                    AND jsonb_typeof(""GraphData""::jsonb -> 'graphData') = 'object'
                    THEN ""GraphData""::jsonb -> 'graphData'
                ELSE ""GraphData""::jsonb
            END AS graph
        FROM ""AbpProWorkflowDefinitions""
    ) AS s
) AS cleaned
WHERE w.""Id"" = cleaned.""Id"";
"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 数据清洗迁移不做反向回滚，避免误删人工修复后的来源标记。
        }
    }
}
