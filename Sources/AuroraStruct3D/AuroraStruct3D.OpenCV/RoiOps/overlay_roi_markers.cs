using System.Text.Json;

namespace AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 工作流算子：将 ROI 区域叠加标记到图像。
/// </summary>
[Guid("6bac219e-87f4-4e77-a455-a1c5ecf0c301")]
[Category("2D预处理")]
[DisplayName("ROI叠加标记")]
[Description("把 ROI 区域半透明叠到图上，并画轮廓和名称，方便核对分区位置。")]
public class overlay_roi_markers : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<List<Mat>>
            {
                ParameterName = "roi_masks",
                ParameterType = typeof(List<Mat>),
                DisplayName = "ROI掩膜列表",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata",
                ParameterType = typeof(string),
                DisplayName = "ROI元数据",
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_mat", DisplayName = "标记图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "overlayAlpha",
                DisplayName = "叠加透明度",
                ParameterType = typeof(double),
                DefaultValue = "0.35",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "borderThickness",
                DisplayName = "边框粗细",
                ParameterType = typeof(int),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "showLabel",
                DisplayName = "显示名称",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly double _overlayAlpha;
    private readonly int _borderThickness;
    private readonly bool _showLabel;
    private bool _disposed;

    /// <summary>
    /// 初始化 ROI 叠加标记算子。
    /// </summary>
    public overlay_roi_markers(
        double overlayAlpha = 0.35,
        int borderThickness = 1,
        bool showLabel = true
    )
    {
        _overlayAlpha = Math.Clamp(overlayAlpha, 0d, 1d);
        _borderThickness = Math.Max(1, borderThickness);
        _showLabel = showLabel;
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
        {
            throw new InvalidOperationException("输入图像为空，无法叠加 ROI 标记。") ;
        }

        List<Mat>? roiMasks = context.Get<List<Mat>>("roi_masks");
        RoiPartitionMetadata? metadata = ParseMetadata(context.Get<string>("roi_metadata"));

        int maskCount = roiMasks?.Count ?? 0;
        int metadataCount = metadata?.Rois.Count ?? 0;
        int roiCount = Math.Max(maskCount, metadataCount);
        if (roiCount == 0)
        {
            throw new InvalidOperationException("未提供 ROI 掩膜或 ROI 元数据，无法叠加标记。");
        }

        Mat output = EnsureBgr(inputMat);

        for (int i = 0; i < roiCount; i++)
        {
            Scalar fillColor = ResolveColor(i);
            // ROI 选区边框和名称保持黑色，不随结果展示主题变化。
            Scalar annotationColor = new(0, 0, 0, 255);
            Mat? mask = roiMasks is not null && i < roiMasks.Count ? roiMasks[i] : null;
            RoiMetadata? roi = metadata is not null && i < metadata.Rois.Count ? metadata.Rois[i] : null;

            if (mask is not null)
            {
                ValidateMaskSize(mask, output.Size(), i);
                BlendMask(output, mask, fillColor);
                DrawMaskOutline(output, mask, annotationColor);
            }

            if (roi is not null)
            {
                DrawMetadata(output, roi, annotationColor);
            }
            else if (_showLabel && mask is not null)
            {
                DrawFallbackLabel(output, mask, $"ROI-{i + 1}", annotationColor);
            }
        }

        context.Set("output_mat", output);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static RoiPartitionMetadata? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RoiPartitionMetadata>(metadataJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"ROI 元数据 JSON 解析失败：{ex.Message}");
        }
    }

    private static Mat EnsureBgr(Mat inputMat)
    {
        if (inputMat.Channels() >= 3)
        {
            return inputMat.Clone();
        }

        Mat output = new();
        Cv2.CvtColor(inputMat, output, ColorConversionCodes.GRAY2BGR);
        return output;
    }

    private void BlendMask(Mat output, Mat mask, Scalar color)
    {
        using Mat overlay = output.Clone();
        overlay.SetTo(color, mask);
        Cv2.AddWeighted(overlay, _overlayAlpha, output, 1d - _overlayAlpha, 0d, output);
    }

    private void DrawMaskOutline(Mat output, Mat mask, Scalar color)
    {
        using Mat contourMask = mask.Clone();
        Cv2.FindContours(
            contourMask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        if (contours.Length > 0)
        {
            Cv2.DrawContours(output, contours, -1, color, _borderThickness);
        }
    }

    private void DrawMetadata(Mat output, RoiMetadata roi, Scalar color)
    {
        Point[] contour = roi
            .ContourPoints
            .Select(point => new Point((int)Math.Round(point.X), (int)Math.Round(point.Y)))
            .ToArray();
        if (contour.Length >= 2)
        {
            Cv2.Polylines(output, new[] { contour }, true, color, _borderThickness);
        }
        else
        {
            Rect rect = new(
                (int)Math.Round(roi.BoundingRect.X),
                (int)Math.Round(roi.BoundingRect.Y),
                Math.Max(1, (int)Math.Round(roi.BoundingRect.Width)),
                Math.Max(1, (int)Math.Round(roi.BoundingRect.Height))
            );
            Cv2.Rectangle(output, rect, color, _borderThickness);
        }

        if (!_showLabel)
        {
            return;
        }

        Point labelPoint = new(
            Math.Max(0, (int)Math.Round(roi.BoundingRect.X)),
            Math.Max(18, (int)Math.Round(roi.BoundingRect.Y) - 6)
        );
        Cv2.PutText(
            output,
            string.IsNullOrWhiteSpace(roi.Name) ? $"ROI-{roi.Index + 1}" : roi.Name,
            labelPoint,
            HersheyFonts.HersheySimplex,
            0.55,
            color,
            1
        );
    }

    private void DrawFallbackLabel(Mat output, Mat mask, string label, Scalar color)
    {
        using Mat contourMask = mask.Clone();
        Cv2.FindContours(
            contourMask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );
        if (contours.Length == 0)
        {
            return;
        }

        Point[] contour = contours
            .OrderByDescending(points => points.Length)
            .First();
        Rect rect = Cv2.BoundingRect(contour);
        Point labelPoint = new(Math.Max(0, rect.X), Math.Max(18, rect.Y - 6));
        Cv2.PutText(
            output,
            label,
            labelPoint,
            HersheyFonts.HersheySimplex,
            0.55,
            color,
            1
        );
    }

    private static void ValidateMaskSize(Mat mask, Size imageSize, int index)
    {
        if (mask.Empty())
        {
            throw new InvalidOperationException($"第 {index + 1} 个 ROI 掩膜为空，无法叠加。");
        }

        if (mask.Size() != imageSize)
        {
            throw new InvalidOperationException(
                $"第 {index + 1} 个 ROI 掩膜尺寸与输入图像不一致，无法叠加标记。"
            );
        }
    }

    private static Scalar ResolveColor(int index)
    {
        Scalar[] palette =
        [
            new Scalar(0, 255, 255),
            new Scalar(0, 200, 0),
            new Scalar(255, 180, 0),
            new Scalar(255, 0, 255),
            new Scalar(0, 128, 255),
            new Scalar(255, 255, 0),
        ];

        return palette[index % palette.Length];
    }

}
