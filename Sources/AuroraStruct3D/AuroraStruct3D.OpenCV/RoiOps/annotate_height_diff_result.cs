using System.Text.Json;

namespace AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 在结果图上绘制 ROI、测量值和 OK/NG 判定。
/// </summary>
[Guid("1d6498c5-8d95-41ab-8801-c3f8e20d7301")]
[Category("2D预处理")]
[DisplayName("高度差结果标注")]
[Description("在 2D 结果图上绘制两个 ROI 轮廓，并标注高度值、差值和 OK/NG。")]
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
                DisplayName = "带符号差值",
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

        Mat output = EnsureBgra(inputMat);
        RoiMetadata roiA = ResolveSingleRoi(
            context.Get<string>("roi_metadata_a"),
            "roi_metadata_a"
        );
        RoiMetadata roiB = ResolveSingleRoi(
            context.Get<string>("roi_metadata_b"),
            "roi_metadata_b"
        );
        double heightA = context.Get<double>("height_a");
        double heightB = context.Get<double>("height_b");
        double signedDiff = context.Get<double>("signed_diff");
        bool isOk = context.Get<bool>("is_ok");

        DrawRoi(output, roiA, new Scalar(255, 180, 0, 255), $"A {heightA:F4}");
        DrawRoi(output, roiB, new Scalar(0, 220, 255, 255), $"B {heightB:F4}");

        Scalar statusColor = isOk ? new Scalar(80, 200, 120, 255) : new Scalar(60, 60, 255, 255);
        string statusText = isOk ? _okText : _ngText;
        Cv2.PutText(
            output,
            $"Diff={signedDiff:F4}",
            new Point(16, 28),
            HersheyFonts.HersheySimplex,
            0.75,
            statusColor,
            2
        );
        Cv2.PutText(
            output,
            statusText,
            new Point(16, 58),
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
        {
            return;
        }

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

    private static RoiMetadata ResolveSingleRoi(string? metadataJson, string inputName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            throw new InvalidOperationException(
                $"上下文变量 '{inputName}' 为空，无法绘制 ROI 标注。"
            );
        }

        RoiPartitionMetadata? metadata = JsonSerializer.Deserialize<RoiPartitionMetadata>(
            metadataJson,
            JsonOptions
        );
        RoiMetadata? roi = metadata?.Rois.FirstOrDefault();
        if (roi is null)
        {
            throw new InvalidOperationException(
                $"上下文变量 '{inputName}' 中未找到可用 ROI 元数据。"
            );
        }

        return roi;
    }

    private static void DrawRoi(Mat output, RoiMetadata roi, Scalar color, string label)
    {
        Point[] contour = roi
            .ContourPoints.Select(point => new Point(
                (int)Math.Round(point.X),
                (int)Math.Round(point.Y)
            ))
            .ToArray();

        if (contour.Length >= 2)
        {
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
            Cv2.Rectangle(output, rect, color, 2);
        }

        Point labelPoint = new(
            Math.Max(0, (int)Math.Round(roi.BoundingRect.X)),
            Math.Max(20, (int)Math.Round(roi.BoundingRect.Y) - 8)
        );
        Cv2.PutText(output, label, labelPoint, HersheyFonts.HersheySimplex, 0.55, color, 2);
    }
}
