using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuroraStruct3D.OpenCV.Workflow.Values;

/// <summary>
/// 工作流变量值 ↔ 字节 的统一编解码入口，按 <see cref="WorkflowValueTypes"/> 令牌分发。
/// <para>
/// 用于 Redis 暂存与内存执行上下文之间的桥接：把活的 CLR 值（Mat / 点云 / 标量）序列化为字节
/// 存入 Redis，或从字节还原为 CLR 值注入上下文。Mat / 点云用原始二进制精确往返；标量用
/// 不变区域性文本。
/// </para>
/// </summary>
public static class WorkflowValueSerializer
{
    /// <summary>根据运行期值推断其 ValueType 令牌。</summary>
    public static string InferValueType(object? value) =>
        value switch
        {
            Mat => WorkflowValueTypes.Mat,
            MatImg => WorkflowValueTypes.Mat,
            PointCloudData => WorkflowValueTypes.PointCloud,
            bool => WorkflowValueTypes.Bool,
            int => WorkflowValueTypes.Int,
            long => WorkflowValueTypes.Long,
            float => WorkflowValueTypes.Float,
            double => WorkflowValueTypes.Double,
            decimal => WorkflowValueTypes.Decimal,
            DateTime or DateTimeOffset => WorkflowValueTypes.DateTime,
            Guid => WorkflowValueTypes.Guid,
            string or char or Enum => WorkflowValueTypes.String,
            InspectionResultBase or ProductInspectionResult => WorkflowValueTypes.Object,
            object registered when WorkflowTypeRegistry.IsRegistered(registered.GetType()) =>
                WorkflowValueTypes.Object,
            System.Collections.IDictionary => WorkflowValueTypes.Object,
            JsonArray or Array or System.Collections.IEnumerable
                when value is not string => WorkflowValueTypes.Array,
            JsonObject or JsonElement { ValueKind: JsonValueKind.Object } =>
                WorkflowValueTypes.Object,
            JsonElement { ValueKind: JsonValueKind.Array } => WorkflowValueTypes.Array,
            _ => WorkflowValueTypes.String,
        };

    /// <summary>把值序列化为字节（按给定 ValueType）。</summary>
    public static byte[] Serialize(object? value, string valueType)
    {
        switch (valueType)
        {
            case WorkflowValueTypes.Mat:
                Mat mat = value switch
                {
                    Mat m => m,
                    MatImg mi => mi.Mat ?? new Mat(),
                    _ => throw new ArgumentException(
                        $"ValueType '{valueType}' 期望 Mat/MatImg，实际为 {value?.GetType().FullName ?? "null"}。"
                    ),
                };
                return MatBinary.Encode(mat);

            case WorkflowValueTypes.PointCloud:
                if (value is not PointCloudData cloud)
                {
                    throw new ArgumentException(
                        $"ValueType '{valueType}' 期望 PointCloudData，实际为 {value?.GetType().FullName ?? "null"}。"
                    );
                }
                return PointCloudBinary.Encode(cloud);

            default: // 标量
                return Encoding.UTF8.GetBytes(ScalarToString(value) ?? string.Empty);
        }
    }

    /// <summary>把字节还原为 CLR 值（按给定 ValueType）。</summary>
    public static object? Deserialize(byte[] data, string valueType)
    {
        switch (valueType)
        {
            case WorkflowValueTypes.Mat:
                return MatBinary.Decode(data);
            case WorkflowValueTypes.PointCloud:
                return PointCloudBinary.Decode(data);

            case WorkflowValueTypes.Int:
                return int.Parse(Text(data), CultureInfo.InvariantCulture);
            case WorkflowValueTypes.Long:
                return long.Parse(Text(data), CultureInfo.InvariantCulture);
            case WorkflowValueTypes.Double:
                return double.Parse(Text(data), CultureInfo.InvariantCulture);
            case WorkflowValueTypes.Float:
                return float.Parse(Text(data), CultureInfo.InvariantCulture);
            case WorkflowValueTypes.Decimal:
                return decimal.Parse(Text(data), CultureInfo.InvariantCulture);
            case WorkflowValueTypes.Bool:
                return bool.Parse(Text(data));
            case WorkflowValueTypes.DateTime:
                return DateTimeOffset.Parse(
                    Text(data),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                );
            case WorkflowValueTypes.Guid:
                return Guid.Parse(Text(data));
            case WorkflowValueTypes.Object:
            case WorkflowValueTypes.Array:
                return JsonNode.Parse(Text(data));

            default: // string
                return Text(data);
        }
    }

    /// <summary>把标量值格式化为不变区域性字符串（也用于结果摘要展示）。</summary>
    public static string? ScalarToString(object? value) =>
        value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset =>
                dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            JsonNode jsonNode => jsonNode.ToJsonString(),
            JsonElement jsonElement => jsonElement.GetRawText(),
            Array or System.Collections.IEnumerable when value is not string =>
                TrySerializeJson(value, out string? json) ? json : null,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => TrySerializeJson(value, out string? json) ? json : value.ToString(),
        };

    /// <summary>
    /// 尝试把运行时值转换为 JSON。Mat、PointCloudData 或包含这些非托管值的复合对象
    /// 不允许穿透到 HTTP/SignalR 序列化边界。
    /// </summary>
    public static bool TrySerializeJson(object? value, out string? json)
    {
        try
        {
            json = JsonSerializer.Serialize(value);
            return true;
        }
        catch (Exception ex) when (IsUnsupportedJsonValue(ex))
        {
            json = null;
            return false;
        }
    }

    /// <summary>尝试把运行时值转换为可安全遍历的 JSON 节点。</summary>
    public static bool TrySerializeToJsonNode(object? value, out JsonNode? node)
    {
        try
        {
            node = JsonSerializer.SerializeToNode(value);
            return true;
        }
        catch (Exception ex) when (IsUnsupportedJsonValue(ex))
        {
            node = null;
            return false;
        }
    }

    /// <summary>尝试计算 JSON 字节数；非托管运行时值返回 false。</summary>
    public static bool TryGetJsonUtf8ByteCount(object? value, out long byteCount)
    {
        try
        {
            byteCount = JsonSerializer.SerializeToUtf8Bytes(value).LongLength;
            return true;
        }
        catch (Exception ex) when (IsUnsupportedJsonValue(ex))
        {
            byteCount = 0;
            return false;
        }
    }

    private static bool IsUnsupportedJsonValue(Exception ex) =>
        ex is JsonException or NotSupportedException or InvalidOperationException;

    private static string Text(byte[] data) => Encoding.UTF8.GetString(data);
}
