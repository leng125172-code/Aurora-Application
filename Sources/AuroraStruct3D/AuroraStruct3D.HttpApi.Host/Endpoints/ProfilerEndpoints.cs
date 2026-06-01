using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;

namespace AuroraStruct3D.Endpoints;

/// <summary>
/// MiniProfiler 自定义查询 REST API。
/// MiniProfiler 内置的 /profiler/results-list 只返回当前用户未读 (unviewed) 的会话，
/// 本接口直接读取存储层，提供全量会话列表和按 ID 加载详情能力，
/// 供管理后台 ProfilerPage 使用。
/// </summary>
public static class ProfilerEndpoints
{
    /// <summary>
    /// 在指定路由构建器上注册 /api/profiler/* 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapProfilerApi(this IEndpointRouteBuilder endpoints)
    {
        // 获取最近 N 条会话列表（包含所有已保存的记录，不受 unviewed 过滤）
        // 显式转换为 Delegate，使 ASP.NET Core 能将 Task<IResult> 返回值写入响应
        endpoints.MapGet("/api/profiler/sessions", (Delegate)HandleListSessions);

        // 按 ID 加载单条会话详情（含完整计时树和 SQL 信息）
        endpoints.MapGet("/api/profiler/sessions/{id:guid}", (Delegate)HandleGetSession);

        return endpoints;
    }

    /// <summary>
    /// 获取 Profiler 会话分页列表，支持按名称关键字筛选。
    /// 先拉取全量 ID（上限 5000），内存过滤后分页返回。
    /// </summary>
    private static async Task<IResult> HandleListSessions(
        HttpContext context,
        int page = 1,
        int pageSize = 20,
        string? q = null
    )
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IAsyncStorage? storage = GetStorage(context);
        if (storage is null)
            return Results.Ok(
                new
                {
                    totalCount = 0,
                    page,
                    pageSize,
                    items = Array.Empty<object>(),
                }
            );

        // 拉取全量 ID（上限 5000，防止内存溢出）
        IEnumerable<Guid> allIds = await storage.ListAsync(5000);

        // 批量加载并按关键字过滤
        List<object> allSessions = [];
        foreach (Guid id in allIds)
        {
            MiniProfiler? profiler = await storage.LoadAsync(id);
            if (profiler is null)
                continue;

            if (
                !string.IsNullOrWhiteSpace(q)
                && !profiler.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            allSessions.Add(
                new
                {
                    profiler.Id,
                    profiler.Name,
                    profiler.Started,
                    profiler.DurationMilliseconds,
                    profiler.MachineName,
                    SqlCount = CountSql(profiler.Root),
                    SqlDurationMs = SumSqlMs(profiler.Root),
                }
            );
        }

        int totalCount = allSessions.Count;
        List<object> items = allSessions.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(
            new
            {
                totalCount,
                page,
                pageSize,
                items,
            }
        );
    }

    /// <summary>
    /// 按 ID 加载单条会话完整详情，含嵌套计时树和 CustomTimings（SQL 等）。
    /// </summary>
    private static async Task<IResult> HandleGetSession(Guid id, HttpContext context)
    {
        IAsyncStorage? storage = GetStorage(context);
        if (storage is null)
            return Results.NotFound();

        MiniProfiler? profiler = await storage.LoadAsync(id);
        if (profiler is null)
            return Results.NotFound();

        return Results.Ok(profiler);
    }

    // ── 辅助方法 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取 MiniProfiler 存储实例。
    /// 通过 MiniProfiler.DefaultOptions 静态属性访问，无需依赖 Internal 类型。
    /// </summary>
    private static IAsyncStorage? GetStorage(HttpContext _)
    {
        // MiniProfilerBaseOptions 位于 Internal 命名空间，不直接声明为变量类型，
        // 通过静态属性访问 Storage 字段即可。
        return MiniProfiler.DefaultOptions.Storage as IAsyncStorage;
    }

    /// <summary>
    /// 递归统计计时节点及其所有子节点中的 SQL 调用次数。
    /// </summary>
    private static int CountSql(StackExchange.Profiling.Timing? timing)
    {
        if (timing is null)
            return 0;
        int count = timing.CustomTimings?.Values.Sum(list => list?.Count ?? 0) ?? 0;
        return count + (timing.Children?.Sum(CountSql) ?? 0);
    }

    /// <summary>
    /// 递归累计计时节点及其所有子节点中的 SQL 总耗时（毫秒）。
    /// </summary>
    private static decimal SumSqlMs(StackExchange.Profiling.Timing? timing)
    {
        if (timing is null)
            return 0m;
        decimal ms =
            timing
                .CustomTimings?.Values.SelectMany(list => list ?? [])
                .Sum(t => t.DurationMilliseconds ?? 0m)
            ?? 0m;
        return ms + (timing.Children?.Sum(SumSqlMs) ?? 0m);
    }
}
