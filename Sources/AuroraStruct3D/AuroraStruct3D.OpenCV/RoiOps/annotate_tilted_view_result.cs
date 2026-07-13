namespace AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 工作流算子：在倾斜视图上绘制 OK/NG 状态标注。
/// <para>
/// 与 <see cref="annotate_height_diff_result"/> 不同，本算子不在图像上绘制 ROI 轮廓
/// （因为 ROI 像素坐标基于俯视图，在倾斜视图上无法直接映射），
/// 仅绘制 OK/NG 状态文本，用于倾斜视图的结果展示。
/// </para>
/// </summary>
[Guid("9f4b2c3d-8e5a-4b6c-9d1e-2f3a4b5c6d7e")]
[Category("2D预处理")]
[DisplayName("倾斜视图结果标注")]
[Description("在倾斜视图上绘制 OK/NG 判定结果。")]
public class annotate_tilted_view_result : IOperator
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

    public annotate_tilted_view_result(string okText = "OK", string ngText = "NG")
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
}
