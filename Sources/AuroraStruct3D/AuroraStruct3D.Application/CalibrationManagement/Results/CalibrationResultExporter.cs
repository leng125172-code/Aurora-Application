using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using AuroraStruct3D.CalibrationManagement.Results;
using Volo.Abp;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果多格式导出器。
/// 统一将 <see cref="CalibrationResult"/> 构建为标准 <see cref="JsonObject"/> 数据源，
/// 再按目标格式（JSON / XML / YAML / TXT）渲染为字节流，便于流式下载。
/// </summary>
internal static class CalibrationResultExporter
{
    /// <summary>
    /// 按指定格式导出标定结果。
    /// </summary>
    /// <param name="result">已加载验证记录的标定结果聚合根</param>
    /// <param name="format">目标格式</param>
    /// <returns>负载字节、文件名、MIME 类型</returns>
    public static (byte[] Payload, string FileName, string ContentType) Export(
        CalibrationResult result,
        CalibrationResultExportFormat format
    )
    {
        // 统一构建中间数据源（保留与 Phase 3 JSON 导出完全一致的字段含义）
        JsonObject root = BuildExportRoot(result);

        string baseName =
            $"calibration-result-{result.CalibrationProjectId:N}-v{result.Version}";

        return format switch
        {
            CalibrationResultExportFormat.Json => (
                Encoding.UTF8.GetBytes(SerializeJson(root)),
                $"{baseName}.json",
                "application/json"
            ),
            CalibrationResultExportFormat.Xml => (
                Encoding.UTF8.GetBytes(SerializeXml(root)),
                $"{baseName}.xml",
                "application/xml"
            ),
            CalibrationResultExportFormat.Yaml => (
                Encoding.UTF8.GetBytes(SerializeYaml(root)),
                $"{baseName}.yaml",
                "application/x-yaml"
            ),
            CalibrationResultExportFormat.Txt => (
                Encoding.UTF8.GetBytes(SerializeTxt(root)),
                $"{baseName}.txt",
                "text/plain"
            ),
            _ => throw new BusinessException("Calibration:UnsupportedExportFormat").WithData(
                "format",
                format
            ),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 统一数据源构建
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 构建导出根对象：元数据 + 结果主体 + 验证记录数组。
    /// </summary>
    private static JsonObject BuildExportRoot(CalibrationResult result)
    {
        JsonObject errorStats = new()
        {
            ["overall"] = result.OverallReprojectionError,
            ["max"] = result.MaxError,
            ["min"] = result.MinError,
            ["mean"] = result.MeanError,
            ["rms"] = result.RmsError,
        };

        JsonObject resultNode = new()
        {
            ["id"] = result.Id.ToString(),
            ["calibrationProjectId"] = result.CalibrationProjectId.ToString(),
            ["calibrationDeviceId"] = result.CalibrationDeviceId.ToString(),
            ["version"] = result.Version,
            ["isActive"] = result.IsActive,
            ["computedTime"] = result.ComputedTime.ToString("O", CultureInfo.InvariantCulture),
            ["errorStatistics"] = errorStats,
            ["cameraIntrinsics"] = ParseJsonOrNull(result.CameraIntrinsicsJson),
            ["cameraExtrinsics"] = ParseJsonOrNull(result.CameraExtrinsicsJson),
            ["structuredLightCalibration"] = ParseJsonOrNull(
                result.StructuredLightCalibrationJson
            ),
        };

        JsonArray validations = new();
        foreach (
            CalibrationValidationRecord v in result.Validations.OrderByDescending(x =>
                x.ValidatedTime
            )
        )
        {
            validations.Add(
                new JsonObject
                {
                    ["id"] = v.Id.ToString(),
                    ["type"] = v.ValidationType.ToString(),
                    ["isPassed"] = v.IsPassed,
                    ["validatedTime"] = v.ValidatedTime.ToString(
                        "O",
                        CultureInfo.InvariantCulture
                    ),
                    ["metrics"] = ParseJsonOrNull(v.MetricsJson),
                    ["reportBlobName"] = v.ReportBlobName,
                    ["remarks"] = v.Remarks,
                }
            );
        }

        return new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["exportedAt"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["result"] = resultNode,
            ["validations"] = validations,
        };
    }

    /// <summary>
    /// 将存储的 JSON 字符串解析为 <see cref="JsonNode"/>；解析失败按原始字符串回退。
    /// </summary>
    private static JsonNode? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // JSON
    // ═══════════════════════════════════════════════════════════════════════

    private static string SerializeJson(JsonObject root) =>
        root.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }
        );

    // ═══════════════════════════════════════════════════════════════════════
    // XML（OpenCV FileStorage 风格：<opencv_storage> 根 + 元素名透传字段名）
    // ═══════════════════════════════════════════════════════════════════════

    private static string SerializeXml(JsonObject root)
    {
        using StringWriter sw = new(new StringBuilder(), CultureInfo.InvariantCulture);
        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
        };
        using (XmlWriter writer = XmlWriter.Create(sw, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("opencv_storage");
            WriteXmlNode(writer, root);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        return sw.ToString();
    }

    private static void WriteXmlNode(XmlWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteString(string.Empty);
                break;
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> kv in obj)
                {
                    writer.WriteStartElement(SanitizeXmlName(kv.Key));
                    WriteXmlNode(writer, kv.Value);
                    writer.WriteEndElement();
                }
                break;
            case JsonArray arr:
                foreach (JsonNode? item in arr)
                {
                    writer.WriteStartElement("item");
                    WriteXmlNode(writer, item);
                    writer.WriteEndElement();
                }
                break;
            case JsonValue val:
                writer.WriteString(JsonValueToString(val));
                break;
        }
    }

    /// <summary>
    /// 将任意 JSON 键标准化为合法 XML 元素名：首字符非字母/下划线时加 "_" 前缀。
    /// </summary>
    private static string SanitizeXmlName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }
        return XmlConvert.EncodeLocalName(name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // YAML（OpenCV FileStorage 风格：以 %YAML:1.0 头开始，2 空格缩进）
    // ═══════════════════════════════════════════════════════════════════════

    private static string SerializeYaml(JsonObject root)
    {
        StringBuilder sb = new();
        sb.AppendLine("%YAML:1.0");
        sb.AppendLine("---");
        WriteYamlObject(sb, root, indent: 0);
        return sb.ToString();
    }

    private static void WriteYamlObject(StringBuilder sb, JsonObject obj, int indent)
    {
        string pad = new(' ', indent);
        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            sb.Append(pad).Append(kv.Key).Append(':');
            WriteYamlValue(sb, kv.Value, indent + 2);
        }
    }

    private static void WriteYamlValue(StringBuilder sb, JsonNode? node, int childIndent)
    {
        switch (node)
        {
            case null:
                sb.AppendLine(" ~");
                break;
            case JsonObject inner when inner.Count == 0:
                sb.AppendLine(" {}");
                break;
            case JsonObject inner:
                sb.AppendLine();
                WriteYamlObject(sb, inner, childIndent);
                break;
            case JsonArray arr when arr.Count == 0:
                sb.AppendLine(" []");
                break;
            case JsonArray arr:
                sb.AppendLine();
                string pad = new(' ', childIndent);
                foreach (JsonNode? item in arr)
                {
                    sb.Append(pad).Append("- ");
                    if (item is JsonObject child)
                    {
                        sb.AppendLine();
                        WriteYamlObject(sb, child, childIndent + 2);
                    }
                    else
                    {
                        sb.AppendLine(FormatYamlScalar(item));
                    }
                }
                break;
            case JsonValue val:
                sb.Append(' ').AppendLine(FormatYamlScalar(val));
                break;
        }
    }

    private static string FormatYamlScalar(JsonNode? val)
    {
        if (val is null)
        {
            return "~";
        }
        string raw = JsonValueToString((JsonValue)val);
        // 含特殊字符（: # 等）或前后空白时加引号
        if (
            raw.Length == 0
            || raw.IndexOfAny([':', '#', '\n', '\r', '\t', '"', '\'']) >= 0
            || raw != raw.Trim()
        )
        {
            return "\"" + raw.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        return raw;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TXT（人类可读：分节标题 + 关键字段平铺 + 复杂子节点直接附 JSON 摘要）
    // ═══════════════════════════════════════════════════════════════════════

    private static string SerializeTxt(JsonObject root)
    {
        StringBuilder sb = new();
        sb.AppendLine("================================================================");
        sb.AppendLine("  Aurora Struct3D - Calibration Result Export");
        sb.AppendLine("================================================================");
        sb.Append("Schema Version : ").AppendLine(root["schemaVersion"]?.ToString());
        sb.Append("Exported At    : ").AppendLine(root["exportedAt"]?.ToString());
        sb.AppendLine();

        if (root["result"] is JsonObject r)
        {
            sb.AppendLine("---------------- Result Header ----------------");
            sb.Append("Result Id           : ").AppendLine(r["id"]?.ToString());
            sb.Append("Project Id          : ").AppendLine(r["calibrationProjectId"]?.ToString());
            sb.Append("Device Id           : ").AppendLine(r["calibrationDeviceId"]?.ToString());
            sb.Append("Version             : ").AppendLine(r["version"]?.ToString());
            sb.Append("Is Active           : ").AppendLine(r["isActive"]?.ToString());
            sb.Append("Computed Time       : ").AppendLine(r["computedTime"]?.ToString());
            sb.AppendLine();

            if (r["errorStatistics"] is JsonObject es)
            {
                sb.AppendLine("---------------- Reprojection Error ----------------");
                sb.Append("Overall : ").AppendLine(es["overall"]?.ToString());
                sb.Append("Max     : ").AppendLine(es["max"]?.ToString());
                sb.Append("Min     : ").AppendLine(es["min"]?.ToString());
                sb.Append("Mean    : ").AppendLine(es["mean"]?.ToString());
                sb.Append("RMS     : ").AppendLine(es["rms"]?.ToString());
                sb.AppendLine();
            }

            AppendTxtJsonSection(sb, "Camera Intrinsics", r["cameraIntrinsics"]);
            AppendTxtJsonSection(sb, "Camera Extrinsics", r["cameraExtrinsics"]);
            AppendTxtJsonSection(
                sb,
                "Structured Light Calibration",
                r["structuredLightCalibration"]
            );
        }

        if (root["validations"] is JsonArray vs && vs.Count > 0)
        {
            sb.AppendLine("---------------- Validations ----------------");
            int i = 1;
            foreach (JsonNode? v in vs)
            {
                if (v is not JsonObject vo)
                {
                    continue;
                }
                sb.Append('[').Append(i++).Append("] ");
                sb.Append("type=").Append(vo["type"]?.ToString());
                sb.Append(" passed=").Append(vo["isPassed"]?.ToString());
                sb.Append(" time=").AppendLine(vo["validatedTime"]?.ToString());
                if (!string.IsNullOrWhiteSpace(vo["remarks"]?.ToString()))
                {
                    sb.Append("    remarks: ").AppendLine(vo["remarks"]?.ToString());
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendTxtJsonSection(StringBuilder sb, string title, JsonNode? node)
    {
        sb.Append("---------------- ").Append(title).AppendLine(" ----------------");
        if (node is null)
        {
            sb.AppendLine("(empty)");
        }
        else
        {
            sb.AppendLine(
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            );
        }
        sb.AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 公共
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 将 <see cref="JsonValue"/> 转为字符串字面量（保留数值不带引号、布尔小写）。
    /// </summary>
    private static string JsonValueToString(JsonValue val)
    {
        JsonElement elem = val.Deserialize<JsonElement>();
        return elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Number => elem.GetRawText(),
            _ => elem.GetRawText(),
        };
    }
}
