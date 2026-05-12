using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using AuroraStruct3D.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuroraStruct3D.Endpoints;

/// <summary>
/// 系统信息 Minimal API 端点注册。
/// 提供服务器运行环境概览与已加载程序集列表。
/// </summary>
public static class SystemInfoEndpoints
{
    /// <summary>
    /// 在指定路由构建器上注册 /api/system-info 端点。
    /// </summary>
    public static IEndpointRouteBuilder MapSystemInfoApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/system-info", HandleGetSystemInfo);
        return endpoints;
    }

    private static IResult HandleGetSystemInfo(HttpContext context)
    {
        // 通过 DI 获取已有采集器，避免重复采集开销
        SystemMetricsSnapshot? snapshot = context
            .RequestServices.GetService<SystemMetricsCollector>()
            ?.Collect();

        IHostEnvironment? env = context.RequestServices.GetService<IHostEnvironment>();

        Process process = Process.GetCurrentProcess();
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset processStart = new DateTimeOffset(
            process.StartTime.ToLocalTime(),
            TimeZoneInfo.Local.GetUtcOffset(process.StartTime)
        );

        ServerInfoDto server = new()
        {
            ApplicationName = env?.ApplicationName ?? "Unknown",
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessorModel = GetProcessorModel(),
            CpuPercent = snapshot?.CpuPercent ?? -1,
            MemoryUsedBytes = snapshot?.MemoryUsedBytes ?? 0,
            MemoryTotalBytes = snapshot?.MemoryTotalBytes ?? 0,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            ContentRootPath = env?.ContentRootPath ?? string.Empty,
            ProcessName = process.ProcessName,
            ProcessStartTime = processStart,
            ServerTime = now,
            Uptime = now - processStart,
        };

        List<AssemblyInfoDto> assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(BuildAssemblyDto)
            // 仅展示有描述信息或属于项目关键命名空间的程序集
            .Where(a =>
                !string.IsNullOrEmpty(a.Description)
                || !string.IsNullOrEmpty(a.Title)
                || IsRelevantAssembly(a.Name)
            )
            .OrderBy(a => a.Name)
            .ToList();

        return Results.Ok(new SystemInfoDto { Server = server, Assemblies = assemblies });
    }

    /// <summary>
    /// 判断是否为值得展示的程序集（过滤掉 .NET 运行时内部程序集）。
    /// </summary>
    private static bool IsRelevantAssembly(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // 展示本项目及常用第三方框架程序集
        return name.StartsWith("Aurora", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Volo.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Lion.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("DotNetCore.CAP", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("StackExchange.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("MiniProfiler", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取处理器型号描述。
    /// Linux 从 /proc/cpuinfo 读取，其他平台返回架构字符串。
    /// </summary>
    private static string GetProcessorModel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                string cpuinfo = File.ReadAllText("/proc/cpuinfo");
                string? line = cpuinfo
                    .Split('\n')
                    .FirstOrDefault(l =>
                        l.StartsWith("model name", StringComparison.OrdinalIgnoreCase)
                        || l.StartsWith("Model name", StringComparison.OrdinalIgnoreCase)
                    );
                if (line != null)
                {
                    string model = line.Split(':').ElementAtOrDefault(1)?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(model))
                        return model;
                }
            }
            catch
            {
                // 读取失败时静默回退
            }
        }
        return $"{RuntimeInformation.OSArchitecture}, {Environment.ProcessorCount} 核";
    }

    /// <summary>
    /// 从程序集的自定义特性中提取元数据。
    /// </summary>
    private static AssemblyInfoDto BuildAssemblyDto(Assembly asm)
    {
        AssemblyName name = asm.GetName();
        string title =
            asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? name.Name ?? string.Empty;
        string fileVersion =
            asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;
        string infoVersion =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;
        string description =
            asm.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

        // 使用 DLL 文件最后写入时间近似代替编译时间
        DateTimeOffset? buildTime = null;
        try
        {
            if (!string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location))
            {
                buildTime = new DateTimeOffset(
                    File.GetLastWriteTime(asm.Location),
                    TimeZoneInfo.Local.GetUtcOffset(File.GetLastWriteTime(asm.Location))
                );
            }
        }
        catch
        {
            // 忽略无法读取的程序集路径
        }

        return new AssemblyInfoDto
        {
            Name = name.Name ?? string.Empty,
            Title = title,
            FileVersion = fileVersion,
            InformationalVersion = infoVersion,
            Description = description,
            BuildTime = buildTime,
        };
    }
}

// ── DTO 定义 ──────────────────────────────────────────────────────────────────

/// <summary>系统信息完整响应 DTO。</summary>
public sealed class SystemInfoDto
{
    /// <summary>服务器运行环境信息。</summary>
    public ServerInfoDto Server { get; init; } = new();

    /// <summary>已加载的程序集列表。</summary>
    public List<AssemblyInfoDto> Assemblies { get; init; } = [];
}

/// <summary>服务器运行环境信息。</summary>
public sealed class ServerInfoDto
{
    /// <summary>应用名称。</summary>
    public string ApplicationName { get; init; } = string.Empty;

    /// <summary>操作系统描述（含内核版本）。</summary>
    public string OsDescription { get; init; } = string.Empty;

    /// <summary>CPU 架构（X64/Arm64 等）。</summary>
    public string OsArchitecture { get; init; } = string.Empty;

    /// <summary>逻辑处理器数量。</summary>
    public int ProcessorCount { get; init; }

    /// <summary>处理器型号描述。</summary>
    public string ProcessorModel { get; init; } = string.Empty;

    /// <summary>CPU 使用率（0-100），-1 表示不支持。</summary>
    public double CpuPercent { get; init; }

    /// <summary>已使用内存（字节）。</summary>
    public long MemoryUsedBytes { get; init; }

    /// <summary>总内存（字节）。</summary>
    public long MemoryTotalBytes { get; init; }

    /// <summary>机器名称。</summary>
    public string MachineName { get; init; } = string.Empty;

    /// <summary>运行用户名。</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>.NET 运行时版本描述。</summary>
    public string DotNetVersion { get; init; } = string.Empty;

    /// <summary>应用根目录路径。</summary>
    public string ContentRootPath { get; init; } = string.Empty;

    /// <summary>宿主进程名称。</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>进程启动时间（含时区）。</summary>
    public DateTimeOffset ProcessStartTime { get; init; }

    /// <summary>当前服务器时间（含时区）。</summary>
    public DateTimeOffset ServerTime { get; init; }

    /// <summary>进程已运行时长。</summary>
    [JsonIgnore]
    public TimeSpan Uptime { get; init; }

    /// <summary>运行时长格式化字符串（供前端展示）。</summary>
    public string UptimeText => FormatUptime(Uptime);

    /// <summary>按可读规则格式化运行时长：秒/分:秒/时:分:秒/天:时:分:秒。</summary>
    private static string FormatUptime(TimeSpan t)
    {
        int totalDays = (int)t.TotalDays;
        if (totalDays >= 1)
            return $"{totalDays}天{t.Hours}小时{t.Minutes}分钟{t.Seconds}秒";
        if ((int)t.TotalHours >= 1)
            return $"{(int)t.TotalHours}小时{t.Minutes}分钟{t.Seconds}秒";
        if ((int)t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}分钟{t.Seconds}秒";
        return $"{Math.Max(0, (int)t.TotalSeconds)}秒";
    }
}

/// <summary>程序集元数据 DTO。</summary>
public sealed class AssemblyInfoDto
{
    /// <summary>程序集名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>程序集标题（AssemblyTitle）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>文件版本（AssemblyFileVersion）。</summary>
    public string FileVersion { get; init; } = string.Empty;

    /// <summary>内部版本（AssemblyInformationalVersion）。</summary>
    public string InformationalVersion { get; init; } = string.Empty;

    /// <summary>编译时间（近似，取 DLL 文件最后写入时间）。</summary>
    public DateTimeOffset? BuildTime { get; init; }

    /// <summary>程序集描述（AssemblyDescription）。</summary>
    public string Description { get; init; } = string.Empty;
}
