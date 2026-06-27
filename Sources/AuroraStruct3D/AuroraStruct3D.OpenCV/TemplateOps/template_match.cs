using System.Text.Json;

namespace AuroraStruct3D.OpenCV.TemplateOps;

/// <summary>
/// 工作流算子：模板匹配。
/// <para>
/// 在输入图像中滑动模板图像，计算每个位置的匹配度，找到最佳匹配位置。
/// 支持多种匹配方法，适用于定位特定图案、识别 Logo、检测重复缺陷等场景。
/// 输出匹配结果热力图和最佳匹配位置坐标。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 待搜索的输入图像</item>
///   <item>输入 <c>template_mat</c>（Mat）— 模板图像</item>
///   <item>输出 <c>output_mat</c>（Mat）— 绘制了匹配框的图像（CV_8UC3）</item>
///   <item>输出 <c>match_json</c>（string）— 最佳匹配信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0013-4000-8000-000000000026")]
[Category("2D检测定位")]
[DisplayName("模板匹配")]
[Description("拿个模板在图里找一模一样的东西做定位（灰度匹配，不抗旋转缩放）。")]
public class template_match : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new MatImg() { ParameterName = "template_mat", DisplayName = "模板图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "匹配图像" },
            new VisionParameter<string>
            {
                ParameterName = "match_json",
                DisplayName = "匹配结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "method",
                DisplayName = "匹配方法",
                ParameterType = typeof(string),
                DefaultValue = "CcoeffNormed",
                ValueLimit = new[]
                {
                    "CcoeffNormed",
                    "Ccoeff",
                    "CcorrNormed",
                    "Ccorr",
                    "SqDiffNormed",
                    "SqDiff",
                },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly string _method;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化模板匹配算子。
    /// </summary>
    /// <param name="method">匹配方法：
    /// CcoeffNormed（归一化相关系数，推荐）、Ccoeff（相关系数）、
    /// CcorrNormed（归一化互相关）、Ccorr（互相关）、
    /// SqDiffNormed（归一化平方差）、SqDiff（平方差）。</param>
    public template_match(string method = "CcoeffNormed")
    {
        _method = method;
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

        Mat templateMat =
            context.Get<Mat>("template_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'template_mat' 为空，请确认已连接模板图像。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入图像为空，无法执行模板匹配。");

        if (templateMat.Empty())
            throw new InvalidOperationException("模板图像为空，无法执行模板匹配。");

        if (templateMat.Rows > inputMat.Rows || templateMat.Cols > inputMat.Cols)
            throw new InvalidOperationException("模板图像尺寸不能大于输入图像。");

        TemplateMatchModes mode = _method switch
        {
            "Ccoeff" => TemplateMatchModes.CCoeff,
            "CcorrNormed" => TemplateMatchModes.CCorrNormed,
            "Ccorr" => TemplateMatchModes.CCorr,
            "SqDiffNormed" => TemplateMatchModes.SqDiffNormed,
            "SqDiff" => TemplateMatchModes.SqDiff,
            _ => TemplateMatchModes.CCoeffNormed,
        };

        // 执行模板匹配
        using Mat result = new Mat();
        Cv2.MatchTemplate(inputMat, templateMat, result, mode);

        // 找最佳匹配位置
        double minVal,
            maxVal;
        Point minLoc,
            maxLoc;
        Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

        // 对于 SqDiff 方法，最小值是最佳匹配；其他方法最大值是最佳匹配
        bool isDiffMethod = _method.StartsWith("SqDiff");
        Point bestLoc = isDiffMethod ? minLoc : maxLoc;
        double bestScore = isDiffMethod ? minVal : maxVal;

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

        // 绘制匹配框
        int tplW = templateMat.Cols;
        int tplH = templateMat.Rows;
        Cv2.Rectangle(
            outputMat,
            new Rect(bestLoc.X, bestLoc.Y, tplW, tplH),
            new Scalar(0, 255, 0),
            2
        );

        var matchInfo = new MatchInfo
        {
            X = bestLoc.X,
            Y = bestLoc.Y,
            Width = tplW,
            Height = tplH,
            Score = bestScore,
            Method = _method,
        };

        string matchJson = JsonSerializer.Serialize(matchInfo, JsonOptions);

        context.Set("output_mat", outputMat);
        context.Set("match_json", matchJson);
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

    /// <summary>模板匹配结果。</summary>
    public class MatchInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Score { get; set; }
        public string Method { get; set; } = string.Empty;
    }
}
