namespace AuroraStruct3D.OpenCV.MorphOps;

/// <summary>
/// 工作流算子：开运算。
/// <para>
/// 开运算 = 先腐蚀后膨胀。使用同一结构元素先对图像进行腐蚀，再对结果进行膨胀。
/// 可以去除小的亮噪点、断开物体之间的狭窄连接、平滑物体轮廓，而不显著改变物体面积。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 开运算后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0009-4000-8000-000000000016")]
[Category("2D预处理")]
[DisplayName("开运算")]
[Description("先腐蚀再膨胀，去掉小白点和细小连接，断开粘连。")]
public class morph_open : IOperator
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
        };

    private readonly int _kernelSize;
    private readonly MorphShapes _kernelShape;
    private bool _disposed;

    /// <summary>
    /// 初始化开运算算子。
    /// </summary>
    /// <param name="kernelSize">结构元素大小，越大去噪效果越强。</param>
    /// <param name="kernelShape">结构元素形状：Rect（矩形）、Ellipse（椭圆）、Cross（十字形）。</param>
    public morph_open(int kernelSize = 3, string kernelShape = "Rect")
    {
        _kernelSize = kernelSize;
        _kernelShape = kernelShape switch
        {
            "Ellipse" => MorphShapes.Ellipse,
            "Cross" => MorphShapes.Cross,
            _ => MorphShapes.Rect,
        };
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
            throw new InvalidOperationException("输入矩阵为空，无法执行开运算。");

        using Mat kernel = Cv2.GetStructuringElement(
            _kernelShape,
            new OpenCvSharp.Size(_kernelSize, _kernelSize)
        );

        Mat result = new Mat();
        try
        {
            Cv2.MorphologyEx(inputMat, result, MorphTypes.Open, kernel);
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
