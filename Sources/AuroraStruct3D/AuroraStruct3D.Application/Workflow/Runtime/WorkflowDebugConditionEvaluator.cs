using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp;

namespace AuroraStruct3D.Workflow.Runtime;

internal static partial class WorkflowDebugConditionEvaluator
{
    private const int MaxExpressionLength = 2048;
    private const int MaxClauseCount = 64;
    private const int MaxPathDepth = 32;
    private static readonly TimeSpan MaxEvaluationDuration = TimeSpan.FromMilliseconds(50);

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*)\s*(==|!=|>=|<=|>|<)\s*(null|true|false|-?\d+(?:\.\d+)?|""(?:\\.|[^""])*"")$")]
    private static partial Regex ComparisonPattern();

    public static bool Evaluate(string? expression, WorkflowContext context)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;
        if (expression.Length > MaxExpressionLength)
            throw new UserFriendlyException($"条件断点表达式不能超过 {MaxExpressionLength} 个字符。");
        if (expression.Contains('(') || Regex.IsMatch(expression, @"(?<![!<>=])=(?!=)"))
            throw new UserFriendlyException("条件断点不允许方法调用或赋值。");

        Stopwatch stopwatch = Stopwatch.StartNew();
        string[] orParts = expression.Split("||", StringSplitOptions.TrimEntries);
        int clauseCount = 0;
        foreach (string orPart in orParts)
        {
            bool allMatched = true;
            foreach (string part in orPart.Split("&&", StringSplitOptions.TrimEntries))
            {
                clauseCount++;
                if (clauseCount > MaxClauseCount)
                    throw new UserFriendlyException($"条件断点最多允许 {MaxClauseCount} 个比较子句。");
                if (stopwatch.Elapsed > MaxEvaluationDuration)
                    throw new UserFriendlyException("条件断点求值超时。");
                if (!EvaluateComparison(part, context))
                    allMatched = false;
            }
            if (allMatched)
                return true;
        }
        return false;
    }

    private static bool EvaluateComparison(string expression, WorkflowContext context)
    {
        bool negate = expression.StartsWith('!');
        if (negate)
            expression = expression[1..].Trim();
        Match match = ComparisonPattern().Match(expression);
        if (!match.Success)
        {
            object? truthy = Resolve(expression, context);
            bool value = truthy is bool boolean ? boolean : truthy is not null;
            return negate ? !value : value;
        }
        object? left = Resolve(match.Groups[1].Value, context);
        object? right = ParseLiteral(match.Groups[3].Value);
        int comparison = Compare(left, right);
        bool result = match.Groups[2].Value switch
        {
            "==" => EqualsNormalized(left, right),
            "!=" => !EqualsNormalized(left, right),
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            _ => false,
        };
        return negate ? !result : result;
    }

    private static object? Resolve(string path, WorkflowContext context)
    {
        int pathDepth = path.Count(character => character is '.' or '[');
        if (pathDepth > MaxPathDepth)
            throw new UserFriendlyException($"条件断点变量路径深度不能超过 {MaxPathDepth}。");
        WorkflowOutputAccessor accessor = WorkflowOutputAccessor.Compile(path);
        object? root = context.Contains(accessor.RootVariableName)
            ? context.Get(accessor.RootVariableName)
            : null;
        return accessor.TryGetValue(root, out object? value) ? value : null;
    }

    private static object? ParseLiteral(string value) => value switch
    {
        "null" => null,
        "true" => true,
        "false" => false,
        _ when value.StartsWith('"') => JsonSerializer.Deserialize<string>(value),
        _ => decimal.Parse(value, CultureInfo.InvariantCulture),
    };

    private static bool EqualsNormalized(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (decimal.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out decimal l)
            && decimal.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out decimal r))
            return l == r;
        return Equals(left, right) || string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static int Compare(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null ? (right is null ? 0 : -1) : 1;
        if (decimal.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out decimal l)
            && decimal.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out decimal r))
            return l.CompareTo(r);
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }
}

public sealed class WorkflowBreakpointState
{
    public required WorkflowBreakpointDto Definition { get; init; }
    public int CurrentHitCount { get; set; }
}
