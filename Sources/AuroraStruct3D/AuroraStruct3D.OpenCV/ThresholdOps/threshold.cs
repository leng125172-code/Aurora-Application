namespace AuroraStruct3D.OpenCV.ThresholdOps;

/// <summary>
/// 工作流算子：2D 阈值分割。
/// <para>
/// 将灰度图像按阈值转换为二值图像，支持三种阈值模式：
/// <list type="bullet">
///   <item><b>Binary</b>：固定阈值二值化（大于阈值为 255，否则为 0）</item>
///   <item><b>Otsu</b>：Otsu 自动阈值分割，自动计算最优阈值，适用于双峰直方图图像</item>
///   <item><b>Adaptive</b>：自适应阈值分割，根据局部邻域像素值动态计算阈值，适用于光照不均图像</item>
/// </list>
/// </para>
/// <para>
/// 自适应阈值模式下，每个像素的阈值由邻域均值或高斯加权均值决定，能有效处理光照不均匀的场景。
/// Otsu 模式下阈值完全自动计算，无需手动指定。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 二值化后的图像矩阵（CV_8UC1，0/255）</item>
///   <item>输出 <c>threshold_value</c>（double）— 实际使用的阈值（Otsu 模式下为自动计算值）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000010")]
[Category("2D检测定位")]
[DisplayName("阈值分割")]
[Description("按灰度卡个阈值，把目标和背景分开（手动设阈值）。")]
public class threshold : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "二值图像" },
            new VisionParameter<double>
            {
                ParameterName = "threshold_value",
                DisplayName = "阈值",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            ConfigParameter.ForEnum<ThresholdMode>(
                name: "mode",
                displayName: "阈值模式",
                defaultValue: ThresholdMode.Binary,
                required: false
            ),
            new ConfigParameter
            {
                Name = "threshold",
                DisplayName = "阈值",
                ParameterType = typeof(int),
                DefaultValue = "128",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxValue",
                DisplayName = "最大值",
                ParameterType = typeof(int),
                DefaultValue = "255",
                ValueLimit = new[] { "255", "1" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "adaptiveMethod",
                DisplayName = "自适应方法",
                ParameterType = typeof(string),
                DefaultValue = "Mean",
                ValueLimit = new[] { "Mean", "Gaussian" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "blockSize",
                DisplayName = "邻域块大小",
                ParameterType = typeof(int),
                DefaultValue = "11",
                ValueLimit = new[] { "3", "5", "7", "9", "11", "13", "15", "21", "31" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "c",
                DisplayName = "偏移常数 C",
                ParameterType = typeof(double),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    /// <summary>阈值模式枚举。</summary>
    public enum ThresholdMode
    {
        /// <summary>固定阈值二值化。</summary>
        Binary,

        /// <summary>Otsu 自动阈值分割。</summary>
        Otsu,

        /// <summary>自适应阈值分割。</summary>
        Adaptive,
    }

    private readonly ThresholdMode _mode;
    private readonly int _threshold;
    private readonly int _maxValue;
    private readonly string _adaptiveMethod;
    private readonly int _blockSize;
    private readonly double _c;
    private bool _disposed;

    /// <summary>
    /// 初始化阈值分割算子。
    /// </summary>
    /// <param name="mode">阈值模式。</param>
    /// <param name="threshold">固定阈值（仅 Binary 模式有效）。</param>
    /// <param name="maxValue">最大值，通常为 255。</param>
    /// <param name="adaptiveMethod">自适应方法：Mean 或 Gaussian。</param>
    /// <param name="blockSize">邻域块大小，必须为奇数。</param>
    /// <param name="c">偏移常数，从均值中减去。</param>
    public threshold(
        ThresholdMode mode = ThresholdMode.Binary,
        int threshold = 128,
        int maxValue = 255,
        string adaptiveMethod = "Mean",
        int blockSize = 11,
        double c = 2
    )
    {
        _mode = mode;
        _threshold = threshold;
        _maxValue = maxValue;
        _adaptiveMethod = adaptiveMethod;
        _blockSize = blockSize;
        _c = c;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行阈值分割。");

        // 确保输入为灰度图
        Mat grayMat;
        bool needDispose = false;
        if (inputMat.Channels() > 1)
        {
            grayMat = new Mat();
            Cv2.CvtColor(inputMat, grayMat, ColorConversionCodes.BGR2GRAY);
            needDispose = true;
        }
        else
        {
            grayMat = inputMat;
        }

        Mat result = new Mat();
        double actualThreshold = _threshold;

        try
        {
            switch (_mode)
            {
                case ThresholdMode.Binary:
                    // 固定阈值二值化
                    Cv2.Threshold(grayMat, result, _threshold, _maxValue, ThresholdTypes.Binary);
                    actualThreshold = _threshold;
                    break;

                case ThresholdMode.Otsu:
                    // Otsu 自动阈值
                    Cv2.Threshold(
                        grayMat,
                        result,
                        0, // 阈值会被 Otsu 自动计算
                        _maxValue,
                        ThresholdTypes.Binary | ThresholdTypes.Otsu
                    );
                    // 从输出中无法直接获取 Otsu 阈值，需二次计算
                    actualThreshold = Cv2.Threshold(
                        grayMat,
                        new Mat(),
                        0,
                        _maxValue,
                        ThresholdTypes.Binary | ThresholdTypes.Otsu
                    );
                    break;

                case ThresholdMode.Adaptive:
                    // 校验块大小必须为奇数
                    int effectiveBlockSize = _blockSize;
                    if (effectiveBlockSize % 2 == 0)
                        effectiveBlockSize++;

                    AdaptiveThresholdTypes adaptiveType = _adaptiveMethod switch
                    {
                        "Gaussian" => AdaptiveThresholdTypes.GaussianC,
                        _ => AdaptiveThresholdTypes.MeanC,
                    };

                    Cv2.AdaptiveThreshold(
                        grayMat,
                        result,
                        _maxValue,
                        adaptiveType,
                        ThresholdTypes.Binary,
                        effectiveBlockSize,
                        _c
                    );
                    // 自适应阈值无单一阈值，返回 0
                    actualThreshold = 0;
                    break;
            }

            context.Set("output_mat", result.Clone());
            context.Set("threshold_value", actualThreshold);
        }
        finally
        {
            result.Dispose();
            if (needDispose)
                grayMat.Dispose();
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
}
