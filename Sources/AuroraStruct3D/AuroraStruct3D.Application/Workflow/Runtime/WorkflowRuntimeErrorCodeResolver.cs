using System.Text.RegularExpressions;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 从算子异常消息中提取稳定的机器错误码。算子统一使用 [ERROR_CODE] 前缀，
/// 运行时在保留原始中文诊断的同时将该代码提升到 DTO 的 ErrorCode 字段。
/// </summary>
internal static class WorkflowRuntimeErrorCodeResolver
{
    private static readonly Regex BracketedCode = new(
        @"\[(?<code>[A-Z][A-Z0-9_]{2,63})\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public static string? Resolve(string? message, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Match match = BracketedCode.Match(message);
            if (match.Success)
                return match.Groups["code"].Value;
        }

        return fallback;
    }
}
