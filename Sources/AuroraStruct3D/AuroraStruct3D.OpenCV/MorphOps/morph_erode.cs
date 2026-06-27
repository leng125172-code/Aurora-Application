namespace AuroraStruct3D.OpenCV.MorphOps;

/// <summary>
/// 工作流算子：腐蚀运算。
/// <para>
/// 使用结构元素对图像进行腐蚀操作，取局部最小值替换中心像素。
/// 腐蚀可以缩小亮区域、消除小噪点、分离粘连的物体，是形态学处理的基础算子之一。
/// 常与膨胀运算配合使用，构成开运算和闭运算。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 腐蚀后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0008-4000-8000-000000000015")]
[Category("2D预处理")]
[DisplayName("腐蚀运算")]
[Description("让白色区域缩一圈，去毛刺、断开细连接。")]
public class morph_erode : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "输出图像" },
        };

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
    /// 初始化腐蚀运算算子。
    /// </summary>
    /// <param name="kernelSize">结构元素大小，越大腐蚀效果越强。</param>
    /// <param name="kernelShape">结构元素形状：Rect（矩形）、Ellipse（椭圆）、Cross（十字形）。</param>
    /// <param name="iterations">腐蚀迭代次数，多次迭代等效于使用更大的核。</param>
    public morph_erode(int kernelSize = 3, string kernelShape = "Rect", int iterations = 1)
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
            throw new InvalidOperationException("输入矩阵为空，无法执行腐蚀运算。");

        using Mat kernel = Cv2.GetStructuringElement(
            _kernelShape,
            new OpenCvSharp.Size(_kernelSize, _kernelSize)
        );

        Mat result = new Mat();
        try
        {
            Cv2.Erode(inputMat, result, kernel, iterations: _iterations);
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
