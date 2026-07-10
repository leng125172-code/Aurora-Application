using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

/// <summary>
/// 工作流算子：棋盘格质量评分。
/// <para>
/// 检测棋盘格并评估照片是否适合进入标定流程，综合考虑棋盘格是否完整、
/// 图像清晰度和棋盘格在画面中的覆盖度，输出有效性布尔值和质量评分。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（灰度图或彩图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 绘制评分结果的标注图像（CV_8UC3）</item>
///   <item>输出 <c>quality_json</c>（string）— 质量评估 JSON</item>
///   <item>输出 <c>quality_score</c>（double）— 0 到 1 的综合质量分数</item>
///   <item>输出 <c>corner_count</c>（int）— 检测到的角点数量</item>
///   <item>输出 <c>pattern_found</c>（bool）— 是否找到完整棋盘格</item>
///   <item>输出 <c>is_valid</c>（bool）— 是否满足最小清晰度和覆盖度要求</item>
/// </list>
/// </para>
/// </summary>
[Guid("9fd6f6e2-8473-4b14-a2fb-3a203f3a5501")]
[Category("2D检测定位")]
[DisplayName("棋盘格质量评分")]
[Description("评估棋盘格照片是否适合标定，输出有效性、清晰度和覆盖度。")]
public class chessboard_quality_assessment : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "评分图像" },
            new VisionParameter<string>
            {
                ParameterName = "quality_json",
                DisplayName = "质量JSON",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "quality_score",
                DisplayName = "质量分数",
                ParameterType = typeof(double),
            },
            new VisionParameter<int>
            {
                ParameterName = "corner_count",
                DisplayName = "角点数",
                ParameterType = typeof(int),
            },
            new VisionParameter<bool>
            {
                ParameterName = "pattern_found",
                DisplayName = "找到棋盘格",
                ParameterType = typeof(bool),
            },
            new VisionParameter<bool>
            {
                ParameterName = "is_valid",
                DisplayName = "有效照片",
                ParameterType = typeof(bool),
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "patternRows",
                DisplayName = "内角点行数",
                ParameterType = typeof(int),
                DefaultValue = "6",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "patternCols",
                DisplayName = "内角点列数",
                ParameterType = typeof(int),
                DefaultValue = "9",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minSharpness",
                DisplayName = "最小清晰度",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minCoverageRatio",
                DisplayName = "最小覆盖率",
                ParameterType = typeof(double),
                DefaultValue = "0.15",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "drawOverlay",
                DisplayName = "绘制评分",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _patternRows;
    private readonly int _patternCols;
    private readonly double _minSharpness;
    private readonly double _minCoverageRatio;
    private readonly bool _drawOverlay;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public chessboard_quality_assessment(
        int patternRows = 6,
        int patternCols = 9,
        double minSharpness = 30,
        double minCoverageRatio = 0.15,
        bool drawOverlay = true
    )
    {
        _patternRows =
            patternRows > 1
                ? patternRows
                : throw new ArgumentOutOfRangeException(
                    nameof(patternRows),
                    "棋盘格内角点行数必须大于 1。"
                );
        _patternCols =
            patternCols > 1
                ? patternCols
                : throw new ArgumentOutOfRangeException(
                    nameof(patternCols),
                    "棋盘格内角点列数必须大于 1。"
                );
        _minSharpness = Math.Max(0d, minSharpness);
        _minCoverageRatio = Math.Clamp(minCoverageRatio, 0d, 1d);
        _drawOverlay = drawOverlay;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行棋盘格质量评分。");

        Mat gray = ChessboardDetectionHelper.GetGrayMat(inputMat, out bool needDisposeGray);
        try
        {
            Size patternSize = new(_patternCols, _patternRows);
            Point2f[] detectedCorners =
                ChessboardDetectionHelper.FindCornersGray(gray, patternSize, refineCorners: true)
                ?? Array.Empty<Point2f>();

            bool patternFound = detectedCorners.Length == _patternRows * _patternCols;
            double sharpness = ChessboardDetectionHelper.ComputeLaplacianVariance(
                gray,
                detectedCorners
            );
            double coverageRatio = patternFound
                ? ChessboardDetectionHelper.ComputeCoverageRatio(detectedCorners, gray.Size())
                : 0d;

            double sharpnessScore =
                _minSharpness > 0 ? Math.Clamp(sharpness / _minSharpness, 0d, 1d) : 1d;
            double coverageScore =
                _minCoverageRatio > 0 ? Math.Clamp(coverageRatio / _minCoverageRatio, 0d, 1d) : 1d;
            double qualityScore = patternFound
                ? Math.Clamp((sharpnessScore * 0.65d) + (coverageScore * 0.35d), 0d, 1d)
                : 0d;

            bool isValid =
                patternFound && sharpness >= _minSharpness && coverageRatio >= _minCoverageRatio;

            Mat outputMat = ChessboardDetectionHelper.CreateOutputMat(inputMat);
            if (_drawOverlay)
            {
                DrawOverlay(
                    outputMat,
                    patternSize,
                    detectedCorners,
                    patternFound,
                    qualityScore,
                    isValid
                );
            }

            string qualityJson = JsonSerializer.Serialize(
                new ChessboardQualityResult
                {
                    PatternFound = patternFound,
                    PatternRows = _patternRows,
                    PatternCols = _patternCols,
                    CornerCount = detectedCorners.Length,
                    Sharpness = sharpness,
                    CoverageRatio = coverageRatio,
                    QualityScore = qualityScore,
                    IsValid = isValid,
                    MinSharpness = _minSharpness,
                    MinCoverageRatio = _minCoverageRatio,
                },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("quality_json", qualityJson);
            context.Set("quality_score", qualityScore);
            context.Set("corner_count", detectedCorners.Length);
            context.Set("pattern_found", patternFound);
            context.Set("is_valid", isValid);
        }
        finally
        {
            if (needDisposeGray)
                gray.Dispose();
        }
    }

    private static void DrawOverlay(
        Mat outputMat,
        Size patternSize,
        Point2f[] corners,
        bool patternFound,
        double qualityScore,
        bool isValid
    )
    {
        Cv2.DrawChessboardCorners(outputMat, patternSize, corners, patternFound);

        Scalar color = isValid ? new Scalar(40, 190, 40) : new Scalar(40, 40, 220);
        string label = isValid ? "VALID" : "INVALID";
        Cv2.PutText(
            outputMat,
            $"{label} score={qualityScore:F2}",
            new Point(16, 32),
            HersheyFonts.HersheySimplex,
            0.85,
            color,
            2,
            LineTypes.AntiAlias
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class ChessboardQualityResult
    {
        public bool PatternFound { get; set; }

        public int PatternRows { get; set; }

        public int PatternCols { get; set; }

        public int CornerCount { get; set; }

        public double Sharpness { get; set; }

        public double CoverageRatio { get; set; }

        public double QualityScore { get; set; }

        public bool IsValid { get; set; }

        public double MinSharpness { get; set; }

        public double MinCoverageRatio { get; set; }
    }
}
