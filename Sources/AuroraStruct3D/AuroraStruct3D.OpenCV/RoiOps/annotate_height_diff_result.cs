namespace AuroraStruct3D.OpenCV.RoiOps;

[Guid("1d6498c5-8d95-41ab-8801-c3f8e20d7301")]
[Category("2D预处理")]
[DisplayName("高度差结果标注")]
[Description("在 2D 结果图上绘制多个 ROI 轮廓和 OK/NG 判定。详细测量数据通过接口返回。")]
public class annotate_height_diff_result : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                ParameterType = typeof(bool),
                DisplayName = "判定结果",
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_mat", DisplayName = "输出图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "okText",
                DisplayName = "OK 文本",
                ParameterType = typeof(string),
                DefaultValue = "OK",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "ngText",
                DisplayName = "NG 文本",
                ParameterType = typeof(string),
                DefaultValue = "NG",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _okText;
    private readonly string _ngText;
    private bool _disposed;

    public annotate_height_diff_result(string okText = "OK", string ngText = "NG")
    {
        _okText = string.IsNullOrWhiteSpace(okText) ? "OK" : okText;
        _ngText = string.IsNullOrWhiteSpace(ngText) ? "NG" : ngText;
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
        {
            throw new InvalidOperationException("输入图像为空，无法绘制结果标注。");
        }

        bool isOk = context.Get<bool>("is_ok");

        Mat output = EnsureBgra(inputMat);

        // 仅绘制整体 OK/NG 状态（轮廓和标签由 render_plane_outlines 负责）
        Scalar statusColor = isOk ? new Scalar(80, 200, 120, 255) : new Scalar(60, 60, 255, 255);
        string statusText = isOk ? _okText : _ngText;
        Cv2.PutText(
            output,
            statusText,
            new Point(16, 36),
            HersheyFonts.HersheySimplex,
            0.9,
            statusColor,
            3
        );

        context.Set("output_mat", output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Mat EnsureBgra(Mat inputMat)
    {
        Mat output = new();
        switch (inputMat.Channels())
        {
            case 4:
                return inputMat.Clone();
            case 3:
                Cv2.CvtColor(inputMat, output, ColorConversionCodes.BGR2BGRA);
                return output;
            default:
                Cv2.CvtColor(inputMat, output, ColorConversionCodes.GRAY2BGRA);
                return output;
        }
    }

    /// <summary>
    /// 用于结果标注的区域数据结构，供其他算子反序列化使用。
    /// </summary>
    public class AnnotateRegion
    {
        public string Name { get; set; } = string.Empty;
        public double Height { get; set; }
        public RoiMetadata Roi { get; set; } = new();
        public RoiProjectionMapping? ProjectionMapping { get; set; }
        public double[]? PlaneParams { get; set; }
        public List<RoiPoint>? OutlinePoints { get; set; }
        public bool IsSelected { get; set; }
    }
}
