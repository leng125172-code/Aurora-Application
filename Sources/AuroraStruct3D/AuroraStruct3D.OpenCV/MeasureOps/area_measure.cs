using System.Text.Json;

namespace AuroraStruct3D.OpenCV.MeasureOps;

[Guid("a1b2c3d4-0018-4000-8000-000000000031")]
[Category("2D尺寸测量")]
[DisplayName("面积测量")]
[Description("测量图像中轮廓或区域的面积，支持像素和实际单位转换。")]
public class area_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "contours_json",
                DisplayName = "轮廓参数",
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
                ParameterName = "area_pixels",
                DisplayName = "面积(像素)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "area_mm2",
                DisplayName = "面积(mm²)",
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
                Name = "contourIndex",
                DisplayName = "轮廓索引",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _pixelToMmRatio;
    private readonly int _contourIndex;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public area_measure(double pixelToMmRatio = 1, int contourIndex = 0)
    {
        _pixelToMmRatio = pixelToMmRatio;
        _contourIndex = contourIndex;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        string contoursJson =
            context.Get<string>("contours_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'contours_json' 为空，请确认已连接轮廓参数。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行面积测量。");

        var contourList = JsonSerializer.Deserialize<List<ContourData>>(contoursJson, JsonOptions)
            ?? throw new InvalidOperationException("轮廓参数JSON解析失败。");

        if (_contourIndex < 0 || _contourIndex >= contourList.Count)
            throw new InvalidOperationException($"轮廓索引 {_contourIndex} 超出范围（0~{contourList.Count - 1}）。");

        var contourData = contourList[_contourIndex];

        Point[] contour = contourData.ApproxVertices.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();

        double areaPixels = Cv2.ContourArea(contour);
        double areaMm2 = areaPixels * _pixelToMmRatio * _pixelToMmRatio;

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

        Cv2.DrawContours(outputMat, new[] { contour }, 0, new Scalar(0, 255, 0), 2);

        Moments moments = Cv2.Moments(contour);
        double cx = moments.M10 / (moments.M00 + 1e-10);
        double cy = moments.M01 / (moments.M00 + 1e-10);

        string label = $"{areaMm2:F2}mm² ({areaPixels:F1}px)";
        Cv2.PutText(
            outputMat,
            label,
            new Point((int)cx - 50, (int)cy),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(0, 255, 0),
            2
        );

        var measurement = new MeasurementInfo
        {
            ContourIndex = _contourIndex,
            AreaPixels = areaPixels,
            AreaMm2 = areaMm2,
            CentroidX = cx,
            CentroidY = cy,
            PixelToMmRatio = _pixelToMmRatio,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_mat", outputMat);
        context.Set("measurement_json", json);
        context.Set("area_pixels", areaPixels);
        context.Set("area_mm2", areaMm2);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class ContourData
    {
        public List<ContourPoint> ApproxVertices { get; set; } = new();
    }

    public class ContourPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class MeasurementInfo
    {
        public int ContourIndex { get; set; }
        public double AreaPixels { get; set; }
        public double AreaMm2 { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public double PixelToMmRatio { get; set; }
    }
}