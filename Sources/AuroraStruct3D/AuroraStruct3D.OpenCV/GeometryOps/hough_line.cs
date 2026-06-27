using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

/// <summary>
/// 工作流算子：霍夫直线检测。
/// <para>
/// 使用概率霍夫变换（HoughLinesP）检测图像中的直线段，返回线段端点坐标。
/// 适用于检测工件边缘、焊缝轨迹、划痕等直线特征，是几何缺陷检测的基础算子。
/// 输入建议为边缘检测后的二值图（如 Canny 或 Sobel 输出）。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为边缘二值图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 绘制了检测直线的图像（CV_8UC3）</item>
///   <item>输出 <c>lines_json</c>（string）— 线段参数 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0011-4000-8000-000000000024")]
[Category("2D检测定位")]
[DisplayName("霍夫直线")]
[Description("从边缘里找直线，量边、定位、测角度用。")]
public class hough_line : IOperator
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
                ParameterName = "lines_json",
                DisplayName = "直线参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "rho",
                DisplayName = "距离分辨率",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "theta",
                DisplayName = "角度分辨率（度）",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "threshold",
                DisplayName = "累积阈值",
                ParameterType = typeof(int),
                DefaultValue = "50",
                ValueLimit = new[] { "20", "50", "100", "150", "200" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "minLineLength",
                DisplayName = "最小线段长度",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxLineGap",
                DisplayName = "最大线段间隙",
                ParameterType = typeof(double),
                DefaultValue = "10",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _rho;
    private readonly double _theta;
    private readonly int _threshold;
    private readonly double _minLineLength;
    private readonly double _maxLineGap;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化霍夫直线检测算子。
    /// </summary>
    /// <param name="rho">累加器距离分辨率（像素）。</param>
    /// <param name="theta">累加器角度分辨率（度）。</param>
    /// <param name="threshold">累加器阈值，只有累积票数超过此值的才被检测为直线。</param>
    /// <param name="minLineLength">最小线段长度（像素），短于此值的线段被丢弃。</param>
    /// <param name="maxLineGap">同一直线上允许的最大间隙（像素），小于此值的间隙被连接。</param>
    public hough_line(
        double rho = 1.0,
        double theta = 1.0,
        int threshold = 50,
        double minLineLength = 30,
        double maxLineGap = 10
    )
    {
        _rho = rho;
        _theta = theta;
        _threshold = threshold;
        _minLineLength = minLineLength;
        _maxLineGap = maxLineGap;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行霍夫直线检测。");

        // 确保是单通道灰度图
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
            // 概率霍夫变换
            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                gray,
                _rho,
                _theta * Math.PI / 180.0,
                _threshold,
                _minLineLength,
                _maxLineGap
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

            // 绘制检测到的直线
            var lineInfos = new List<LineInfo>();
            var rng = new Random(42);
            foreach (var line in lines)
            {
                var color = new Scalar(rng.Next(256), rng.Next(256), rng.Next(256));
                Cv2.Line(outputMat, line.P1, line.P2, color, 2);

                lineInfos.Add(
                    new LineInfo
                    {
                        X1 = line.P1.X,
                        Y1 = line.P1.Y,
                        X2 = line.P2.X,
                        Y2 = line.P2.Y,
                        Length = Math.Sqrt(
                            Math.Pow(line.P2.X - line.P1.X, 2) + Math.Pow(line.P2.Y - line.P1.Y, 2)
                        ),
                    }
                );
            }

            string linesJson = JsonSerializer.Serialize(
                new { LineCount = lines.Length, Lines = lineInfos },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("lines_json", linesJson);
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

    /// <summary>线段信息。</summary>
    public class LineInfo
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Length { get; set; }
    }
}
