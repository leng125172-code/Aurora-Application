namespace AuroraStruct3D.OpenCV.ThresholdOps;

/// <summary>
/// 工作流算子：Otsu 自动阈值分割。
/// <para>
/// 使用 Otsu 大津算法自动计算最优阈值，将灰度图像二值化。
/// Otsu 算法通过最大化类间方差来选择阈值，无需手动指定阈值，适用于双峰直方图图像。
/// </para>
/// <para>
/// 相比通用 <see cref="threshold"/> 算子，本算子仅专注 Otsu 模式，
/// 无模式选择参数，配置更简洁，适合标准缺陷检测流水线。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 二值化后的图像矩阵（CV_8UC1，0/255）</item>
///   <item>输出 <c>threshold_value</c>（double）— Otsu 自动计算的最优阈值</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000011")]
[Category("2D检测定位")]
[DisplayName("Otsu自动阈值")]
[Description("自动算最佳阈值做二值化，省得一个个手动试，适合明暗两堆的图。")]
public class otsu_threshold : IOperator
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
        };

    private readonly int _maxValue;
    private bool _disposed;

    /// <summary>
    /// 初始化 Otsu 阈值分割算子。
    /// </summary>
    /// <param name="maxValue">最大值，通常为 255。</param>
    public otsu_threshold(int maxValue = 255)
    {
        _maxValue = maxValue;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行 Otsu 阈值分割。");

        // 确保输入为灰度图（Otsu 要求单通道）
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

        try
        {
            // Otsu 自动阈值：threshold 参数传 0，由 Otsu 自动计算
            double otsuValue = Cv2.Threshold(
                grayMat,
                result,
                0,
                _maxValue,
                ThresholdTypes.Binary | ThresholdTypes.Otsu
            );

            context.Set("output_mat", result.Clone());
            context.Set("threshold_value", otsuValue);
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
