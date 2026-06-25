namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 双边滤波降噪。
/// <para>
/// 同时考虑空间邻近度和像素相似度的非线性滤波，能在去噪的同时保留边缘。
/// 适用于需要保留清晰边缘的降噪场景。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 滤波后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("4c3d5e6f-7089-0123-cdef-123456789003")]
[Category("2D降噪滤波")]
[DisplayName("2D 双边滤波")]
[Description("保留边缘的降噪滤波，同时考虑空间邻近度和像素相似度。")]
public class bilateral_filter : IOperator
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
                Name = "d",
                DisplayName = "滤波直径",
                ParameterType = typeof(int),
                DefaultValue = "9",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "sigmaColor",
                DisplayName = "颜色标准差",
                ParameterType = typeof(double),
                DefaultValue = "75",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "sigmaSpace",
                DisplayName = "空间标准差",
                ParameterType = typeof(double),
                DefaultValue = "75",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _d;
    private readonly double _sigmaColor;
    private readonly double _sigmaSpace;
    private bool _disposed;

    public bilateral_filter(int d = 9, double sigmaColor = 75, double sigmaSpace = 75)
    {
        _d = d;
        _sigmaColor = sigmaColor;
        _sigmaSpace = sigmaSpace;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行双边滤波。");

        Mat result = new Mat();
        try
        {
            Cv2.BilateralFilter(inputMat, result, _d, _sigmaColor, _sigmaSpace);
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
