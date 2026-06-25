namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 中值滤波降噪。
/// <para>
/// 使用中值滤波对图像进行平滑处理，适用于去除椒盐噪声。
/// 相比高斯滤波，中值滤波能更好地保留边缘信息。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 滤波后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("3b2c4d5e-6f70-8901-bcde-f12345678902")]
[Category("2D降噪滤波")]
[DisplayName("2D 中值滤波")]
[Description("使用中值滤波去除椒盐噪声，保留边缘信息。")]
public class median_blur : IOperator
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
                Name = "kernelSize",
                DisplayName = "卷积核大小",
                ParameterType = typeof(int),
                DefaultValue = "5",
                ValueLimit = new[] { "3", "5", "7", "9", "11" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _kernelSize;
    private bool _disposed;

    public median_blur(int kernelSize = 5)
    {
        _kernelSize = kernelSize;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行中值滤波。");

        Mat result = new Mat();
        try
        {
            Cv2.MedianBlur(inputMat, result, _kernelSize);
            context.Set("output_mat", result.Clone());
        }
        finally
        {
            result.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
