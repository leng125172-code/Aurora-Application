using System.Text.Json;

namespace AuroraStruct3D.OpenCV.MeasureOps;

[Guid("a1b2c3d4-0017-4000-8000-000000000030")]
[Category("2D尺寸测量")]
[DisplayName("长度测量")]
[Description("测量图像中点到点的距离或线段长度，支持像素和实际单位转换。")]
public class length_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "line_json",
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
                ParameterName = "length_pixels",
                DisplayName = "长度(像素)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "length_mm",
                DisplayName = "长度(mm)",
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
                Name = "lineWidth",
                DisplayName = "线宽",
                ParameterType = typeof(int),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _pixelToMmRatio;
    private readonly int _lineWidth;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public length_measure(double pixelToMmRatio = 1, int lineWidth = 2)
    {
        _pixelToMmRatio = pixelToMmRatio;
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

        string lineJson =
            context.Get<string>("line_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'line_json' 为空，请确认已连接线段参数。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行长度测量。");

        var lineData = JsonSerializer.Deserialize<LineData>(lineJson, JsonOptions)
            ?? throw new InvalidOperationException("线段参数JSON解析失败。");

        double x1 = lineData.X1;
        double y1 = lineData.Y1;
        double x2 = lineData.X2;
        double y2 = lineData.Y2;

        double lengthPixels = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        double lengthMm = lengthPixels * _pixelToMmRatio;

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
            new Point((int)x1, (int)y1),
            new Point((int)x2, (int)y2),
            new Scalar(0, 255, 0),
            _lineWidth
        );

        Cv2.Circle(outputMat, new Point((int)x1, (int)y1), 3, new Scalar(0, 0, 255), -1);
        Cv2.Circle(outputMat, new Point((int)x2, (int)y2), 3, new Scalar(0, 0, 255), -1);

        string label = $"{lengthMm:F2}mm ({lengthPixels:F1}px)";
        Cv2.PutText(
            outputMat,
            label,
            new Point((int)Math.Min(x1, x2) + 10, (int)Math.Min(y1, y2) - 10),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(0, 255, 0),
            2
        );

        var measurement = new MeasurementInfo
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            LengthPixels = lengthPixels,
            LengthMm = lengthMm,
            PixelToMmRatio = _pixelToMmRatio,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_mat", outputMat);
        context.Set("measurement_json", json);
        context.Set("length_pixels", lengthPixels);
        context.Set("length_mm", lengthMm);
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
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double LengthPixels { get; set; }
        public double LengthMm { get; set; }
        public double PixelToMmRatio { get; set; }
    }
}