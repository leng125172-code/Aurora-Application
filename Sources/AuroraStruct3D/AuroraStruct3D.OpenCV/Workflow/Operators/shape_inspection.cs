using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Operators;

/// <summary>
/// 将圆形、矩形等底层检测算子的数量和结果转换为统一的工作流判定结果。
/// </summary>
[Guid("d68277b0-a69b-4cf1-804b-302933accdc8")]
[Category("工作流检测")]
[DisplayName("形状检测判定")]
[Description("按期望形状和数量判定底层形状检测结果，输出统一的 OK、NG 或 UNKNOWN。")]
public sealed class shape_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new VisionParameter<int>
            {
                ParameterName = "detected_count",
                DisplayName = "检测数量",
                ParameterType = typeof(int),
            },
            new VisionParameter<string>
            {
                ParameterName = "detection_json",
                DisplayName = "形状检测详情",
                ParameterType = typeof(string),
            },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            NumberOutput("detected_count", "实际数量"),
            BoolOutput("is_valid", "结果是否有效"),
            BoolOutput("is_ok", "是否合格"),
            StringOutput("inspection_status", "检测状态"),
            StringOutput("result_json", "形状判定结果", PortControlType.Download),
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "expectedShape",
                DisplayName = "期望形状",
                ParameterType = typeof(string),
                DefaultValue = "rectangle",
                ValueLimit = new[] { "rectangle", "circle", "contour", "custom" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "expectedCount",
                DisplayName = "期望数量",
                ParameterType = typeof(int),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "allowAdditional",
                DisplayName = "允许多检",
                ParameterType = typeof(bool),
                DefaultValue = "false",
                Required = false,
                ControlType = PortControlType.Switch,
            },
        ];

    private readonly string _expectedShape;
    private readonly int _expectedCount;
    private readonly bool _allowAdditional;

    public shape_inspection(
        string expectedShape = "rectangle",
        int expectedCount = 1,
        bool allowAdditional = false
    )
    {
        _expectedShape = string.IsNullOrWhiteSpace(expectedShape)
            ? throw new ArgumentException("期望形状不能为空。", nameof(expectedShape))
            : expectedShape.Trim().ToLowerInvariant();
        if (_expectedShape is not ("rectangle" or "circle" or "contour" or "custom"))
            throw new ArgumentException("期望形状配置无效。", nameof(expectedShape));
        if (expectedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedCount));

        _expectedCount = expectedCount;
        _allowAdditional = allowAdditional;
    }

    public void Execute(IWorkflowContext context)
    {
        bool hasDetectedCount = context.Contains("detected_count");
        int detectedCount = hasDetectedCount ? context.Get<int>("detected_count") : 0;
        bool isValid = hasDetectedCount && detectedCount >= 0;
        bool isOk =
            isValid
            && (
                _allowAdditional
                    ? detectedCount >= _expectedCount
                    : detectedCount == _expectedCount
            );
        string status = !isValid ? "UNKNOWN" : isOk ? "OK" : "NG";
        JsonElement? detail = ParseJson(context.Get<string>("detection_json"));

        context.Set("detected_count", detectedCount);
        context.Set("is_valid", isValid);
        context.Set("is_ok", isOk);
        context.Set("inspection_status", status);
        context.Set(
            "result_json",
            JsonSerializer.Serialize(
                new
                {
                    status,
                    isValid,
                    isOk,
                    expectedShape = _expectedShape,
                    expectedCount = _expectedCount,
                    detectedCount,
                    allowAdditional = _allowAdditional,
                    detection = detail,
                }
            )
        );
    }

    public void Dispose() { }

    private static VisionParameter<int> NumberOutput(string name, string displayName) =>
        new() { ParameterName = name, DisplayName = displayName, ParameterType = typeof(int) };

    private static VisionParameter<bool> BoolOutput(string name, string displayName) =>
        new() { ParameterName = name, DisplayName = displayName, ParameterType = typeof(bool) };

    private static VisionParameter<string> StringOutput(
        string name,
        string displayName,
        PortControlType? controlType = null
    )
    {
        var parameter = new VisionParameter<string>
        {
            ParameterName = name,
            DisplayName = displayName,
            ParameterType = typeof(string),
        };
        if (controlType.HasValue)
            parameter.ControlType = controlType.Value;
        return parameter;
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
