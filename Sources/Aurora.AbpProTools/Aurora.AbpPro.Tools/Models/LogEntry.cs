using System;

namespace Aurora.AbpPro.Tools.Models;

/// <summary>
/// 结构化日志条目：用于 DataGrid 展示与筛选
/// </summary>
public class LogEntry
{
    /// <summary>
    /// 日志写入时间（本地时间）
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 日志级别（Trace / Debug / Info / Warn / Error / Fatal）
    /// </summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>
    /// 日志来源名称（Logger Name）
    /// </summary>
    public string Logger { get; init; } = string.Empty;

    /// <summary>
    /// 日志消息正文（含异常）
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
