namespace AuroraStruct3D.OpenCV.ContrastOps;

/// <summary>
/// 工作流算子：CLAHE 自适应直方图均衡化。
/// <para>
/// CLAHE（Contrast Limited Adaptive Histogram Equalization）将图像分成多个小块，
/// 对每个小块独立进行直方图均衡化，并使用对比度限制防止噪声过度放大。
/// 相比全局直方图均衡化，CLAHE 能更好地增强局部对比度，同时抑制噪声。
/// 适用于光照不均匀的缺陷检测场景，如暗区细节增强。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 均衡化后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000b-4000-8000-000000000018")]
[Category("2D预处理")]
[DisplayName("CLAHE均衡化")]
[Description("分块自适应增强对比度，比普通均衡更耐光照不均，局部细节更清楚。")]
public class clahe : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new MatImg() { ParameterName = "input_mat" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new MatImg() { ParameterName = "output_mat" } };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "clipLimit",
                DisplayName = "对比度限制",
                ParameterType = typeof(double),
                DefaultValue = "2.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "tileGridSize",
                DisplayName = "网格大小",
                ParameterType = typeof(int),
                DefaultValue = "8",
                ValueLimit = new[] { "4", "8", "16" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _clipLimit;
    private readonly int _tileGridSize;
    private bool _disposed;

    /// <summary>
    /// 初始化 CLAHE 算子。
    /// </summary>
    /// <param name="clipLimit">对比度限制阈值，值越大对比度越强，但噪声也可能被放大。</param>
    /// <param name="tileGridSize">网格大小（像素），将图像分为 tileGridSize × tileGridSize 的小块。</param>
    public clahe(double clipLimit = 2.0, int tileGridSize = 8)
    {
        _clipLimit = clipLimit;
        _tileGridSize = tileGridSize;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行 CLAHE 均衡化。");

        // CLAHE 仅支持单通道灰度图
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

        try
        {
            using var clahe = Cv2.CreateCLAHE(
                _clipLimit,
                new OpenCvSharp.Size(_tileGridSize, _tileGridSize)
            );
            Mat result = new Mat();
            clahe.Apply(grayMat, result);
            context.Set("output_mat", result);
        }
        finally
        {
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
