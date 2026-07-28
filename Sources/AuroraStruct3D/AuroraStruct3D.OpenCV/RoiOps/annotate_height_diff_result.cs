using System.Globalization;

namespace AuroraStruct3D.OpenCV.RoiOps;

[Guid("1d6498c5-8d95-41ab-8801-c3f8e20d7301")]
[Category("2D预处理")]
[DisplayName("高度差结果标注")]
[Description("在 2D 结果图上绘制 OK/NG 判定、区域高度和有符号高度差。")]
public class annotate_height_diff_result : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata_a",
                ParameterType = typeof(string),
                DisplayName = "区域A元数据",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata_b",
                ParameterType = typeof(string),
                DisplayName = "区域B元数据",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<double>
            {
                ParameterName = "height_a",
                ParameterType = typeof(double),
                DisplayName = "区域A高度",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<double>
            {
                ParameterName = "height_b",
                ParameterType = typeof(double),
                DisplayName = "区域B高度",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<double>
            {
                ParameterName = "signed_diff",
                ParameterType = typeof(double),
                DisplayName = "有符号高度差",
                ControlType = PortControlType.Variable,
            },
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
        double heightA = context.Get<double>("height_a");
        double heightB = context.Get<double>("height_b");
        double signedDiff = context.Get<double>("signed_diff");

        Mat output = EnsureBgra(inputMat);

        // ROI 半透明区域、轮廓和名称由 overlay_roi_markers 负责。
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
        DrawMeasurementText(output, $"A: {heightA.ToString("F3", CultureInfo.InvariantCulture)}", 64);
        DrawMeasurementText(output, $"B: {heightB.ToString("F3", CultureInfo.InvariantCulture)}", 88);
        DrawMeasurementText(
            output,
            $"dH(A-B): {signedDiff.ToString("F3", CultureInfo.InvariantCulture)}",
            112
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

    private static void DrawMeasurementText(Mat output, string text, int y)
    {
        Cv2.PutText(
            output,
            text,
            new Point(16, y),
            HersheyFonts.HersheySimplex,
            0.55,
            new Scalar(255, 255, 255, 255),
            2
        );
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
