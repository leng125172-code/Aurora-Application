namespace AuroraStruct3D.OpenCV.ContrastOps;

/// <summary>
/// 工作流算子：直方图均衡化。
/// <para>
/// 对灰度图像进行全局直方图均衡化，通过拉伸像素强度分布来增强整体对比度。
/// 适用于图像背景和前景都较亮或较暗时的对比度提升。
/// 相比 CLAHE，全局均衡化计算更快，但可能在局部区域产生过度增强或噪声放大。
/// 对于光照不均匀的场景，建议使用 CLAHE 算子。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 均衡化后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0010-4000-8000-000000000023")]
[Category("2D预处理")]
[DisplayName("直方图均衡化")]
[Description("图太暗或灰蒙蒙就拉一下对比度，明暗更分明。")]
public class histogram_equalization : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "增强图像" },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

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
            throw new InvalidOperationException("输入矩阵为空，无法执行直方图均衡化。");

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
            Mat result = new Mat();
            Cv2.EqualizeHist(gray, result);
            context.Set("output_mat", result);
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
