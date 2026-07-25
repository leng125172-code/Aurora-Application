using System.Text.Json;

namespace AuroraStruct3D.OpenCV.MeasureOps;

[Guid("a1b2c3d4-0019-4000-8000-000000000032")]
[Category("2D尺寸测量")]
[DisplayName("角度测量")]
[Description("测量图像中两条线段之间的夹角，支持度数和弧度输出。")]
public class angle_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "lines_json",
                DisplayName = "线段参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "测量图像" },
            new VisionParameter<string>
            {
                ParameterName = "measurement_json",
                DisplayName = "测量结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "angle_degrees",
                DisplayName = "角度(度)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "angle_radians",
                DisplayName = "角度(弧度)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "lineWidth",
                DisplayName = "线宽",
                ParameterType = typeof(int),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "displayUnit",
                DisplayName = "显示单位",
                ParameterType = typeof(string),
                DefaultValue = "degrees",
                ValueLimit = new[] { "degrees", "radians" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _lineWidth;
    private readonly string _displayUnit;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public angle_measure(int lineWidth = 2, string displayUnit = "degrees")
    {
        _lineWidth = lineWidth;
        _displayUnit = displayUnit;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        string linesJson =
            context.Get<string>("lines_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'lines_json' 为空，请确认已连接线段参数。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行角度测量。");

        var lineList = JsonSerializer.Deserialize<List<LineData>>(linesJson, JsonOptions)
            ?? throw new InvalidOperationException("线段参数JSON解析失败。");

        if (lineList.Count < 2)
            throw new InvalidOperationException("至少需要两条线段才能计算角度。");

        var line1 = lineList[0];
        var line2 = lineList[1];

        double dx1 = line1.X2 - line1.X1;
        double dy1 = line1.Y2 - line1.Y1;
        double dx2 = line2.X2 - line2.X1;
        double dy2 = line2.Y2 - line2.Y1;

        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 1e-10 || len2 < 1e-10)
            throw new InvalidOperationException("线段长度不能为零。");

        double dotProduct = dx1 * dx2 + dy1 * dy2;
        double cosAngle = dotProduct / (len1 * len2);
        cosAngle = Math.Clamp(cosAngle, -1, 1);

        double angleRadians = Math.Acos(cosAngle);
        double angleDegrees = angleRadians * 180 / Math.PI;

        Mat outputMat;
        if (inputMat.Channels() == 1)
        {
            outputMat = new Mat();
            Cv2.CvtColor(inputMat, outputMat, ColorConversionCodes.GRAY2BGR);
        }
        else
        {
            outputMat = inputMat.Clone();
        }

        Cv2.Line(
            outputMat,
            new Point((int)line1.X1, (int)line1.Y1),
            new Point((int)line1.X2, (int)line1.Y2),
            new Scalar(0, 255, 0),
            _lineWidth
        );
        Cv2.Line(
            outputMat,
            new Point((int)line2.X1, (int)line2.Y1),
            new Point((int)line2.X2, (int)line2.Y2),
            new Scalar(255, 0, 0),
            _lineWidth
        );

        double centerX = (line1.X1 + line1.X2 + line2.X1 + line2.X2) / 4;
        double centerY = (line1.Y1 + line1.Y2 + line2.Y1 + line2.Y2) / 4;

        string label = _displayUnit == "radians"
            ? $"{angleRadians:F3} rad"
            : $"{angleDegrees:F1}°";

        Cv2.PutText(
            outputMat,
            label,
            new Point((int)centerX - 40, (int)centerY - 10),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(0, 0, 255),
            2
        );

        var measurement = new MeasurementInfo
        {
            Line1 = line1,
            Line2 = line2,
            AngleDegrees = angleDegrees,
            AngleRadians = angleRadians,
            DisplayUnit = _displayUnit,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_mat", outputMat);
        context.Set("measurement_json", json);
        context.Set("angle_degrees", angleDegrees);
        context.Set("angle_radians", angleRadians);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class LineData
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public class MeasurementInfo
    {
        public LineData Line1 { get; set; } = new();
        public LineData Line2 { get; set; } = new();
        public double AngleDegrees { get; set; }
        public double AngleRadians { get; set; }
        public string DisplayUnit { get; set; } = string.Empty;
    }
}