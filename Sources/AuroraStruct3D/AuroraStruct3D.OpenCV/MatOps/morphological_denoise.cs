namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 形态学去噪。
/// <para>
/// 使用形态学开运算（先腐蚀后膨胀）或闭运算（先膨胀后腐蚀）去除图像中的小噪点。
/// 开运算适用于去除亮噪点，闭运算适用于去除暗噪点。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 去噪后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("5d4e6f70-8901-2345-def0-123456789004")]
[Category("2D预处理")]
[DisplayName("形态学去噪")]
[Description("用开闭运算清掉零碎噪点和小毛刺，把图弄干净点。")]
public class morphological_denoise : IOperator
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
                ValueLimit = new[] { "3", "5", "7", "9" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "morphShape",
                DisplayName = "结构元素形状",
                ParameterType = typeof(string),
                DefaultValue = "Rect",
                ValueLimit = new[] { "Rect", "Ellipse", "Cross" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "operation",
                DisplayName = "运算类型",
                ParameterType = typeof(string),
                DefaultValue = "Open",
                ValueLimit = new[] { "Open", "Close" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _kernelSize;
    private readonly MorphShapes _morphShape;
    private readonly string _operation;
    private bool _disposed;

    public morphological_denoise(
        int kernelSize = 3,
        string morphShape = "Rect",
        string operation = "Open"
    )
    {
        _kernelSize = kernelSize;
        _morphShape = morphShape switch
        {
            "Ellipse" => MorphShapes.Ellipse,
            "Cross" => MorphShapes.Cross,
            _ => MorphShapes.Rect,
        };
        _operation = operation;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行形态学去噪。");

        using Mat kernel = Cv2.GetStructuringElement(
            _morphShape,
            new OpenCvSharp.Size(_kernelSize, _kernelSize)
        );

        MorphTypes op = _operation == "Close" ? MorphTypes.Close : MorphTypes.Open;
        Mat result = new Mat();
        try
        {
            Cv2.MorphologyEx(inputMat, result, op, kernel);
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
