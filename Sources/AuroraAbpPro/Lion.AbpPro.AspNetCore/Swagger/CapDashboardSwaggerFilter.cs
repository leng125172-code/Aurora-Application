using System.Net.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Swagger;

/// <summary>
/// 手动向 Swagger 文档中注入 CAP Dashboard 的 API 路径。
/// 因为 CAP 使用 Minimal API 方式注册路由，在 ABP 环境中无法被自动发现，需通过此过滤器手动补充。
/// </summary>
public class CapDashboardSwaggerFilter : IDocumentFilter
{
    /// <summary>
    /// 对应 DashboardOptions.PathMatch 默认值 "/cap"
    /// </summary>
    private const string PathPrefix = "/cap/api";

    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        const string tag = "CAP";

        // ── GET 接口 ──────────────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/metrics-realtime",
            tag,
            "获取实时指标数据",
            "CAP_GetMetricsRealtime"
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/meta",
            tag,
            "获取CAP元信息（Broker/Storage）",
            "CAP_GetMeta"
        );

        AddGet(swaggerDoc, PathPrefix + "/stats", tag, "获取消息统计概览", "CAP_GetStats");

        AddGet(
            swaggerDoc,
            PathPrefix + "/metrics-history",
            tag,
            "获取小时级历史指标",
            "CAP_GetMetricsHistory"
        );

        AddGet(swaggerDoc, PathPrefix + "/health", tag, "健康检查", "CAP_Health");

        AddGet(
            swaggerDoc,
            PathPrefix + "/published/message/{id}",
            tag,
            "获取已发布消息详情",
            "CAP_GetPublishedMessageDetail",
            PathParam("id", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/received/message/{id}",
            tag,
            "获取已接收消息详情",
            "CAP_GetReceivedMessageDetail",
            PathParam("id", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/published/{status}",
            tag,
            "分页查询已发布消息列表",
            "CAP_GetPublishedList",
            PathParam("status", JsonSchemaType.String),
            QueryParam("perPage", JsonSchemaType.Integer),
            QueryParam("currentPage", JsonSchemaType.Integer),
            QueryParam("name", JsonSchemaType.String),
            QueryParam("content", JsonSchemaType.String)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/received/{status}",
            tag,
            "分页查询已接收消息列表",
            "CAP_GetReceivedList",
            PathParam("status", JsonSchemaType.String),
            QueryParam("perPage", JsonSchemaType.Integer),
            QueryParam("currentPage", JsonSchemaType.Integer),
            QueryParam("name", JsonSchemaType.String),
            QueryParam("group", JsonSchemaType.String),
            QueryParam("content", JsonSchemaType.String)
        );

        AddGet(swaggerDoc, PathPrefix + "/subscriber", tag, "获取订阅者列表", "CAP_GetSubscribers");

        AddGet(swaggerDoc, PathPrefix + "/nodes", tag, "获取节点列表", "CAP_GetNodes");

        AddGet(
            swaggerDoc,
            PathPrefix + "/list-ns",
            tag,
            "获取K8s命名空间列表",
            "CAP_ListNamespaces"
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/list-svc/{namespace}",
            tag,
            "获取K8s服务列表",
            "CAP_ListServices",
            PathParam("namespace", JsonSchemaType.String)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/ping",
            tag,
            "Ping指定节点的健康端点",
            "CAP_Ping",
            QueryParam("endpoint", JsonSchemaType.String)
        );

        // ── POST 接口（请求体为 long[] 消息ID列表）────────────────────────────

        AddPost(
            swaggerDoc,
            PathPrefix + "/published/requeue",
            tag,
            "重新入队已发布消息",
            "CAP_RequeuePublished"
        );

        AddPost(
            swaggerDoc,
            PathPrefix + "/published/delete",
            tag,
            "删除已发布消息",
            "CAP_DeletePublished"
        );

        AddPost(
            swaggerDoc,
            PathPrefix + "/received/reexecute",
            tag,
            "重新执行已接收消息",
            "CAP_ReexecuteReceived"
        );

        AddPost(
            swaggerDoc,
            PathPrefix + "/received/delete",
            tag,
            "删除已接收消息",
            "CAP_DeleteReceived"
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
            ["200"] = new OpenApiResponse { Description = "成功" },
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
            Description = "消息ID列表",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    },
                },
            },
        };
        operation.Responses = new OpenApiResponses
        {
            ["204"] = new OpenApiResponse { Description = "成功" },
            ["422"] = new OpenApiResponse { Description = "消息ID列表为空" },
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
        // Paths 在某些情况下可能为 null，需先初始化
        doc.Paths ??= new OpenApiPaths();

        if (!doc.Paths.ContainsKey(path))
        {
            // Operations 默认为 null，需显式初始化 Dictionary
            var item = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>(),
            };
            doc.Paths.Add(path, item);
        }

        var pathItem = (OpenApiPathItem)doc.Paths[path];
        // 若已存在但 Operations 尚未初始化，补充初始化
        pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
        return pathItem;
    }

    private static OpenApiParameter PathParam(string name, JsonSchemaType type)
    {
        return new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = type },
        };
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
