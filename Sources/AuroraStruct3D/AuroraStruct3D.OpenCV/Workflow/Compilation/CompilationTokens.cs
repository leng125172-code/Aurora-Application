using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>持久化 JSON 中的固定节点类型 / 容器种类 / 哨兵标记常量。</summary>
internal static class NodeTypeTokens
{
    /// <summary>流程起点。</summary>
    public const string StartNode = "start-node";

    /// <summary>流程终点。</summary>
    public const string EndNode = "end-node";

    /// <summary>容器节点统一持久化类型。</summary>
    public const string FlowContainer = "flow_container";

    /// <summary>变量赋值内置节点。</summary>
    public const string Assign = "builtin::assign";

    /// <summary>For 循环容器种类（<c>params.containerKind</c>）。</summary>
    public const string ForLoop = "builtin::for_loop";

    /// <summary>If/Else 容器种类（<c>params.containerKind</c>）。</summary>
    public const string IfElse = "builtin::if_else";

    /// <summary>params 值中表示「引用工作流变量」的哨兵键。</summary>
    public const string VarSentinel = "$var";

    /// <summary>参数/绑定来源：变量引用。</summary>
    public const string SourceVariable = "variable";

    /// <summary>参数/绑定来源：字面量。</summary>
    public const string SourceLiteral = "literal";
}

/// <summary>变量引用哨兵 <c>{ "$var": "name" }</c> 的识别辅助。</summary>
internal static class VarRef
{
    /// <summary>
    /// 判断一个 params 值是否为变量引用哨兵，并取出被引用的变量名。
    /// </summary>
    public static bool TryGet(JsonElement value, out string variableName)
    {
        variableName = string.Empty;

        if (
            value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(NodeTypeTokens.VarSentinel, out JsonElement name)
            && name.ValueKind == JsonValueKind.String
        )
        {
            variableName = name.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(variableName);
        }

        return false;
    }
}

/// <summary>参数来源标记解析辅助。</summary>
public static class BindingSource
{
    public static bool TryResolveSource(
        Dictionary<string, string>? sourceMap,
        string key,
        out bool isVariable,
        out string error
    )
    {
        isVariable = false;
        error = string.Empty;

        if (sourceMap is null)
        {
            error = "missing source map";
            return false;
        }

        if (sourceMap is null || !sourceMap.TryGetValue(key, out string? source))
        {
            error = $"missing source key '{key}'";
            return false;
        }

        if (string.Equals(source, NodeTypeTokens.SourceVariable, StringComparison.Ordinal))
        {
            isVariable = true;
            return true;
        }

        if (string.Equals(source, NodeTypeTokens.SourceLiteral, StringComparison.Ordinal))
        {
            isVariable = false;
            return true;
        }

        error =
            $"invalid source '{source}' for key '{key}', allowed values: {NodeTypeTokens.SourceLiteral}, {NodeTypeTokens.SourceVariable}";
        return false;
    }

    public static bool TryGetParamVariableNameStrict(
        Dictionary<string, string>? sourceMap,
        string key,
        JsonElement value,
        out string variableName,
        out string error
    )
    {
        variableName = string.Empty;
        error = string.Empty;

        if (!TryResolveSource(sourceMap, key, out bool isVariable, out error))
        {
            return false;
        }

        if (!isVariable)
        {
            return false;
        }

        if (VarRef.TryGet(value, out variableName))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            variableName = value.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(variableName))
            {
                return true;
            }
        }

        error =
            $"value for key '{key}' is flagged as variable but cannot be resolved as variable name";
        return false;
    }
}
