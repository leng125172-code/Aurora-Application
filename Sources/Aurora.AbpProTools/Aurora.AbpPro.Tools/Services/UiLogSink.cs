using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using Aurora.AbpPro.Tools.Models;
using NLog;
using NLog.Targets;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// UI 日志接收器接口：缓冲结构化日志并向订阅方推送
/// </summary>
public interface IUiLogSink
{
    /// <summary>
    /// 新日志写入事件（在 UI 线程触发）
    /// </summary>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>
    /// 日志被清空事件（在 UI 线程触发）
    /// </summary>
    event EventHandler? Cleared;

    /// <summary>
    /// 当前完整日志快照（用于订阅时还原历史）
    /// </summary>
    IReadOnlyList<LogEntry> Snapshot { get; }

    /// <summary>
    /// 清空缓冲
    /// </summary>
    void Clear();

    /// <summary>
    /// 由 NLog Target 调用，向订阅者派发一条日志
    /// </summary>
    void Push(LogEntry entry);
}

/// <summary>
/// UI 日志接收器实现：单例服务，自身维护历史缓冲，
/// 即使订阅方（页面 ViewModel）被销毁重建，日志依旧不丢失。
/// </summary>
public class UiLogSink : IUiLogSink
{
    /// <summary>
    /// 缓冲最大条数；超过后从头部移除最旧条目
    /// </summary>
    private const int MaxEntryCount = 1_200;

    private readonly object _bufferLock = new();
    private readonly List<LogEntry> _entries = new();

    public event EventHandler<LogEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public IReadOnlyList<LogEntry> Snapshot
    {
        get
        {
            lock (_bufferLock)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_bufferLock)
        {
            _entries.Clear();
        }
        DispatchClear();
    }

    public void Push(LogEntry entry)
    {
        lock (_bufferLock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntryCount)
            {
                // 超出上限：逐条移除最旧的那一条
                _entries.RemoveRange(0, _entries.Count - MaxEntryCount);
            }
        }
        DispatchEntry(entry);
    }

    /// <summary>
    /// 在 UI 线程派发新条目事件
    /// </summary>
    private void DispatchEntry(LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            EntryAdded?.Invoke(this, entry);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => EntryAdded?.Invoke(this, entry)));
        }
    }

    /// <summary>
    /// 在 UI 线程派发清空事件
    /// </summary>
    private void DispatchClear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Cleared?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => Cleared?.Invoke(this, EventArgs.Empty)));
        }
    }
}

/// <summary>
/// NLog 自定义 Target：将日志条目转发到 IUiLogSink 单例
/// 注意：必须为无参构造函数，NLog 反射创建时使用
/// </summary>
[Target("UiLogSink")]
public class UiLogSinkTarget : TargetWithLayout
{
    /// <summary>
    /// 缓冲在 App.Services 就绪前产生的日志，避免丢失启动期日志
    /// </summary>
    private static readonly ConcurrentQueue<LogEntry> PendingEntries = new();

    protected override void Write(LogEventInfo logEvent)
    {
        // 格式化消息正文（含异常）
        var message = logEvent.FormattedMessage ?? string.Empty;
        if (logEvent.Exception is not null)
        {
            message = string.IsNullOrEmpty(message)
                ? logEvent.Exception.ToString()
                : message + " " + logEvent.Exception;
        }

        var entry = new LogEntry
        {
            Timestamp = logEvent.TimeStamp,
            Level = logEvent.Level?.Name ?? string.Empty,
            Logger = logEvent.LoggerName ?? string.Empty,
            Message = message,
        };

        var sink = TryGetSink();
        if (sink is null)
        {
            PendingEntries.Enqueue(entry);
            return;
        }

        // 把启动前的缓冲条目一并刷出
        while (PendingEntries.TryDequeue(out var pending))
        {
            sink.Push(pending);
        }
        sink.Push(entry);
    }

    /// <summary>
    /// 从 App.Services 安全获取 UI 日志接收器（启动早期可能尚未注入）
    /// </summary>
    private static IUiLogSink? TryGetSink()
    {
        try
        {
            return App.Services?.GetService(typeof(IUiLogSink)) as IUiLogSink;
        }
        catch
        {
            return null;
        }
    }
}
