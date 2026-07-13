using System.Text.Json;

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
            new VisionParameter<string>
            {
                ParameterName = "regions_json",
                ParameterType = typeof(string),
                DisplayName = "区域数据JSON",
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Scalar[] DefaultColors =
    {
        new(255, 180, 0, 255),
        new(0, 220, 255, 255),
        new(180, 255, 0, 255),
        new(255, 80, 200, 255),
        new(0, 255, 180, 255),
        new(255, 255, 0, 255),
        new(200, 80, 255, 255),
        new(80, 200, 255, 255),
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

        string? regionsJson = context.Get<string>("regions_json");
        if (string.IsNullOrWhiteSpace(regionsJson))
        {
            throw new InvalidOperationException(
                "上下文变量 'regions_json' 为空，请确认输入绑定已正确设置。"
            );
        }

        List<AnnotateRegion> regions;
        try
        {
            regions =
                JsonSerializer.Deserialize<List<AnnotateRegion>>(regionsJson, JsonOptions)
                ?? throw new InvalidOperationException("区域数据JSON反序列化失败。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"区域数据JSON格式错误: {ex.Message}", ex);
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("区域数据列表为空。");
        }

        bool isOk = context.Get<bool>("is_ok");

        Mat output = EnsureBgra(inputMat);

        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            Scalar color =
                i < DefaultColors.Length
                    ? DefaultColors[i]
                    : new Scalar(
                        (byte)(255 * Math.Sin(i * 0.7)),
                        (byte)(255 * Math.Cos(i * 0.5)),
                        (byte)(255 * Math.Sin(i * 0.3)),
                        255
                    );

            DrawRoi(output, region.Roi, color, region.Name);
        }

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

    private static void DrawRoi(Mat output, RoiMetadata roi, Scalar color, string label)
    {
        Point[] contour = roi
            .ContourPoints.Select(point => new Point(
                (int)Math.Round(point.X),
                (int)Math.Round(point.Y)
            ))
            .ToArray();

        Scalar fillColor = new(color.Val0, color.Val1, color.Val2, 80);

        if (contour.Length >= 2)
        {
            Cv2.FillPoly(output, new[] { contour }, fillColor);
            Cv2.Polylines(output, new[] { contour }, true, color, 2);
        }
        else
        {
            Rect rect = new(
                (int)Math.Round(roi.BoundingRect.X),
                (int)Math.Round(roi.BoundingRect.Y),
                Math.Max(1, (int)Math.Round(roi.BoundingRect.Width)),
                Math.Max(1, (int)Math.Round(roi.BoundingRect.Height))
            );
            Cv2.Rectangle(output, rect, fillColor, -1);
            Cv2.Rectangle(output, rect, color, 2);
        }

        Point labelPoint = new(
            Math.Max(0, (int)Math.Round(roi.BoundingRect.X)),
            Math.Max(20, (int)Math.Round(roi.BoundingRect.Y) - 8)
        );
        Cv2.PutText(output, label, labelPoint, HersheyFonts.HersheySimplex, 0.55, color, 2);
    }

    public class AnnotateRegion
    {
        public string Name { get; set; } = string.Empty;
        public double Height { get; set; }
        public RoiMetadata Roi { get; set; } = new();
        public RoiProjectionMapping? ProjectionMapping { get; set; }
    }
}
