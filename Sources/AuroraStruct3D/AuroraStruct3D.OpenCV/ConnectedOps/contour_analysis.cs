using System.Text.Json;

namespace AuroraStruct3D.OpenCV.ConnectedOps;

/// <summary>
/// 工作流算子：轮廓提取与特征分析。
/// <para>
/// 从二值图像中提取所有轮廓，计算每个轮廓的几何特征，
/// 包括面积、周长、质心、外接矩形、最小外接旋转矩形、凸包、圆形度等。
/// 支持按面积阈值过滤小轮廓噪点，以及轮廓近似精度控制。
/// </para>
/// <para>
/// 轮廓提取使用 <c>FindContours</c>（RETR_EXTERNAL，仅外轮廓），
/// 近似方法使用 <c>CHAIN_APPROX_SIMPLE</c> 压缩水平/垂直/对角线段。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 二值输入图像</item>
///   <item>输出 <c>contours_json</c>（string）— JSON 格式的轮廓特征列表</item>
///   <item>输出 <c>contour_count</c>（int）— 有效轮廓数量</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000014")]
[Category("2D检测定位")]
[DisplayName("轮廓分析")]
[Description("提取轮廓并算出面积、周长等形状参数，做尺寸判断。")]
public class contour_analysis : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<string>
            {
                ParameterName = "contours_json",
                DisplayName = "轮廓特征",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "contour_count",
                DisplayName = "轮廓数",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                DisplayName = "轮廓数量是否合格",
                ParameterType = typeof(bool),
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
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minCount",
                DisplayName = "最少轮廓数",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxCount",
                DisplayName = "最多轮廓数",
                ParameterType = typeof(int),
                DefaultValue = "2147483647",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _minArea;
    private readonly double _maxArea;
    private readonly double _epsilonFactor;
    private readonly int _minCount;
    private readonly int _maxCount;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化轮廓分析算子。
    /// </summary>
    /// <param name="minArea">最小面积阈值，小于此值的轮廓被过滤（0 表示不过滤）。</param>
    /// <param name="maxArea">最大面积阈值，大于此值的轮廓被过滤（0 表示不过滤）。</param>
    /// <param name="epsilonFactor">轮廓近似精度系数，乘以轮廓周长得到近似距离。</param>
    public contour_analysis(
        double minArea = 0,
        double maxArea = 0,
        double epsilonFactor = 0.01,
        int minCount = 0,
        int maxCount = int.MaxValue
    )
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _epsilonFactor = epsilonFactor;
        _minCount = minCount;
        _maxCount = maxCount;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行轮廓分析。");

        // 确保输入为二值图
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

            List<ContourFeature> features = new(contours.Length);

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);

                // 面积过滤
                if (_minArea > 0 && area < _minArea)
                    continue;
                if (_maxArea > 0 && area > _maxArea)
                    continue;

                double perimeter = Cv2.ArcLength(contour, closed: true);

                // 质心（矩计算）
                Moments moments = Cv2.Moments(contour);
                double cx = moments.M10 / (moments.M00 + 1e-10);
                double cy = moments.M01 / (moments.M00 + 1e-10);

                // 外接矩形
                Rect boundingRect = Cv2.BoundingRect(contour);

                // 最小外接旋转矩形
                RotatedRect rotatedRect = Cv2.MinAreaRect(contour);

                // 凸包
                Point[] hull = Cv2.ConvexHull(contour);
                double hullArea = Cv2.ContourArea(hull);

                // 圆形度：4π × 面积 / 周长²，圆 = 1，越不规则越接近 0
                double circularity =
                    perimeter > 0 ? (4 * Math.PI * area) / (perimeter * perimeter) : 0;

                // 凸性：面积 / 凸包面积
                double convexity = hullArea > 0 ? area / hullArea : 0;

                // 近似轮廓
                Point[] approx = Cv2.ApproxPolyDP(
                    contour,
                    _epsilonFactor * perimeter,
                    closed: true
                );

                features.Add(
                    new ContourFeature
                    {
                        Area = area,
                        Perimeter = perimeter,
                        Centroid = new ContourPoint { X = cx, Y = cy },
                        BoundingRect = new ContourRect
                        {
                            X = boundingRect.X,
                            Y = boundingRect.Y,
                            Width = boundingRect.Width,
                            Height = boundingRect.Height,
                        },
                        RotatedRect = new ContourRotatedRect
                        {
                            Cx = rotatedRect.Center.X,
                            Cy = rotatedRect.Center.Y,
                            Width = rotatedRect.Size.Width,
                            Height = rotatedRect.Size.Height,
                            Angle = rotatedRect.Angle,
                        },
                        Circularity = circularity,
                        Convexity = convexity,
                        VertexCount = approx.Length,
                        ApproxVertices = approx
                            .Select(p => new ContourPoint { X = p.X, Y = p.Y })
                            .ToList(),
                    }
                );
            }

            string json = JsonSerializer.Serialize(features, JsonOptions);
            context.Set("contours_json", json);
            context.Set("contour_count", features.Count);
            context.Set(
                "is_ok",
                features.Count >= _minCount && features.Count <= _maxCount
            );
        }
        finally
        {
            if (needDispose)
                binaryMat.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 输出 DTO（内嵌，避免文件膨胀）──────────────────────────────────────

    /// <summary>单个轮廓的几何特征。</summary>
    public class ContourFeature
    {
        public double Area { get; set; }
        public double Perimeter { get; set; }
        public ContourPoint Centroid { get; set; } = new();
        public ContourRect BoundingRect { get; set; } = new();
        public ContourRotatedRect RotatedRect { get; set; } = new();
        public double Circularity { get; set; }
        public double Convexity { get; set; }
        public int VertexCount { get; set; }
        public List<ContourPoint> ApproxVertices { get; set; } = new();
    }

    public class ContourPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class ContourRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class ContourRotatedRect
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Angle { get; set; }
    }
}
