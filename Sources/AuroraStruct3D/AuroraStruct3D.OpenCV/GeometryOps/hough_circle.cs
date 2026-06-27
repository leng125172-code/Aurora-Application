using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

/// <summary>
/// 工作流算子：霍夫圆检测。
/// <para>
/// 使用霍夫梯度法（HoughCircles）检测图像中的圆形轮廓，返回圆心坐标和半径。
/// 适用于检测圆孔、焊点、螺栓孔、圆形缺陷等圆形特征。
/// 输入建议为灰度图或经过高斯模糊预处理后的图像。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 绘制了检测圆的图像（CV_8UC3）</item>
///   <item>输出 <c>circles_json</c>（string）— 圆形参数 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0012-4000-8000-000000000025")]
[Category("2D检测定位")]
[DisplayName("霍夫圆检测")]
[Description("找圆和圆孔，定出圆心和半径。")]
public class hough_circle : IOperator
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
                ParameterName = "circles_json",
                DisplayName = "圆参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "dp",
                DisplayName = "累加器分辨率反比",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                ValueLimit = new[] { "1.0", "1.5", "2.0" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "minDist",
                DisplayName = "圆心最小间距",
                ParameterType = typeof(double),
                DefaultValue = "20",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "param1",
                DisplayName = "Canny 高阈值",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "param2",
                DisplayName = "累加器阈值",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minRadius",
                DisplayName = "最小半径",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxRadius",
                DisplayName = "最大半径",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _dp;
    private readonly double _minDist;
    private readonly double _param1;
    private readonly double _param2;
    private readonly int _minRadius;
    private readonly int _maxRadius;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化霍夫圆检测算子。
    /// </summary>
    /// <param name="dp">累加器分辨率与输入图像分辨率的反比，1.0 表示相同分辨率。</param>
    /// <param name="minDist">检测到的圆心之间的最小距离，防止重复检测。</param>
    /// <param name="param1">Canny 边缘检测的高阈值，低阈值自动设为高阈值的一半。</param>
    /// <param name="param2">累加器阈值，越小检测到的圆越多（可能包含假圆）。</param>
    /// <param name="minRadius">最小圆半径，0 表示不限制。</param>
    /// <param name="maxRadius">最大圆半径，0 表示不限制。</param>
    public hough_circle(
        double dp = 1.0,
        double minDist = 20,
        double param1 = 100,
        double param2 = 30,
        int minRadius = 0,
        int maxRadius = 0
    )
    {
        _dp = dp;
        _minDist = minDist;
        _param1 = param1;
        _param2 = param2;
        _minRadius = minRadius;
        _maxRadius = maxRadius;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行霍夫圆检测。");

        // 转为灰度图
        Mat gray;
        bool needDisposeGray = false;
        if (inputMat.Channels() > 1)
        {
            gray = new Mat();
            Cv2.CvtColor(inputMat, gray, ColorConversionCodes.BGR2GRAY);
            needDisposeGray = true;
        }
        else
        {
            gray = inputMat;
        }

        try
        {
            CircleSegment[] circles = Cv2.HoughCircles(
                gray,
                HoughModes.Gradient,
                _dp,
                _minDist,
                _param1,
                _param2,
                _minRadius,
                _maxRadius
            );

            // 构建输出图像
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

            // 绘制检测到的圆
            var circleInfos = new List<CircleInfo>();
            var rng = new Random(42);
            foreach (var circle in circles)
            {
                var color = new Scalar(rng.Next(256), rng.Next(256), rng.Next(256));
                Cv2.Circle(outputMat, (Point)circle.Center, (int)circle.Radius, color, 2);
                Cv2.Circle(outputMat, (Point)circle.Center, 2, color, -1);

                circleInfos.Add(
                    new CircleInfo
                    {
                        CenterX = circle.Center.X,
                        CenterY = circle.Center.Y,
                        Radius = circle.Radius,
                        Area = Math.PI * circle.Radius * circle.Radius,
                    }
                );
            }

            string circlesJson = JsonSerializer.Serialize(
                new { CircleCount = circles.Length, Circles = circleInfos },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("circles_json", circlesJson);
        }
        finally
        {
            if (needDisposeGray)
                gray.Dispose();
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

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>圆形信息。</summary>
    public class CircleInfo
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public double Area { get; set; }
    }
}
