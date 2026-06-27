namespace AuroraStruct3D.OpenCV.MorphOps;

/// <summary>
/// 工作流算子：膨胀运算。
/// <para>
/// 使用结构元素对图像进行膨胀操作，取局部最大值替换中心像素。
/// 膨胀可以扩大亮区域、填充小孔洞、连接相邻的物体，是形态学处理的基础算子之一。
/// 常与腐蚀运算配合使用，构成开运算和闭运算。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 膨胀后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0007-4000-8000-000000000014")]
[Category("2D预处理")]
[DisplayName("膨胀运算")]
[Description("让白色区域往外胀一圈，补断点、连小缝。")]
public class morph_dilate : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new MatImg() { ParameterName = "output_mat", DisplayName = "输出图像" } };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "kernelSize",
                DisplayName = "结构元素大小",
                ParameterType = typeof(int),
                DefaultValue = "3",
                ValueLimit = new[] { "3", "5", "7", "9", "11" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "kernelShape",
                DisplayName = "结构元素形状",
                ParameterType = typeof(string),
                DefaultValue = "Rect",
                ValueLimit = new[] { "Rect", "Ellipse", "Cross" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "iterations",
                DisplayName = "迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "1",
                ValueLimit = new[] { "1", "2", "3", "5" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _kernelSize;
    private readonly MorphShapes _kernelShape;
    private readonly int _iterations;
    private bool _disposed;

    /// <summary>
    /// 初始化膨胀运算算子。
    /// </summary>
    /// <param name="kernelSize">结构元素大小，越大膨胀效果越强。</param>
    /// <param name="kernelShape">结构元素形状：Rect（矩形）、Ellipse（椭圆）、Cross（十字形）。</param>
    /// <param name="iterations">膨胀迭代次数，多次迭代等效于使用更大的核。</param>
    public morph_dilate(
        int kernelSize = 3,
        string kernelShape = "Rect",
        int iterations = 1
    )
    {
        _kernelSize = kernelSize;
        _kernelShape = kernelShape switch
        {
            "Ellipse" => MorphShapes.Ellipse,
            "Cross" => MorphShapes.Cross,
            _ => MorphShapes.Rect,
        };
        _iterations = iterations;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行膨胀运算。");

        using Mat kernel = Cv2.GetStructuringElement(
            _kernelShape,
            new OpenCvSharp.Size(_kernelSize, _kernelSize)
        );

        Mat result = new Mat();
        try
        {
            Cv2.Dilate(inputMat, result, kernel, iterations: _iterations);
            context.Set("output_mat", result.Clone());
        }
        finally
        {
            result.Dispose();
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