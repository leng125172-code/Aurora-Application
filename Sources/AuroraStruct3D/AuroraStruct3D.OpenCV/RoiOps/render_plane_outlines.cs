using System.Globalization;
using System.Text.Json;

namespace AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 工作流算子：平面轮廓渲染。
/// <para>
/// 从区域数据 JSON 中读取每个区域的平面轮廓点（OutlinePoints），
/// 在输入图像上绘制轮廓边框线，区分选中/未选中平面。
/// 选中平面使用高亮颜色 + 粗线条，未选中平面使用对应颜色 + 细线条。
/// </para>
/// </summary>
[Guid("e1f2a3b4-c5d6-7890-abcd-ef0123456789")]
[Category("2D预处理")]
[DisplayName("平面轮廓渲染")]
[Description("在图像上绘制拟合平面的外轮廓边框线，选中平面高亮显示。")]
public class render_plane_outlines : IOperator
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
                Name = "outlineThickness",
                DisplayName = "轮廓线宽度",
                ParameterType = typeof(int),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "highlightThickness",
                DisplayName = "高亮线宽度",
                ParameterType = typeof(int),
                DefaultValue = "3",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "highlightColor",
                DisplayName = "高亮颜色",
                ParameterType = typeof(string),
                DefaultValue = "#00FF00",
                Required = false,
                ControlType = PortControlType.Input,
            },
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly int _outlineThickness;
    private readonly int _highlightThickness;
    private readonly Scalar _highlightColor;
    private bool _disposed;

    public render_plane_outlines(
        int outlineThickness = 2,
        int highlightThickness = 3,
        string highlightColor = "#00FF00"
    )
    {
        _outlineThickness = Math.Max(1, outlineThickness);
        _highlightThickness = Math.Max(1, highlightThickness);
        _highlightColor = ParseHexColor(highlightColor);
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
            throw new InvalidOperationException("输入图像为空，无法渲染平面轮廓。");
        }

        string? regionsJson = context.Get<string>("regions_json");
        if (string.IsNullOrWhiteSpace(regionsJson))
        {
            throw new InvalidOperationException(
                "上下文变量 'regions_json' 为空，请确认输入绑定已正确设置。"
            );
        }

        List<annotate_height_diff_result.AnnotateRegion> regions;
        try
        {
            regions =
                JsonSerializer.Deserialize<List<annotate_height_diff_result.AnnotateRegion>>(
                    regionsJson,
                    JsonOptions
                ) ?? throw new InvalidOperationException("区域数据JSON反序列化失败。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"区域数据JSON格式错误: {ex.Message}", ex);
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("区域数据列表为空。");
        }

        Mat output = EnsureBgra(inputMat);

        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];

            if (region.OutlinePoints is not { Count: >= 3 })
                continue;

            Scalar baseColor =
                i < DefaultColors.Length
                    ? DefaultColors[i]
                    : new Scalar(
                        (byte)(255 * Math.Sin(i * 0.7)),
                        (byte)(255 * Math.Cos(i * 0.5)),
                        (byte)(255 * Math.Sin(i * 0.3)),
                        255
                    );

            Scalar drawColor = region.IsSelected ? _highlightColor : baseColor;
            int thickness = region.IsSelected ? _highlightThickness : _outlineThickness;

            Point[] outlinePoints = region
                .OutlinePoints.Select(point => new Point(
                    (int)Math.Round(point.X),
                    (int)Math.Round(point.Y)
                ))
                .ToArray();

            Cv2.Polylines(output, new[] { outlinePoints }, true, drawColor, thickness);

            // 在轮廓上方标注区域名称
            if (outlinePoints.Length > 0)
            {
                Point labelPoint = new(
                    outlinePoints[0].X,
                    Math.Max(20, outlinePoints[0].Y - 8)
                );
                Cv2.PutText(
                    output,
                    region.Name,
                    labelPoint,
                    HersheyFonts.HersheySimplex,
                    0.55,
                    drawColor,
                    2
                );
            }
        }

        context.Set("output_mat", output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 解析十六进制颜色字符串（如 "#00FF00"）为 OpenCV Scalar（BGR）。
    /// </summary>
    private static Scalar ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            return new Scalar(0, 255, 0, 255);

        byte r = byte.Parse(hex[..2], NumberStyles.HexNumber);
        byte g = byte.Parse(hex[2..4], NumberStyles.HexNumber);
        byte b = byte.Parse(hex[4..6], NumberStyles.HexNumber);
        return new Scalar(b, g, r, 255);
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