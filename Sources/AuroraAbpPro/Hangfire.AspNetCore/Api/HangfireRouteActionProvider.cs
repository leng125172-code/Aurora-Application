// Hangfire REST API 路由提供者
// 替代原有 Hangfire Dashboard，通过 Minimal API 对外暴露监控和管理接口。

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hangfire.Api
{
    /// <summary>
    /// 将 Hangfire.Common.Job 整体序列化为安全的字符串字段对象。
    /// Job.Type、Job.Method、Job.Args 均含有 System.Text.Json 无法处理的运行时类型
    /// （System.Type、MethodInfo、CancellationToken、IntPtr 等），
    /// 通过本 Converter 统一投影为字符串，彻底阻断 STJ 的反射链，避免 NotSupportedException。
    /// </summary>
    internal sealed class HangfireJobConverter : JsonConverter<Job>
    {
        /// <inheritdoc/>
        public override Job? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => null; // 反序列化暂不支持

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Job value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // 类型名称：写完整限定名字符串
            writer.WriteString("type", value.Type?.FullName);

            // 方法名称：写「DeclaringType.方法名」字符串
            writer.WriteString(
                "method",
                value.Method is null
                    ? null
                    : $"{value.Method.DeclaringType?.FullName}.{value.Method.Name}"
            );

            // 参数列表：每个参数统一用 ToString() 转为字符串，避免反射进入复杂对象
            writer.WritePropertyName("args");
            writer.WriteStartArray();
            foreach (object? arg in value.Args)
            {
                if (arg is null)
                    writer.WriteNullValue();
                else
                    writer.WriteStringValue(arg.ToString());
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// 可安全通过 System.Text.Json 返回的周期性作业视图。
    /// 不直接暴露 Hangfire 的 Job 对象，避免序列化 System.Type、MethodInfo 等运行时类型。
    /// </summary>
    internal sealed class RecurringJobApiDto
    {
        public string? Id { get; init; }
        public string? Cron { get; init; }
        public string? Queue { get; init; }
        public DateTime? NextExecution { get; init; }
        public string? LastJobId { get; init; }
        public string? LastJobState { get; init; }
        public DateTime? LastExecution { get; init; }
        public DateTime? CreatedAt { get; init; }
        public bool Removed { get; init; }
        public string? TimeZoneId { get; init; }
        public string? Error { get; init; }
        public int RetryAttempt { get; init; }
        public string? JobType { get; init; }
        public string? Method { get; init; }
    }

    /// <summary>
    /// Hangfire 监控 REST API 的路由注册提供者。
    /// 所有接口统一挂载在指定路径前缀下，默认 /api/hangfire。
    /// </summary>
    public class HangfireRouteActionProvider
    {
        private readonly IEndpointRouteBuilder _builder;
        private readonly string _prefix;

        /// <summary>
        /// 用于序列化含有 System.Type 属性的 Hangfire 作业对象的 JSON 选项。
        /// </summary>
        private static readonly JsonSerializerOptions _jobSerializerOptions = new(
            JsonSerializerDefaults.Web
        )
        {
            // HangfireJobConverter 统一处理 Job 对象，阻断所有嵌套不可序列化类型
            Converters = { new HangfireJobConverter() },
        };

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="builder">ASP.NET Core 端点路由构建器</param>
        /// <param name="pathPrefix">API 路径前缀</param>
        public HangfireRouteActionProvider(
            IEndpointRouteBuilder builder,
            string pathPrefix = "/api/hangfire"
        )
        {
            _builder = builder;
            _prefix = pathPrefix.TrimEnd('/');
        }

        private JobStorage GetStorage(IServiceProvider sp) => sp.GetRequiredService<JobStorage>();

        private IMonitoringApi GetMonitor(IServiceProvider sp) => GetStorage(sp).GetMonitoringApi();

        /// <summary>
        /// 注册所有 Hangfire 监控与管理 API 路由。
        /// </summary>
        public void MapApiRoutes()
        {
            // ── 概览 ──────────────────────────────────────────────────────────────────

            // 获取作业统计概览（各状态计数）
            _builder
                .MapGet(
                    _prefix + "/stats",
                    (IServiceProvider sp) =>
                    {
                        var stats = GetMonitor(sp).GetStatistics();
                        return Results.Ok(stats);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetStats")
                .WithSummary("获取作业统计概览（各状态计数）");

            // 获取所有 Worker 服务器列表
            _builder
                .MapGet(
                    _prefix + "/servers",
                    (IServiceProvider sp) =>
                    {
                        var servers = GetMonitor(sp).Servers();
                        return Results.Ok(servers);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetServers")
                .WithSummary("获取所有 Worker 服务器列表");

            // 获取所有队列及队首作业预览
            _builder
                .MapGet(
                    _prefix + "/queues",
                    (IServiceProvider sp) =>
                    {
                        var queues = GetMonitor(sp).Queues();
                        return Results.Ok(queues);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetQueues")
                .WithSummary("获取所有队列及队首作业预览");

            // ── 作业详情 ──────────────────────────────────────────────────────────────

            // 获取作业详情及状态历史
            _builder
                .MapGet(
                    _prefix + "/jobs/{jobId}",
                    (IServiceProvider sp, string jobId) =>
                    {
                        var details = GetMonitor(sp).JobDetails(jobId);
                        if (details == null)
                            return Results.NotFound();
                        return Results.Json(details, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetJobDetails")
                .WithSummary("获取作业详情及状态历史");

            // ── 各状态作业列表 ────────────────────────────────────────────────────────

            // 分页查询入队（等待执行）的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/enqueued",
                    (
                        IServiceProvider sp,
                        string queue = "default",
                        int from = 0,
                        int perPage = 20
                    ) =>
                    {
                        var jobs = GetMonitor(sp).EnqueuedJobs(queue, from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetEnqueuedJobs")
                .WithSummary("分页查询入队（等待执行）的作业");

            // 分页查询已获取（正在被 Worker 持有）的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/fetched",
                    (
                        IServiceProvider sp,
                        string queue = "default",
                        int from = 0,
                        int perPage = 20
                    ) =>
                    {
                        var jobs = GetMonitor(sp).FetchedJobs(queue, from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetFetchedJobs")
                .WithSummary("分页查询已获取（Worker 持有）的作业");

            // 分页查询计划中（定时延迟）的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/scheduled",
                    (IServiceProvider sp, int from = 0, int perPage = 20) =>
                    {
                        var jobs = GetMonitor(sp).ScheduledJobs(from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetScheduledJobs")
                .WithSummary("分页查询计划中（定时延迟）的作业");

            // 分页查询执行中的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/processing",
                    (IServiceProvider sp, int from = 0, int perPage = 20) =>
                    {
                        var jobs = GetMonitor(sp).ProcessingJobs(from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetProcessingJobs")
                .WithSummary("分页查询执行中的作业");

            // 分页查询执行成功的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/succeeded",
                    (IServiceProvider sp, int from = 0, int perPage = 20) =>
                    {
                        var jobs = GetMonitor(sp).SucceededJobs(from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetSucceededJobs")
                .WithSummary("分页查询执行成功的作业");

            // 分页查询执行失败的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/failed",
                    (IServiceProvider sp, int from = 0, int perPage = 20) =>
                    {
                        var jobs = GetMonitor(sp).FailedJobs(from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetFailedJobs")
                .WithSummary("分页查询执行失败的作业");

            // 分页查询已删除的作业
            _builder
                .MapGet(
                    _prefix + "/jobs/deleted",
                    (IServiceProvider sp, int from = 0, int perPage = 20) =>
                    {
                        var jobs = GetMonitor(sp).DeletedJobs(from, perPage);
                        return Results.Json(jobs, _jobSerializerOptions);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetDeletedJobs")
                .WithSummary("分页查询已删除的作业");

            // ── 作业写操作 ────────────────────────────────────────────────────────────

            // 重新入队指定作业
            _builder
                .MapPost(
                    _prefix + "/jobs/{jobId}/requeue",
                    (IServiceProvider sp, string jobId) =>
                    {
                        var client = sp.GetRequiredService<IBackgroundJobClient>();
                        bool result = client.Requeue(jobId);
                        if (!result)
                            return Results.NotFound();
                        return Results.NoContent();
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_RequeueJob")
                .WithSummary("重新入队指定作业（将失败/已删除作业放回队列）");

            // 删除指定作业
            _builder
                .MapDelete(
                    _prefix + "/jobs/{jobId}",
                    (IServiceProvider sp, string jobId) =>
                    {
                        var client = sp.GetRequiredService<IBackgroundJobClient>();
                        bool result = client.Delete(jobId);
                        if (!result)
                            return Results.NotFound();
                        return Results.NoContent();
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_DeleteJob")
                .WithSummary("删除指定作业");

            // ── 周期性作业 ────────────────────────────────────────────────────────────

            // 获取所有周期性作业列表
            _builder
                .MapGet(
                    _prefix + "/recurring-jobs",
                    (IServiceProvider sp) =>
                    {
                        using IStorageConnection connection = GetStorage(sp)
                            .GetReadOnlyConnection();
                        var jobs = connection
                            .GetRecurringJobs()
                            .ConvertAll(job => new RecurringJobApiDto
                            {
                                Id = job.Id,
                                Cron = job.Cron,
                                Queue = job.Queue,
                                NextExecution = job.NextExecution,
                                LastJobId = job.LastJobId,
                                LastJobState = job.LastJobState,
                                LastExecution = job.LastExecution,
                                CreatedAt = job.CreatedAt,
                                Removed = job.Removed,
                                TimeZoneId = job.TimeZoneId,
                                Error = job.Error ?? job.LoadException?.Message,
                                RetryAttempt = job.RetryAttempt,
                                JobType = job.Job?.Type?.FullName,
                                Method = job.Job?.Method?.Name,
                            });
                        return Results.Ok(jobs);
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetRecurringJobs")
                .WithSummary("获取所有周期性作业列表");

            // 立即触发指定周期性作业
            _builder
                .MapPost(
                    _prefix + "/recurring-jobs/{recurringJobId}/trigger",
                    (IServiceProvider sp, string recurringJobId) =>
                    {
                        var manager = sp.GetRequiredService<IRecurringJobManager>();
                        manager.Trigger(recurringJobId);
                        return Results.NoContent();
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_TriggerRecurringJob")
                .WithSummary("立即触发指定周期性作业（无需等待下次调度时间）");

            // 删除指定周期性作业
            _builder
                .MapDelete(
                    _prefix + "/recurring-jobs/{recurringJobId}",
                    (IServiceProvider sp, string recurringJobId) =>
                    {
                        var manager = sp.GetRequiredService<IRecurringJobManager>();
                        manager.RemoveIfExists(recurringJobId);
                        return Results.NoContent();
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_DeleteRecurringJob")
                .WithSummary("删除指定周期性作业");

            // ── 历史统计图表数据 ──────────────────────────────────────────────────────

            // 获取近7天每日成功/失败作业数量（用于趋势图表）
            _builder
                .MapGet(
                    _prefix + "/stats/history/daily",
                    (IServiceProvider sp) =>
                    {
                        var monitor = GetMonitor(sp);
                        return Results.Ok(
                            new
                            {
                                succeeded = monitor.SucceededByDatesCount(),
                                failed = monitor.FailedByDatesCount(),
                            }
                        );
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetDailyStats")
                .WithSummary("获取近7天每日成功/失败作业数量（趋势图表）");

            // 获取近24小时每小时成功/失败作业数量（用于趋势图表）
            _builder
                .MapGet(
                    _prefix + "/stats/history/hourly",
                    (IServiceProvider sp) =>
                    {
                        var monitor = GetMonitor(sp);
                        return Results.Ok(
                            new
                            {
                                succeeded = monitor.HourlySucceededJobs(),
                                failed = monitor.HourlyFailedJobs(),
                            }
                        );
                    }
                )
                .WithTags("Hangfire")
                .WithName("Hangfire_GetHourlyStats")
                .WithSummary("获取近24小时每小时成功/失败作业数量（趋势图表）");
        }
    }
}
