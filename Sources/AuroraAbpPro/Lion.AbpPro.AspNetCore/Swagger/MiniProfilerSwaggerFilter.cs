using System.Collections.Generic;
using System.Net.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Swagger;

/// <summary>
/// 手动向 Swagger 文档中注入 MiniProfiler REST API 的路径。
/// MiniProfiler 使用中间件方式注册路由，在 ABP 环境中无法被自动发现，
/// 需通过此过滤器手动补充，与 CapDashboardSwaggerFilter 保持一致的模式。
/// </summary>
public class MiniProfilerSwaggerFilter : IDocumentFilter
{
    /// <summary>
    /// 对应 AbpProMiniProfilerOptions.RouteBasePath 配置值，默认 "/profiler"
    /// </summary>
    private const string PathPrefix = "/profiler";

    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        const string tag = "MiniProfiler";

        // 获取最近 100 条 profiling 会话列表（JSON）
        AddGet(
            swaggerDoc,
            PathPrefix + "/results-list",
            tag,
            "获取最近 profiling 会话列表（最多100条）",
            "MiniProfiler_ResultsList",
            QueryParam("last-id", JsonSchemaType.String)
        );

        // 获取单条 profiling 会话的详细数据（JSON）
        // 可通过 ?id=GUID 查询参数传入，或通过 JSON Body { "id": "..." } 传入
        AddPost(
            swaggerDoc,
            PathPrefix + "/results",
            tag,
            "获取指定 profiling 会话的详细计时数据（传入 {id: GUID}）",
            "MiniProfiler_GetResult"
        );
    }

    // ── 私有辅助方法 ──────────────────────────────────────────────────────────

    private static void AddGet(
        OpenApiDocument doc,
        string path,
        string tag,
        string summary,
        string operationId,
        params OpenApiParameter[] parameters
    )
    {
        var operation = BuildOperation(doc, tag, summary, operationId);
        foreach (var param in parameters)
            operation.Parameters!.Add(param);
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "成功，返回 JSON 数组" },
        };

        EnsurePath(doc, path).Operations[HttpMethod.Get] = operation;
    }

    private static void AddPost(
        OpenApiDocument doc,
        string path,
        string tag,
        string summary,
        string operationId
    )
    {
        var operation = BuildOperation(doc, tag, summary, operationId);
        operation.RequestBody = new OpenApiRequestBody
        {
            Description = "Profiler 会话 ID",
            Required = false,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["id"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "uuid",
                            },
                            ["timingCount"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                        },
                    },
                },
            },
        };
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "成功，返回 profiling JSON 数据" },
            ["404"] = new OpenApiResponse { Description = "未找到指定会话" },
        };

        EnsurePath(doc, path).Operations[HttpMethod.Post] = operation;
    }

    private static OpenApiOperation BuildOperation(
        OpenApiDocument doc,
        string tag,
        string summary,
        string operationId
    )
    {
        return new OpenApiOperation
        {
            Summary = summary,
            OperationId = operationId,
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(tag, doc, null!) },
            Parameters = new List<IOpenApiParameter>(),
        };
    }

    private static OpenApiPathItem EnsurePath(OpenApiDocument doc, string path)
    {
        doc.Paths ??= new OpenApiPaths();

        if (!doc.Paths.ContainsKey(path))
        {
            var item = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>(),
            };
            doc.Paths.Add(path, item);
        }

        var pathItem = (OpenApiPathItem)doc.Paths[path];
        pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
        return pathItem;
    }

    private static OpenApiParameter QueryParam(string name, JsonSchemaType type)
    {
        return new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Schema = new OpenApiSchema { Type = type },
        };
    }
}
