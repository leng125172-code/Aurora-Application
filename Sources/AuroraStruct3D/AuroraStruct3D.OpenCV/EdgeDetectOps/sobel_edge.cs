namespace AuroraStruct3D.OpenCV.EdgeDetectOps;

/// <summary>
/// 工作流算子：Sobel 梯度边缘检测。
/// <para>
/// 使用 Sobel 算子计算图像的一阶梯度，提取边缘强度信息。
/// 分别计算 X 方向和 Y 方向的梯度，然后合并为梯度幅值图。
/// 相比 Canny 边缘检测，Sobel 输出的是连续的梯度强度而非二值边缘，
/// 适用于需要梯度方向信息或更精细边缘强度控制的场景。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 透明 BGRA 梯度边缘图；线条颜色跟随当前主题</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000f-4000-8000-000000000022")]
[Category("2D检测定位")]
[DisplayName("Sobel边缘检测")]
[Description("算梯度找边缘，能分横向竖向，看边缘强弱方向。")]
public class sobel_edge : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "梯度图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "kernelSize",
                DisplayName = "卷积核大小",
                ParameterType = typeof(int),
                DefaultValue = "3",
                ValueLimit = new[] { "3", "5", "7" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "scale",
                DisplayName = "缩放系数",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _kernelSize;
    private readonly double _scale;
    private bool _disposed;

    /// <summary>
    /// 初始化 Sobel 边缘检测算子。
    /// </summary>
    /// <param name="kernelSize">Sobel 卷积核大小，越大梯度越平滑。</param>
    /// <param name="scale">梯度缩放系数。</param>
    public sobel_edge(int kernelSize = 3, double scale = 1.0)
    {
        _kernelSize = kernelSize;
        _scale = scale;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行 Sobel 边缘检测。");

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
            using Mat gradX = new Mat();
            using Mat gradY = new Mat();
            Cv2.Sobel(gray, gradX, MatType.CV_64FC1, 1, 0, _kernelSize, _scale);
            Cv2.Sobel(gray, gradY, MatType.CV_64FC1, 0, 1, _kernelSize, _scale);

            // 合并梯度幅值：|G| = sqrt(Gx² + Gy²)
            using Mat magnitude = new Mat();
            Cv2.Magnitude(gradX, gradY, magnitude);

            // 归一化到 0-255
            using Mat output = new Mat();
            Cv2.Normalize(magnitude, output, 0, 255, NormTypes.MinMax);
            output.ConvertTo(output, MatType.CV_8UC1);

            using Mat themedOutput = EdgeThemeRenderer.Render(output);
            context.Set("output_mat", themedOutput.Clone());
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
}
