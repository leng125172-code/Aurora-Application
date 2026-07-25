using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

[Guid("a1b2c3d4-0015-4000-8000-000000000028")]
[Category("2D检测定位")]
[DisplayName("椭圆检测")]
[Description("从轮廓中拟合椭圆，输出中心、长短轴和角度。")]
public class ellipse_detect : IOperator
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
                ParameterName = "ellipses_json",
                DisplayName = "椭圆参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "ellipse_count",
                DisplayName = "椭圆数量",
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
                Name = "minAxisLength",
                DisplayName = "最小轴长",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minEccentricity",
                DisplayName = "最小离心率",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxEccentricity",
                DisplayName = "最大离心率",
                ParameterType = typeof(double),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _minArea;
    private readonly double _maxArea;
    private readonly double _minAxisLength;
    private readonly double _minEccentricity;
    private readonly double _maxEccentricity;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ellipse_detect(
        double minArea = 0,
        double maxArea = 0,
        double minAxisLength = 0,
        double minEccentricity = 0,
        double maxEccentricity = 1
    )
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _minAxisLength = minAxisLength;
        _minEccentricity = minEccentricity;
        _maxEccentricity = maxEccentricity;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行椭圆检测。");

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

            List<EllipseInfo> ellipses = new();

            foreach (Point[] contour in contours)
            {
                if (contour.Length < 5)
                    continue;

                double area = Cv2.ContourArea(contour);
                if (_minArea > 0 && area < _minArea)
                    continue;
                if (_maxArea > 0 && area > _maxArea)
                    continue;

                try
                {
                    RotatedRect ellipseRect = Cv2.FitEllipse(contour);

                    double majorAxis = Math.Max(ellipseRect.Size.Width, ellipseRect.Size.Height);
                    double minorAxis = Math.Min(ellipseRect.Size.Width, ellipseRect.Size.Height);

                    if (minorAxis < _minAxisLength)
                        continue;

                    double eccentricity = majorAxis > 0
                        ? Math.Sqrt(1 - Math.Pow(minorAxis / majorAxis, 2))
                        : 0;

                    if (eccentricity < _minEccentricity || eccentricity > _maxEccentricity)
                        continue;

                    double ellipseArea = Math.PI * (majorAxis / 2) * (minorAxis / 2);

                    ellipses.Add(
                        new EllipseInfo
                        {
                            CenterX = ellipseRect.Center.X,
                            CenterY = ellipseRect.Center.Y,
                            MajorAxis = majorAxis,
                            MinorAxis = minorAxis,
                            Angle = ellipseRect.Angle,
                            Area = ellipseArea,
                            Eccentricity = eccentricity,
                        }
                    );
                }
                catch
                {
                }
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

            var rng = new Random(42);
            foreach (var ellipse in ellipses)
            {
                var color = new Scalar(rng.Next(256), rng.Next(256), rng.Next(256));
                Cv2.Ellipse(
                    outputMat,
                    new RotatedRect(
                        new Point2f((float)ellipse.CenterX, (float)ellipse.CenterY),
                        new Size2f((float)ellipse.MajorAxis, (float)ellipse.MinorAxis),
                        (float)ellipse.Angle
                    ),
                    color,
                    2
                );
                Cv2.Circle(
                    outputMat,
                    new Point((int)ellipse.CenterX, (int)ellipse.CenterY),
                    3,
                    color,
                    -1
                );
            }

            string json = JsonSerializer.Serialize(
                new { EllipseCount = ellipses.Count, Ellipses = ellipses },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("ellipses_json", json);
            context.Set("ellipse_count", ellipses.Count);
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

    public class EllipseInfo
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double MajorAxis { get; set; }
        public double MinorAxis { get; set; }
        public double Angle { get; set; }
        public double Area { get; set; }
        public double Eccentricity { get; set; }
    }
}