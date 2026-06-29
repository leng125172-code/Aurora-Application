using System.Globalization;
using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// if/else 判断条件编译器：把 <c>params.condition</c> 的 JSON 编译为运行期布尔委托。
/// <para>
/// 支持三种形态：
/// <list type="bullet">
///   <item>布尔字面量：<c>true</c> / <c>false</c></item>
///   <item>单变量真值：<c>{ "$var": "flag" }</c></item>
///   <item>比较表达式：<c>{ "left": &lt;操作数&gt;, "op": "==|!=|&gt;|&gt;=|&lt;|&lt;=", "right": &lt;操作数&gt; }</c>
///   （操作数可为字面量或 <c>{ "$var": name }</c>）</item>
/// </list>
/// 数值优先按数值比较，否则退化为序数字符串比较。
/// </para>
/// </summary>
internal static class ConditionCompiler
{
    /// <summary>编译条件为运行期委托。</summary>
    public static Func<IWorkflowContext, bool> Compile(JsonElement condition)
    {
        switch (condition.ValueKind)
        {
            case JsonValueKind.True:
                return static _ => true;
            case JsonValueKind.False:
                return static _ => false;
            case JsonValueKind.Object:
                if (VarRef.TryGet(condition, out string varName))
                    return ctx => Truthy(ctx.Get(varName));
                return CompileComparison(condition);
            default:
                throw new WorkflowCompilationException(
                    "if/else 条件非法：应为布尔字面量、{ \"$var\": ... } 或 { left, op, right }。"
                );
        }
    }

    /// <summary>提取条件中引用的变量名（供静态校验"写前读"）。</summary>
    public static IEnumerable<string> ExtractVarRefs(JsonElement condition)
    {
        if (condition.ValueKind != JsonValueKind.Object)
            yield break;

        if (VarRef.TryGet(condition, out string single))
        {
            yield return single;
            yield break;
        }

        if (condition.TryGetProperty("left", out JsonElement l) && VarRef.TryGet(l, out string lv))
            yield return lv;
        if (condition.TryGetProperty("right", out JsonElement r) && VarRef.TryGet(r, out string rv))
            yield return rv;
    }

    private static Func<IWorkflowContext, bool> CompileComparison(JsonElement c)
    {
        if (!c.TryGetProperty("op", out JsonElement opEl) || opEl.ValueKind != JsonValueKind.String)
            throw new WorkflowCompilationException("if/else 比较条件缺少 op。");

        string op = opEl.GetString()!;
        Func<IWorkflowContext, object?> left = Operand(c, "left");
        Func<IWorkflowContext, object?> right = Operand(c, "right");

        return ctx => Evaluate(left(ctx), op, right(ctx));
    }

    private static Func<IWorkflowContext, object?> Operand(JsonElement c, string key)
    {
        if (!c.TryGetProperty(key, out JsonElement el))
            return static _ => null;

        if (VarRef.TryGet(el, out string name))
            return ctx => ctx.Get(name);

        object? literal = el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out double d) ? d : null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        return _ => literal;
    }

    private static bool Evaluate(object? left, string op, object? right) =>
        op switch
        {
            "==" => LooseEquals(left, right),
            "!=" => !LooseEquals(left, right),
            ">" => Compare(left, right) > 0,
            ">=" => Compare(left, right) >= 0,
            "<" => Compare(left, right) < 0,
            "<=" => Compare(left, right) <= 0,
            _ => throw new WorkflowCompilationException($"if/else 不支持的运算符 '{op}'。"),
        };

    private static bool LooseEquals(object? l, object? r)
    {
        if (TryDouble(l, out double dl) && TryDouble(r, out double dr))
            return dl.Equals(dr);
        return string.Equals(Str(l), Str(r), StringComparison.Ordinal);
    }

    private static int Compare(object? l, object? r)
    {
        if (TryDouble(l, out double dl) && TryDouble(r, out double dr))
            return dl.CompareTo(dr);
        return string.Compare(Str(l), Str(r), StringComparison.Ordinal);
    }

    private static bool Truthy(object? v) =>
        v switch
        {
            null => false,
            bool b => b,
            string s => bool.TryParse(s, out bool b) ? b : !string.IsNullOrEmpty(s),
            _ => TryDouble(v, out double d) ? d != 0 : true,
        };

    private static bool TryDouble(object? v, out double d)
    {
        switch (v)
        {
            case double dd:
                d = dd;
                return true;
            case float f:
                d = f;
                return true;
            case int i:
                d = i;
                return true;
            case long l:
                d = l;
                return true;
            case string s:
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            default:
                d = 0;
                return false;
        }
    }

    private static string Str(object? v) =>
        v switch
        {
            null => "",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
}
