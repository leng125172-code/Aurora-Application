namespace AuroraStruct3D.OpenCV.EdgeDetectOps;

/// <summary>
/// 工作流算子：Canny 边缘检测。
/// <para>
/// 使用 Canny 算法对图像进行边缘检测，提取图像中的边缘轮廓。
/// Canny 算法流程：高斯平滑降噪 → 梯度计算（Sobel）→ 非极大值抑制 → 双阈值滞后连接。
/// 双阈值机制使得低于低阈值的像素被丢弃，高于高阈值的像素被保留为强边缘，
/// 介于两者之间的像素仅在与强边缘相连时才被保留。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 边缘检测结果（CV_8UC1 二值图，255=边缘）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000012")]
[Category("2D检测定位")]
[DisplayName("Canny边缘检测")]
[Description("把物体轮廓边缘抠出来，双阈值控制检出多少边。")]
public class canny_edge : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "边缘图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "lowThreshold",
                DisplayName = "低阈值",
                ParameterType = typeof(double),
                DefaultValue = "50",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "highThreshold",
                DisplayName = "高阈值",
                ParameterType = typeof(double),
                DefaultValue = "150",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "apertureSize",
                DisplayName = "Sobel 孔径",
                ParameterType = typeof(int),
                DefaultValue = "3",
                ValueLimit = new[] { "3", "5", "7" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "l2Gradient",
                DisplayName = "L2 梯度",
                ParameterType = typeof(bool),
                DefaultValue = "false",
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _lowThreshold;
    private readonly double _highThreshold;
    private readonly int _apertureSize;
    private readonly bool _l2Gradient;
    private bool _disposed;

    /// <summary>
    /// 初始化 Canny 边缘检测算子。
    /// </summary>
    /// <param name="lowThreshold">低阈值，低于此值的像素被丢弃。</param>
    /// <param name="highThreshold">高阈值，高于此值的像素为强边缘。</param>
    /// <param name="apertureSize">Sobel 算子孔径大小，必须为奇数。</param>
    /// <param name="l2Gradient">是否使用更精确的 L2 范数计算梯度。</param>
    public canny_edge(
        double lowThreshold = 50,
        double highThreshold = 150,
        int apertureSize = 3,
        bool l2Gradient = false
    )
    {
        _lowThreshold = lowThreshold;
        _highThreshold = highThreshold;
        _apertureSize = apertureSize;
        _l2Gradient = l2Gradient;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行 Canny 边缘检测。");

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

        try
        {
            Cv2.Canny(grayMat, result, _lowThreshold, _highThreshold, _apertureSize, _l2Gradient);
            context.Set("output_mat", result.Clone());
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
