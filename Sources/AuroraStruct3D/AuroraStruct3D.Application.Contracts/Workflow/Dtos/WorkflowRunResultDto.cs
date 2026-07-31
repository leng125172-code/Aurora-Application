using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>单个结果变量的摘要。</summary>
public class WorkflowVariableResultDto
{
    /// <summary>变量名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 值类型令牌（int/long/float/double/decimal/bool/string/blob/object/array/datetime/guid）。
    /// 原始 Mat/PointCloudData 仅用于提示调用方先存 Blob。
    /// </summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>
    /// 标量值的内部文本表示。仅供服务端运行时快照使用，不直接写入 API 响应。
    /// </summary>
    [JsonIgnore]
    public string? ScalarValue { get; set; }

    /// <summary>
    /// 对外返回的原生 JSON 值。JSON 对象/数组不再作为转义字符串返回。
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value => ToJsonValue(ScalarValue, ValueType);

    /// <summary>若已写回 Redis 暂存，则为暂存 key（前端可经 variable-stage 接口取回）；否则 null。</summary>
    public string? StagedKey { get; set; }

    internal static object? ToJsonValue(string? scalarValue, string valueType)
    {
        if (scalarValue is null)
        {
            return null;
        }

        switch (valueType)
        {
            case "int":
                return int.TryParse(
                    scalarValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int intValue
                )
                    ? intValue
                    : scalarValue;
            case "long":
                return long.TryParse(
                    scalarValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long longValue
                )
                    ? longValue
                    : scalarValue;
            case "double":
                return double.TryParse(
                    scalarValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double doubleValue
                )
                    ? doubleValue
                    : scalarValue;
            case "float":
                return float.TryParse(
                    scalarValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float floatValue
                )
                    ? floatValue
                    : scalarValue;
            case "decimal":
                return decimal.TryParse(
                    scalarValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal decimalValue
                )
                    ? decimalValue
                    : scalarValue;
            case "bool":
                return bool.TryParse(scalarValue, out bool boolValue) ? boolValue : scalarValue;
            case "object":
            case "array":
                try
                {
                    return JsonNode.Parse(scalarValue);
                }
                catch (JsonException)
                {
                    return scalarValue;
                }
            case "datetime":
            case "guid":
                return scalarValue;
        }

        if (valueType == "string")
        {
            try
            {
                JsonNode? node = JsonNode.Parse(scalarValue);
                if (node is JsonObject or JsonArray)
                {
                    return node;
                }
            }
            catch (JsonException)
            {
                // 普通字符串保持原样。
            }
        }

        return scalarValue;
    }
}

/// <summary>工作流执行结果。</summary>
public class WorkflowRunResultDto
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>顶层变量数量。</summary>
    public int VariableCount { get; set; }

    /// <summary>执行耗时（毫秒）。</summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 运行时实例 ID（在线变量池模式下返回）。
    /// </summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>所有顶层变量的摘要。</summary>
    public List<WorkflowVariableResultDto> Variables { get; set; } = new();

    /// <summary>区域测量结果（高度差检测工作流专用）。</summary>
    public List<RegionMeasurementResultDto>? Regions { get; set; }
}

/// <summary>单个区域的测量结果。</summary>
public class RegionMeasurementResultDto
{
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
