using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

/// <summary>
/// 工作流算子：棋盘格角点检测。
/// <para>
/// 检测标定图像中的棋盘格内角点，输出角点坐标、角点数量和标注图像，
/// 适用于相机/投影仪标定流程中的有效照片判定与角点提取。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（灰度图或彩图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 绘制角点后的标注图像（CV_8UC3）</item>
///   <item>输出 <c>corners_json</c>（string）— 棋盘格检测结果 JSON</item>
///   <item>输出 <c>corner_count</c>（int）— 检测到的角点数量</item>
///   <item>输出 <c>pattern_found</c>（bool）— 是否成功找到完整棋盘格</item>
/// </list>
/// </para>
/// </summary>
[Guid("8c91e4a7-2f43-4ce9-b651-6b4d9a200401")]
[Category("2D检测定位")]
[DisplayName("棋盘格角点检测")]
[Description("检测棋盘格内角点，给标定拍照做有效性校验和角点输出。")]
public class chessboard_corner_detection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "标注图像" },
            new VisionParameter<string>
            {
                ParameterName = "corners_json",
                DisplayName = "角点JSON",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
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
                Name = "refineCorners",
                DisplayName = "亚像素优化",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "drawCorners",
                DisplayName = "绘制角点",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _patternRows;
    private readonly int _patternCols;
    private readonly bool _refineCorners;
    private readonly bool _drawCorners;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化棋盘格角点检测算子。
    /// </summary>
    /// <param name="patternRows">棋盘格内角点行数。</param>
    /// <param name="patternCols">棋盘格内角点列数。</param>
    /// <param name="refineCorners">是否执行亚像素角点优化。</param>
    /// <param name="drawCorners">是否在输出图像上绘制角点。</param>
    public chessboard_corner_detection(
        int patternRows = 6,
        int patternCols = 9,
        bool refineCorners = true,
        bool drawCorners = true
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
        _refineCorners = refineCorners;
        _drawCorners = drawCorners;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行棋盘格角点检测。");

        Mat gray = ChessboardDetectionHelper.GetGrayMat(inputMat, out bool needDisposeGray);

        try
        {
            Size patternSize = new(_patternCols, _patternRows);
            Point2f[] detectedCorners =
                ChessboardDetectionHelper.FindCornersGray(gray, patternSize, _refineCorners)
                ?? Array.Empty<Point2f>();
            bool patternFound = detectedCorners.Length == _patternRows * _patternCols;

            Mat outputMat = ChessboardDetectionHelper.CreateOutputMat(inputMat);
            if (_drawCorners)
            {
                Cv2.DrawChessboardCorners(outputMat, patternSize, detectedCorners, patternFound);
            }

            string cornersJson = JsonSerializer.Serialize(
                new ChessboardCornerResult
                {
                    PatternFound = patternFound,
                    PatternRows = _patternRows,
                    PatternCols = _patternCols,
                    CornerCount = detectedCorners.Length,
                    Corners = detectedCorners
                        .Select(point => new ChessboardCornerInfo { X = point.X, Y = point.Y })
                        .ToList(),
                },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("corners_json", cornersJson);
            context.Set("corner_count", detectedCorners.Length);
            context.Set("pattern_found", patternFound);
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

    private sealed class ChessboardCornerResult
    {
        public bool PatternFound { get; set; }

        public int PatternRows { get; set; }

        public int PatternCols { get; set; }

        public int CornerCount { get; set; }

        public List<ChessboardCornerInfo> Corners { get; set; } = [];
    }

    private sealed class ChessboardCornerInfo
    {
        public float X { get; set; }

        public float Y { get; set; }
    }
}
