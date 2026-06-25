namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 高斯滤波降噪。
/// <para>
/// 使用二维高斯核对图像进行平滑处理，适用于去除高斯噪声。
/// 卷积核大小越大，平滑效果越强，但细节损失也越多。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 滤波后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("2a1b3c4d-5e6f-7890-abcd-ef1234567801")]
[Category("2D降噪滤波")]
[DisplayName("2D 高斯滤波")]
[Description("使用高斯核对图像进行平滑降噪，适用于去除高斯噪声。")]
public class gaussian_blur : IOperator
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
            new ConfigParameter
            {
                Name = "sigmaX",
                DisplayName = "X 方向标准差",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _kernelSize;
    private readonly double _sigmaX;
    private bool _disposed;

    public gaussian_blur(int kernelSize = 5, double sigmaX = 0)
    {
        _kernelSize = kernelSize;
        _sigmaX = sigmaX;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行高斯滤波。");

        Mat result = new Mat();
        try
        {
            Cv2.GaussianBlur(
                inputMat,
                result,
                new OpenCvSharp.Size(_kernelSize, _kernelSize),
                _sigmaX
            );
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
