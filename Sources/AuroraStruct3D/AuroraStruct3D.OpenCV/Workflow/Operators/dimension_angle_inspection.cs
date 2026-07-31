using System.Text.Json;

namespace AuroraStruct3D.OpenCV.Workflow.Operators;

/// <summary>
/// 对长度和角度测量值执行公差判定。
/// </summary>
[Guid("0d2255bf-e3b7-468d-b34c-d9ee1dae8f59")]
[Category("工作流检测")]
[DisplayName("尺寸角度检测判定")]
[Description("按标称尺寸、标称角度及上下偏差判定测量结果，输出统一的 OK、NG 或 UNKNOWN。")]
public sealed class dimension_angle_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            NumberInput("measured_length", "实测尺寸"),
            NumberInput("measured_angle", "实测角度(度)"),
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            NumberOutput("length_deviation", "尺寸偏差"),
            NumberOutput("angle_deviation", "角度偏差"),
            BoolOutput("length_ok", "尺寸是否合格"),
            BoolOutput("angle_ok", "角度是否合格"),
            BoolOutput("is_valid", "结果是否有效"),
            BoolOutput("is_ok", "是否合格"),
            StringOutput("inspection_status", "检测状态"),
            StringOutput("result_json", "尺寸角度判定结果", PortControlType.Download),
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            BoolConfig("checkLength", "启用尺寸检测", true),
            NumberConfig("nominalLength", "标称尺寸", 0),
            NumberConfig("minLengthDeviation", "尺寸最小偏差", -0.1),
            NumberConfig("maxLengthDeviation", "尺寸最大偏差", 0.1),
            BoolConfig("checkAngle", "启用角度检测", true),
            NumberConfig("nominalAngle", "标称角度(度)", 0),
            NumberConfig("minAngleDeviation", "角度最小偏差(度)", -0.5),
            NumberConfig("maxAngleDeviation", "角度最大偏差(度)", 0.5),
        ];

    private readonly bool _checkLength;
    private readonly double _nominalLength;
    private readonly double _minLengthDeviation;
    private readonly double _maxLengthDeviation;
    private readonly bool _checkAngle;
    private readonly double _nominalAngle;
    private readonly double _minAngleDeviation;
    private readonly double _maxAngleDeviation;

    public dimension_angle_inspection(
        bool checkLength = true,
        double nominalLength = 0,
        double minLengthDeviation = -0.1,
        double maxLengthDeviation = 0.1,
        bool checkAngle = true,
        double nominalAngle = 0,
        double minAngleDeviation = -0.5,
        double maxAngleDeviation = 0.5
    )
    {
        if (!checkLength && !checkAngle)
            throw new ArgumentException("尺寸检测和角度检测至少启用一项。");
        ValidateFinite(nominalLength, nameof(nominalLength));
        ValidateRange(minLengthDeviation, maxLengthDeviation, "尺寸偏差");
        ValidateFinite(nominalAngle, nameof(nominalAngle));
        ValidateRange(minAngleDeviation, maxAngleDeviation, "角度偏差");

        _checkLength = checkLength;
        _nominalLength = nominalLength;
        _minLengthDeviation = minLengthDeviation;
        _maxLengthDeviation = maxLengthDeviation;
        _checkAngle = checkAngle;
        _nominalAngle = nominalAngle;
        _minAngleDeviation = minAngleDeviation;
        _maxAngleDeviation = maxAngleDeviation;
    }

    public void Execute(IWorkflowContext context)
    {
        bool hasLength = !_checkLength || context.Contains("measured_length");
        bool hasAngle = !_checkAngle || context.Contains("measured_angle");
        double measuredLength = context.Get<double>("measured_length");
        double measuredAngle = context.Get<double>("measured_angle");
        bool finiteLength = !_checkLength || double.IsFinite(measuredLength);
        bool finiteAngle = !_checkAngle || double.IsFinite(measuredAngle);
        bool isValid = hasLength && hasAngle && finiteLength && finiteAngle;

        double lengthDeviation = measuredLength - _nominalLength;
        double angleDeviation = NormalizeAngleDeviation(measuredAngle - _nominalAngle);
        bool lengthOk =
            !_checkLength
            || (
                isValid
                && lengthDeviation >= _minLengthDeviation
                && lengthDeviation <= _maxLengthDeviation
            );
        bool angleOk =
            !_checkAngle
            || (
                isValid
                && angleDeviation >= _minAngleDeviation
                && angleDeviation <= _maxAngleDeviation
            );
        bool isOk = isValid && lengthOk && angleOk;
        string status = !isValid ? "UNKNOWN" : isOk ? "OK" : "NG";

        context.Set("length_deviation", lengthDeviation);
        context.Set("angle_deviation", angleDeviation);
        context.Set("length_ok", lengthOk);
        context.Set("angle_ok", angleOk);
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
                    length = new
                    {
                        enabled = _checkLength,
                        measured = measuredLength,
                        nominal = _nominalLength,
                        deviation = lengthDeviation,
                        minDeviation = _minLengthDeviation,
                        maxDeviation = _maxLengthDeviation,
                        isOk = lengthOk,
                    },
                    angle = new
                    {
                        enabled = _checkAngle,
                        measured = measuredAngle,
                        nominal = _nominalAngle,
                        deviation = angleDeviation,
                        minDeviation = _minAngleDeviation,
                        maxDeviation = _maxAngleDeviation,
                        isOk = angleOk,
                    },
                }
            )
        );
    }

    public void Dispose() { }

    private static double NormalizeAngleDeviation(double angle)
    {
        while (angle > 180)
            angle -= 360;
        while (angle <= -180)
            angle += 360;
        return angle;
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateRange(double minimum, double maximum, string name)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentException($"{name}范围无效。");
    }

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

    private static ConfigParameter BoolConfig(string name, string displayName, bool value) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = typeof(bool),
            DefaultValue = value ? "true" : "false",
            Required = false,
            ControlType = PortControlType.Switch,
        };
}
