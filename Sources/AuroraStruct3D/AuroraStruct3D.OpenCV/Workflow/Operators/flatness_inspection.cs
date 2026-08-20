using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Operators;

/// <summary>
/// 将平面距离算子的统计量转换为统一的平面度检测结果。
/// </summary>
[Guid("3a4a0b3b-3bb1-4379-9746-d1673d63e440")]
[Category("工作流检测")]
[DisplayName("平面度检测判定")]
[Description("按最大平面度和最大绝对偏差判定平面测量结果，输出统一的 OK、NG 或 UNKNOWN。")]
public sealed class flatness_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            NumberInput("measured_flatness", "实测平面度(PV)"),
            NumberInput("max_absolute_distance", "最大绝对偏差"),
            new VisionParameter<string>
            {
                ParameterName = "measurement_json",
                DisplayName = "平面测量详情",
                ParameterType = typeof(string),
            },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [InspectionResults.Output<FlatnessInspectionDetails>("平面度判定结果")];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            NumberConfig("maxFlatness", "最大允许平面度", 0.1),
            NumberConfig("maxAbsoluteDistance", "最大允许绝对偏差（0为不限制）", 0),
        ];

    private readonly double _maxFlatness;
    private readonly double _maxAbsoluteDistance;

    public flatness_inspection(double maxFlatness = 0.1, double maxAbsoluteDistance = 0)
    {
        if (!double.IsFinite(maxFlatness) || maxFlatness < 0)
            throw new ArgumentOutOfRangeException(nameof(maxFlatness));
        if (!double.IsFinite(maxAbsoluteDistance) || maxAbsoluteDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAbsoluteDistance));
        _maxFlatness = maxFlatness;
        _maxAbsoluteDistance = maxAbsoluteDistance;
    }

    public void Execute(IWorkflowContext context)
    {
        bool hasFlatness = context.Contains("measured_flatness");
        bool hasAbsoluteDistance = context.Contains("max_absolute_distance");
        double measuredFlatness = context.Get<double>("measured_flatness");
        double maxAbsoluteDistance = context.Get<double>("max_absolute_distance");
        bool isValid =
            hasFlatness
            && double.IsFinite(measuredFlatness)
            && measuredFlatness >= 0
            && (
                _maxAbsoluteDistance <= 0
                || (
                    hasAbsoluteDistance
                    && double.IsFinite(maxAbsoluteDistance)
                    && maxAbsoluteDistance >= 0
                )
            );
        bool flatnessOk = isValid && measuredFlatness <= _maxFlatness;
        bool absoluteDistanceOk =
            _maxAbsoluteDistance <= 0
            || (isValid && maxAbsoluteDistance <= _maxAbsoluteDistance);
        bool isOk = isValid && flatnessOk && absoluteDistanceOk;
        string status = !isValid ? "UNKNOWN" : isOk ? "OK" : "NG";
        JsonElement? measurement = ParseJson(context.Get<string>("measurement_json"));

        context.Set("result", InspectionResults.CreateTyped(isValid, isOk,
                new FlatnessInspectionDetails(
                    status,
                    isValid,
                    isOk,
                    measuredFlatness,
                    _maxFlatness,
                    measuredFlatness - _maxFlatness,
                    maxAbsoluteDistance,
                    _maxAbsoluteDistance,
                    flatnessOk,
                    absoluteDistanceOk,
                    measurement), status));
    }

    public void Dispose() { }

    private static VisionParameter<double> NumberInput(string name, string displayName) =>
        new() { ParameterName = name, DisplayName = displayName, ParameterType = typeof(double) };

    private static VisionParameter<double> NumberOutput(string name, string displayName) =>
        new() { ParameterName = name, DisplayName = displayName, ParameterType = typeof(double) };

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

    private static ConfigParameter NumberConfig(string name, string displayName, double value) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = typeof(double),
            DefaultValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Required = false,
            ControlType = PortControlType.Input,
        };

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
