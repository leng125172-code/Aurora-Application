using System.Collections.Generic;
using System.Net.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Swagger;

/// <summary>
/// 手动向 Swagger 文档中注入 Hangfire REST API 的路径。
/// 因为 Hangfire 使用 Minimal API 方式注册路由，在 ABP 环境中无法被自动发现，
/// 需通过此过滤器手动补充，与 CapDashboardSwaggerFilter 保持一致的模式。
/// </summary>
public class HangfireDashboardSwaggerFilter : IDocumentFilter
{
    /// <summary>
    /// 对应 HangfireRouteActionProvider 默认值 "/api/hangfire"
    /// </summary>
    private const string PathPrefix = "/api/hangfire";

    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        const string tag = "Hangfire";

        // ── 概览 ──────────────────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/stats",
            tag,
            "获取作业统计概览（各状态计数）",
            "Hangfire_GetStats"
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/servers",
            tag,
            "获取所有 Worker 服务器列表",
            "Hangfire_GetServers"
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/queues",
            tag,
            "获取所有队列及队首作业预览",
            "Hangfire_GetQueues"
        );

        // ── 作业详情 ──────────────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/{jobId}",
            tag,
            "获取作业详情及状态历史",
            "Hangfire_GetJobDetails",
            PathParam("jobId", JsonSchemaType.String)
        );

        // ── 各状态作业列表 ────────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/enqueued",
            tag,
            "分页查询入队（等待执行）的作业",
            "Hangfire_GetEnqueuedJobs",
            QueryParam("queue", JsonSchemaType.String),
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/fetched",
            tag,
            "分页查询已获取（Worker 持有）的作业",
            "Hangfire_GetFetchedJobs",
            QueryParam("queue", JsonSchemaType.String),
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/scheduled",
            tag,
            "分页查询计划中（定时延迟）的作业",
            "Hangfire_GetScheduledJobs",
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/processing",
            tag,
            "分页查询执行中的作业",
            "Hangfire_GetProcessingJobs",
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/succeeded",
            tag,
            "分页查询执行成功的作业",
            "Hangfire_GetSucceededJobs",
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/failed",
            tag,
            "分页查询执行失败的作业",
            "Hangfire_GetFailedJobs",
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/jobs/deleted",
            tag,
            "分页查询已删除的作业",
            "Hangfire_GetDeletedJobs",
            QueryParam("from", JsonSchemaType.Integer),
            QueryParam("perPage", JsonSchemaType.Integer)
        );

        // ── 作业写操作 ────────────────────────────────────────────────────────

        AddPost(
            swaggerDoc,
            PathPrefix + "/jobs/{jobId}/requeue",
            tag,
            "重新入队指定作业（将失败/已删除作业放回队列）",
            "Hangfire_RequeueJob",
            PathParam("jobId", JsonSchemaType.String)
        );

        AddDelete(
            swaggerDoc,
            PathPrefix + "/jobs/{jobId}",
            tag,
            "删除指定作业",
            "Hangfire_DeleteJob",
            PathParam("jobId", JsonSchemaType.String)
        );

        // ── 周期性作业 ────────────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/recurring-jobs",
            tag,
            "获取所有周期性作业列表",
            "Hangfire_GetRecurringJobs"
        );

        AddPost(
            swaggerDoc,
            PathPrefix + "/recurring-jobs/{recurringJobId}/trigger",
            tag,
            "立即触发指定周期性作业（无需等待下次调度时间）",
            "Hangfire_TriggerRecurringJob",
            PathParam("recurringJobId", JsonSchemaType.String)
        );

        AddDelete(
            swaggerDoc,
            PathPrefix + "/recurring-jobs/{recurringJobId}",
            tag,
            "删除指定周期性作业",
            "Hangfire_DeleteRecurringJob",
            PathParam("recurringJobId", JsonSchemaType.String)
        );

        // ── 历史统计图表数据 ──────────────────────────────────────────────────

        AddGet(
            swaggerDoc,
            PathPrefix + "/stats/history/daily",
            tag,
            "获取近7天每日成功/失败作业数量（趋势图表）",
            "Hangfire_GetDailyStats"
        );

        AddGet(
            swaggerDoc,
            PathPrefix + "/stats/history/hourly",
            tag,
            "获取近24小时每小时成功/失败作业数量（趋势图表）",
            "Hangfire_GetHourlyStats"
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
        string operationId,
        params OpenApiParameter[] parameters
    )
    {
        var operation = BuildOperation(doc, tag, summary, operationId);
        foreach (var param in parameters)
            operation.Parameters!.Add(param);
        operation.Responses = new OpenApiResponses
        {
            ["204"] = new OpenApiResponse { Description = "成功" },
            ["404"] = new OpenApiResponse { Description = "作业不存在" },
        };

        EnsurePath(doc, path).Operations[HttpMethod.Post] = operation;
    }

    private static void AddDelete(
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
            ["204"] = new OpenApiResponse { Description = "成功" },
            ["404"] = new OpenApiResponse { Description = "作业不存在" },
        };

        EnsurePath(doc, path).Operations[HttpMethod.Delete] = operation;
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
