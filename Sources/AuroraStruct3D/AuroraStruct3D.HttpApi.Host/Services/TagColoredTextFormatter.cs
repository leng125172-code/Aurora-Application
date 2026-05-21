using Serilog.Events;
using Serilog.Formatting;

namespace AuroraStruct3D.Services;

/// <summary>
/// 自定义 Serilog 控制台文本格式化器。
/// 检测日志消息首部的 [Tag] 前缀，使用 ANSI 24-bit 真彩色着色输出，
/// 其余内容保持默认颜色，异常信息以红色显示。
/// </summary>
public sealed class TagColoredTextFormatter : ITextFormatter
{
    // ─── 标签 → RGB 颜色映射 ─────────────────────────────────────────────────
    private static readonly (string Tag, byte R, byte G, byte B)[] TagColors =
    [
        ("[Servos]",      247, 107, 115),
        ("[Servo drive]", 247, 107, 115),
        ("[Cameras]",      87, 204, 153),
        ("[Projector]",    58, 134, 255),
        ("[Serial port]", 110, 116, 128),
    ];

    // ─── 日志级别 → RGB 颜色映射 ─────────────────────────────────────────────
    private static readonly Dictionary<LogEventLevel, (byte R, byte G, byte B)> LevelColors = new()
    {
        [LogEventLevel.Verbose]     = (128, 128, 128),
        [LogEventLevel.Debug]       = (150, 150, 150),
        [LogEventLevel.Information] = (90,  186, 255),
        [LogEventLevel.Warning]     = (249, 204, 131),
        [LogEventLevel.Error]       = (247, 107, 115),
        [LogEventLevel.Fatal]       = (255,  80,  80),
    };

    /// <summary>将 LogEvent 格式化为带 ANSI 颜色的控制台文本行</summary>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        // 时间戳（灰暗色）
        string timestamp = logEvent.Timestamp.ToString("HH:mm:ss.fff");
        string levelAbbr = GetLevelAbbr(logEvent.Level);
        string levelColored = ApplyColor(levelAbbr, LevelColors[logEvent.Level]);
        string message = logEvent.RenderMessage();
        string coloredMessage = ApplyTagColor(message);

        output.Write($"\x1B[2m{timestamp}\x1B[0m [{levelColored}] {coloredMessage}");

        // 附加异常信息（红色）
        if (logEvent.Exception is not null)
        {
            output.WriteLine();
            output.Write(ApplyColor(logEvent.Exception.ToString(), (200, 80, 80)));
        }

        output.WriteLine();
    }

    // ─── 私有辅助方法 ────────────────────────────────────────────────────────

    /// <summary>若消息以已知标签开头，对标签部分施加 ANSI 24-bit 前景色</summary>
    private static string ApplyTagColor(string message)
    {
        foreach ((string tag, byte r, byte g, byte b) in TagColors)
        {
            if (!message.StartsWith(tag, StringComparison.Ordinal))
                continue;

            string rest = message[tag.Length..];
            return $"{ApplyColor(tag, (r, g, b))}{rest}";
        }

        return message;
    }

    /// <summary>使用 ANSI 24-bit 真彩色包装文本片段</summary>
    private static string ApplyColor(string text, (byte R, byte G, byte B) color)
        => $"\x1B[38;2;{color.R};{color.G};{color.B}m{text}\x1B[0m";

    /// <summary>将 Serilog 日志级别转换为 3 字符缩写</summary>
    private static string GetLevelAbbr(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose     => "VRB",
        LogEventLevel.Debug       => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning     => "WRN",
        LogEventLevel.Error       => "ERR",
        LogEventLevel.Fatal       => "FTL",
        _                         => "???",
    };
}
