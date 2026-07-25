using System.Text.Json;

namespace AuroraStruct3D.OpenCV.MeasureOps;

[Guid("a1b2c3d4-0020-4000-8000-000000000033")]
[Category("2D尺寸测量")]
[DisplayName("距离测量")]
[Description("测量图像中两点之间的距离，支持像素和实际单位转换。")]
public class distance_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "points_json",
                DisplayName = "点参数",
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
                ParameterName = "distance_pixels",
                DisplayName = "距离(像素)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "distance_mm",
                DisplayName = "距离(mm)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "pixelToMmRatio",
                DisplayName = "像素/mm比例",
                ParameterType = typeof(double),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "drawLine",
                DisplayName = "绘制连接线",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Switch,
            },
            new ConfigParameter
            {
                Name = "lineWidth",
                DisplayName = "线宽",
                ParameterType = typeof(int),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _pixelToMmRatio;
    private readonly bool _drawLine;
    private readonly int _lineWidth;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public distance_measure(double pixelToMmRatio = 1, bool drawLine = true, int lineWidth = 2)
    {
        _pixelToMmRatio = pixelToMmRatio;
        _drawLine = drawLine;
        _lineWidth = lineWidth;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        string pointsJson =
            context.Get<string>("points_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'points_json' 为空，请确认已连接点参数。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行距离测量。");

        var pointList = JsonSerializer.Deserialize<List<PointData>>(pointsJson, JsonOptions)
            ?? throw new InvalidOperationException("点参数JSON解析失败。");

        if (pointList.Count < 2)
            throw new InvalidOperationException("至少需要两个点才能计算距离。");

        var point1 = pointList[0];
        var point2 = pointList[1];

        double dx = point2.X - point1.X;
        double dy = point2.Y - point1.Y;

        double distancePixels = Math.Sqrt(dx * dx + dy * dy);
        double distanceMm = distancePixels * _pixelToMmRatio;

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

        if (_drawLine)
        {
            Cv2.Line(
                outputMat,
                new Point((int)point1.X, (int)point1.Y),
                new Point((int)point2.X, (int)point2.Y),
                new Scalar(0, 255, 0),
                _lineWidth
            );
        }

        Cv2.Circle(outputMat, new Point((int)point1.X, (int)point1.Y), 4, new Scalar(0, 0, 255), -1);
        Cv2.Circle(outputMat, new Point((int)point2.X, (int)point2.Y), 4, new Scalar(0, 0, 255), -1);

        double labelX = (point1.X + point2.X) / 2;
        double labelY = Math.Min(point1.Y, point2.Y) - 10;

        string label = $"{distanceMm:F2}mm ({distancePixels:F1}px)";
        Cv2.PutText(
            outputMat,
            label,
            new Point((int)labelX - 50, (int)labelY),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(0, 255, 0),
            2
        );

        var measurement = new MeasurementInfo
        {
            Point1 = point1,
            Point2 = point2,
            DistancePixels = distancePixels,
            DistanceMm = distanceMm,
            PixelToMmRatio = _pixelToMmRatio,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_mat", outputMat);
        context.Set("measurement_json", json);
        context.Set("distance_pixels", distancePixels);
        context.Set("distance_mm", distanceMm);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class PointData
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class MeasurementInfo
    {
        public PointData Point1 { get; set; } = new();
        public PointData Point2 { get; set; } = new();
        public double DistancePixels { get; set; }
        public double DistanceMm { get; set; }
        public double PixelToMmRatio { get; set; }
    }
}