using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace KtechMotorCli.Logging;

/// <summary>
/// 仿瓴控官方 Demo 的控制台输出格式化器。
/// <para>识别 <see cref="AuroraStruct3D.RS485.Protocol.RS485Port"/> 的 TX/RX 帧日志，
/// 把冗长的 "[KTECH-PORT] RS485 TX [COM3]: 3E 1F 01 00 5E" 简化为 Demo 同款的
/// "TX:  3E 1F 01 00 5E" / "RX:  3E 1F 01 02 60 11 20 31"；其他日志保留时间戳 + 级别。</para>
/// </summary>
internal sealed class DemoConsoleFormatter : ConsoleFormatter
{
    /// <summary>Formatter 名称，注册到 <see cref="ConsoleLoggerOptions.FormatterName"/></summary>
    public new const string Name = "demo";

    /// <summary>匹配 TX 帧的子串</summary>
    private const string TxMarker = " RS485 TX [";

    /// <summary>匹配 RX 帧的子串</summary>
    private const string RxMarker = " RS485 RX [";

    /// <summary>RS485Port 日志分类名</summary>
    private const string RS485PortCategory = "AuroraStruct3D.RS485.Protocol.RS485Port";

    /// <summary>初始化默认实例</summary>
    public DemoConsoleFormatter()
        : base(Name) { }

    /// <inheritdoc/>
    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter
    )
    {
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        // 优先识别 RS485 TX/RX 帧 → 输出 Demo 同款紧凑格式
        if (TryWriteFrameLine(message, textWriter))
        {
            return;
        }

        // 其他日志：HH:mm:ss.fff [级别] 分类名: 消息
        textWriter.Write(DateTime.Now.ToString("HH:mm:ss.fff "));
        textWriter.Write('[');
        textWriter.Write(LevelShort(logEntry.LogLevel));
        textWriter.Write("] ");

        // 仅在非 RS485Port 类目下打印分类名，减少噪声
        if (!string.IsNullOrEmpty(logEntry.Category) && logEntry.Category != RS485PortCategory)
        {
            textWriter.Write(ShortCategory(logEntry.Category));
            textWriter.Write(": ");
        }

        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }

    /// <summary>
    /// 尝试把 RS485 TX/RX 日志重排为 "TX:  hex" / "RX:  hex"；命中返回 true。
    /// </summary>
    private static bool TryWriteFrameLine(string message, TextWriter textWriter)
    {
        int txIndex = message.IndexOf(TxMarker, StringComparison.Ordinal);
        if (txIndex >= 0)
        {
            string? hex = ExtractHexAfterMarker(message, txIndex + TxMarker.Length);
            if (hex is not null)
            {
                textWriter.Write("TX:  ");
                textWriter.WriteLine(hex);
                return true;
            }
        }

        int rxIndex = message.IndexOf(RxMarker, StringComparison.Ordinal);
        if (rxIndex >= 0)
        {
            string? hex = ExtractHexAfterMarker(message, rxIndex + RxMarker.Length);
            if (hex is not null)
            {
                textWriter.Write("RX:  ");
                textWriter.WriteLine(hex);
                return true;
            }
        }

        return false;
    }

    /// <summary>定位 "PORT]: " 后的 hex 字符串，找不到返回 null</summary>
    private static string? ExtractHexAfterMarker(string message, int startIndex)
    {
        int closeBracket = message.IndexOf("]: ", startIndex, StringComparison.Ordinal);
        if (closeBracket < 0)
        {
            return null;
        }
        return message[(closeBracket + 3)..].TrimEnd();
    }

    /// <summary>日志级别缩写（与默认 SimpleConsole 一致）</summary>
    private static string LevelShort(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

    /// <summary>取分类名的最后一段（去命名空间），降低输出宽度</summary>
    private static string ShortCategory(string category)
    {
        int dot = category.LastIndexOf('.');
        return dot >= 0 && dot + 1 < category.Length ? category[(dot + 1)..] : category;
    }
}
