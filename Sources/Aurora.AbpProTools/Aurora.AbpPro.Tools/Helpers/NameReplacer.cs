using System.Text.RegularExpressions;

namespace Aurora.AbpPro.Tools.Helpers;

/// <summary>
/// 生成框架时使用的统一名称替换工具：
/// 单次正则扫描完成全部替换，避免多遍替换造成"Aurora.Hangfire → Aurora.Aurora.Hangfire"等链式污染。
/// 替换优先级（左侧匹配优先）：长前缀优先于短前缀。
/// </summary>
public static class NameReplacer
{
    // 替换映射：按长度从长到短，长前缀必须排前面，确保 "Volo.Abp" 优先于 "Volo" 命中
    private static readonly (string Pattern, string Replacement)[] Rules = new (string, string)[]
    {
        // 大小写敏感：先大写规则
        ("Lion.AbpPro", "Aurora.AbpPro"),
        ("Volo.Abp", "Aurora.AbpPro"),
        ("DotNetCore.CAP", "Aurora.CAP"),
        // Hangfire 单独处理：仅当未被 "Aurora." 前缀保护时才替换为 "Aurora.Hangfire"
        // 通过 (?<!Aurora\.) 负回顾断言避免重复包裹
        ("Hangfire", "Aurora.Hangfire"),
        ("Lion", "Aurora"),
        ("Volo", "Aurora"),
        ("DotNetCore", "Aurora"),
        // 小写规则
        ("lion.abppro", "aurora.abppro"),
        ("volo.abp", "aurora.abppro"),
        ("dotnetcore.cap", "aurora.cap"),
        ("hangfire", "aurora.hangfire"),
        ("lion", "aurora"),
        ("volo", "aurora"),
        ("dotnetcore", "aurora"),
    };

    /// <summary>
    /// 编译后的总正则：把所有规则用 | 拼接，按规则顺序优先命中
    /// Hangfire 加 (?<!Aurora\.) 负回顾，避免把"Aurora.Hangfire"再次包成"Aurora.Aurora.Hangfire"
    /// hangfire 同理用 (?<!aurora\.)
    /// </summary>
    private static readonly Regex Combined = BuildRegex();

    private static Regex BuildRegex()
    {
        // 注意：这里把 Hangfire/hangfire 替换为带负回顾的等价模式
        var parts = new System.Collections.Generic.List<string>(Rules.Length);
        foreach (var (pattern, _) in Rules)
        {
            string escaped = Regex.Escape(pattern);
            if (pattern == "Hangfire")
            {
                escaped = @"(?<!Aurora\.)" + escaped;
            }
            else if (pattern == "hangfire")
            {
                escaped = @"(?<!aurora\.)" + escaped;
            }
            parts.Add(escaped);
        }
        var combined = string.Join("|", parts);
        return new Regex(combined, RegexOptions.Compiled);
    }

    /// <summary>
    /// 对输入字符串执行单次替换扫描，按规则优先级返回新字符串
    /// </summary>
    public static string Replace(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }
        return Combined.Replace(
            input,
            match =>
            {
                // 命中的原文按规则表查到对应替换值
                foreach (var (pattern, replacement) in Rules)
                {
                    if (string.Equals(match.Value, pattern, System.StringComparison.Ordinal))
                    {
                        return replacement;
                    }
                }
                return match.Value;
            }
        );
    }
}
