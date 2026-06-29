using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流配置参数值的类型强转辅助。
/// <para>
/// 同时服务两条路径：
/// <list type="bullet">
///   <item><b>编译期字面量</b>：params 中的 <see cref="JsonElement"/> → 算子构造函数参数 CLR 类型；</item>
///   <item><b>运行期 <c>$var</c></b>：从上下文取得的对象值 → 同一目标类型（数值宽化、枚举名解析等）。</item>
/// </list>
/// 强转尽力而为：目标类型无法解析或转换失败时，返回拆箱后的原值，交由 <c>Activator.CreateInstance</c>
/// 做最终类型校验（不匹配会抛出，便于定位）。
/// </para>
/// </summary>
public static class ValueCoercion
{
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 将原始值强转为指定 CLR 类型全名所表示的类型。
    /// </summary>
    /// <param name="raw">原始值：可为 <see cref="JsonElement"/>（字面量）或任意运行期对象（<c>$var</c> 取值）。</param>
    /// <param name="clrTypeName">目标 CLR 类型全名（来自 <c>ConfigParameterDescriptor.ParameterTypeName</c>）。</param>
    public static object? Coerce(object? raw, string? clrTypeName)
    {
        Type? target = string.IsNullOrWhiteSpace(clrTypeName) ? null : ResolveType(clrTypeName!);
        return target is null ? Unwrap(raw) : Coerce(raw, target);
    }

    /// <summary>
    /// 将原始值强转为指定目标类型。
    /// </summary>
    public static object? Coerce(object? raw, Type target)
    {
        ArgumentNullException.ThrowIfNull(target);

        object? value = Unwrap(raw);
        Type t = Nullable.GetUnderlyingType(target) ?? target;

        if (value is null)
            return t.IsValueType ? Activator.CreateInstance(t) : null;

        if (t.IsInstanceOfType(value))
            return value;

        if (t.IsEnum)
            return CoerceEnum(value, t);

        if (t == typeof(string))
            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

        try
        {
            return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            // 无法转换：返回拆箱原值，最终类型校验交给算子构造函数。
            return value;
        }
    }

    /// <summary>
    /// 解析 CLR 类型全名为 <see cref="Type"/>：先试 <see cref="Type.GetType(string)"/>，
    /// 再遍历已加载程序集按 FullName 匹配。结果缓存（含未命中的 null）。
    /// </summary>
    public static Type? ResolveType(string clrTypeName)
    {
        return TypeCache.GetOrAdd(
            clrTypeName,
            static name =>
            {
                Type? t = Type.GetType(name, throwOnError: false);
                if (t is not null)
                    return t;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = assembly.GetType(name, throwOnError: false);
                    if (t is not null)
                        return t;
                }

                return null;
            }
        );
    }

    /// <summary>
    /// 将 <see cref="JsonElement"/> 拆箱为最贴近的 CLR 原值；非 JSON 值原样返回。
    /// </summary>
    private static object? Unwrap(object? raw)
    {
        if (raw is not JsonElement je)
            return raw;

        switch (je.ValueKind)
        {
            case JsonValueKind.String:
                return je.GetString();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return je.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Number:
                if (je.TryGetInt64(out long l))
                    return l;
                return je.GetDouble();
            default:
                // 对象/数组（配置项不应出现，$var 已在编译器层处理）：退化为原文。
                return je.GetRawText();
        }
    }

    /// <summary>将名称字符串或数值强转为枚举值。</summary>
    private static object CoerceEnum(object value, Type enumType)
    {
        if (value is string s)
            return Enum.Parse(enumType, s, ignoreCase: true);

        // 数值（含 long/double 拆箱）→ 先转底层整型再 ToObject。
        object asUnderlying = Convert.ChangeType(
            value,
            Enum.GetUnderlyingType(enumType),
            CultureInfo.InvariantCulture
        );
        return Enum.ToObject(enumType, asUnderlying);
    }
}
