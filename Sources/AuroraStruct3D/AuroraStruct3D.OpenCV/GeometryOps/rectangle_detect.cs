using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

[Guid("24b85761-1dc7-49d4-a6d9-2bf5ea871301")]
[Category("2D检测定位")]
[DisplayName("矩形检测")]
[Description("从轮廓中找矩形，输出四角点坐标和尺寸。")]
public class rectangle_detect : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "检测图像" },
            new VisionParameter<string>
            {
                ParameterName = "rectangles_json",
                DisplayName = "矩形参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "rectangle_count",
                DisplayName = "矩形数量",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "minArea",
                DisplayName = "最小面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxArea",
                DisplayName = "最大面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "epsilonFactor",
                DisplayName = "近似精度系数",
                ParameterType = typeof(double),
                DefaultValue = "0.02",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minAspectRatio",
                DisplayName = "最小宽高比",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxAspectRatio",
                DisplayName = "最大宽高比",
                ParameterType = typeof(double),
                DefaultValue = "10",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _minArea;
    private readonly double _maxArea;
    private readonly double _epsilonFactor;
    private readonly double _minAspectRatio;
    private readonly double _maxAspectRatio;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public rectangle_detect(
        double minArea = 0,
        double maxArea = 0,
        double epsilonFactor = 0.02,
        double minAspectRatio = 0.1,
        double maxAspectRatio = 10
    )
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _epsilonFactor = epsilonFactor;
        _minAspectRatio = minAspectRatio;
        _maxAspectRatio = maxAspectRatio;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行矩形检测。");

        Mat binaryMat;
        bool needDispose = false;
        if (inputMat.Channels() > 1)
        {
            binaryMat = new Mat();
            Cv2.CvtColor(inputMat, binaryMat, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(binaryMat, binaryMat, 127, 255, ThresholdTypes.Binary);
            needDispose = true;
        }
        else if (inputMat.Type() != MatType.CV_8UC1)
        {
            binaryMat = new Mat();
            inputMat.ConvertTo(binaryMat, MatType.CV_8UC1);
            needDispose = true;
        }
        else
        {
            binaryMat = inputMat;
        }

        try
        {
            Cv2.FindContours(
                binaryMat,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

            List<RectangleInfo> rectangles = new();
            var rng = new Random(42);

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (_minArea > 0 && area < _minArea)
                    continue;
                if (_maxArea > 0 && area > _maxArea)
                    continue;

                double perimeter = Cv2.ArcLength(contour, closed: true);
                if (perimeter < 10)
                    continue;

                Point[] approx = Cv2.ApproxPolyDP(
                    contour,
                    _epsilonFactor * perimeter,
                    closed: true
                );

                if (approx.Length != 4)
                    continue;

                if (!Cv2.IsContourConvex(approx))
                    continue;

                Rect boundingRect = Cv2.BoundingRect(approx);
                double aspectRatio = boundingRect.Width > 0
                    ? (double)boundingRect.Height / boundingRect.Width
                    : double.MaxValue;
                aspectRatio = Math.Max(aspectRatio, 1.0 / aspectRatio);

                if (aspectRatio < _minAspectRatio || aspectRatio > _maxAspectRatio)
                    continue;

                Moments moments = Cv2.Moments(approx);
                double cx = moments.M10 / (moments.M00 + 1e-10);
                double cy = moments.M01 / (moments.M00 + 1e-10);

                var color = new Scalar(rng.Next(256), rng.Next(256), rng.Next(256));

                rectangles.Add(
                    new RectangleInfo
                    {
                        Centroid = new RectPoint { X = cx, Y = cy },
                        Width = boundingRect.Width,
                        Height = boundingRect.Height,
                        Area = area,
                        Perimeter = perimeter,
                        AspectRatio = aspectRatio,
                        Corners = approx
                            .Select(p => new RectPoint { X = p.X, Y = p.Y })
                            .ToList(),
                    }
                );
            }

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

            var rng2 = new Random(42);
            foreach (var rect in rectangles)
            {
                var color = new Scalar(rng2.Next(256), rng2.Next(256), rng2.Next(256));
                Point[] cornerPoints = rect.Corners.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    Cv2.Line(outputMat, cornerPoints[i], cornerPoints[next], color, 2);
                }
                foreach (var corner in cornerPoints)
                {
                    Cv2.Circle(outputMat, corner, 3, color, -1);
                }
            }

            string json = JsonSerializer.Serialize(
                new { RectangleCount = rectangles.Count, Rectangles = rectangles },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("rectangles_json", json);
            context.Set("rectangle_count", rectangles.Count);
        }
        finally
        {
            if (needDispose)
                binaryMat.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class RectangleInfo
    {
        public RectPoint Centroid { get; set; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
        public double Area { get; set; }
        public double Perimeter { get; set; }
        public double AspectRatio { get; set; }
        public List<RectPoint> Corners { get; set; } = new();
    }

    public class RectPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
