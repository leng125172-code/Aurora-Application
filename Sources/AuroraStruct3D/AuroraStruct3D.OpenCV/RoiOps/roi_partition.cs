using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraStruct3D.OpenCV.RoiOps;

// ═══════════════════════════════════════════════════════════════════════════════
// ROI 分区输入 DTO 模型
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>ROI 类型枚举。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoiType
{
    Rect,
    Polygon,
    Path,
    Circle,
    Sector,
}

/// <summary>二维点坐标。</summary>
public class RoiPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// 单个 ROI 定义。
/// <para>
/// <b>rect 旋转约定</b>：<c>x, y</c> 为旋转<b>前</b>的矩形左上角坐标，
/// <c>rotation</c> 为绕矩形自身中心点 <c>(x + width/2, y + height/2)</c> 顺时针旋转的角度（度）。
/// 即先以左上角定位矩形，再以其几何中心为轴旋转。
/// </para>
/// </summary>
public class RoiDefinition
{
    /// <summary>ROI 名称，用于前端标识与回显。</summary>
    public string Name { get; set; } = string.Empty;

    public RoiType Type { get; set; }

    // ── rect 专属字段 ──
    /// <summary>旋转前矩形左上角 X 坐标。</summary>
    public double X { get; set; }

    /// <summary>旋转前矩形左上角 Y 坐标。</summary>
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>绕矩形中心顺时针旋转角度（度）。</summary>
    public double Rotation { get; set; }

    // ── polygon 专属字段 ──
    public List<RoiPoint>? Points { get; set; }

    // ── path 专属字段 ──
    public string? D { get; set; }

    // ── circle 专属字段 ──
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double R { get; set; }

    // ── sector 专属字段 ──
    public RoiPoint? StartPoint { get; set; }
    public RoiPoint? EndPoint { get; set; }
}

/// <summary>ROI 分区完整输入配置（仅含 rois 数组，不含合并模式，合并由前端处理）。</summary>
public class RoiPartitionConfig
{
    public List<RoiDefinition> Rois { get; set; } = new();

    /// <summary>ROI 编辑底图信息（XY/XZ/YZ 三视图候选及当前选中项）。</summary>
    public RoiBaseImageInfo? BaseImage { get; set; }
}

/// <summary>ROI 编辑底图候选图信息。</summary>
public class RoiBaseImagePreview
{
    /// <summary>视图标签，例如 XY、XZ、YZ。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>对应 blob 名称。</summary>
    public string BlobName { get; set; } = string.Empty;
}

/// <summary>ROI 编辑底图信息。</summary>
public class RoiBaseImageInfo
{
    /// <summary>当前选中的 blob 名称。</summary>
    public string? SelectedBlobName { get; set; }

    /// <summary>当前选中的视图标签。</summary>
    public string? SelectedLabel { get; set; }

    /// <summary>三视图候选列表。</summary>
    public List<RoiBaseImagePreview> PreviewImages { get; set; } = new();

    /// <summary>
    /// 底图到模型坐标的映射参数。
    /// 用于将 ROI 像素坐标稳定映射回模型世界坐标。
    /// </summary>
    public RoiProjectionMapping? ProjectionMapping { get; set; }
}

/// <summary>ROI 底图像素坐标到模型坐标的投影映射信息。</summary>
public class RoiProjectionMapping
{
    /// <summary>映射对应的视图标签，如 XY、XZ、YZ。</summary>
    public string ViewLabel { get; set; } = "XY";

    /// <summary>模型坐标 X 轴最小值。</summary>
    public double WorldMinX { get; set; }

    /// <summary>模型坐标 X 轴最大值。</summary>
    public double WorldMaxX { get; set; }

    /// <summary>模型坐标 Y 轴最小值。</summary>
    public double WorldMinY { get; set; }

    /// <summary>模型坐标 Y 轴最大值。</summary>
    public double WorldMaxY { get; set; }

    /// <summary>映射对应的底图像素宽度。</summary>
    public int ImageWidth { get; set; }

    /// <summary>映射对应的底图像素高度。</summary>
    public int ImageHeight { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ROI 输出元数据 DTO
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>单个 ROI 的几何元数据。</summary>
public class RoiMetadata
{
    /// <summary>ROI 名称（来自输入配置）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ROI 索引（与输入顺序一致）。</summary>
    public int Index { get; set; }

    /// <summary>ROI 类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>最小外接矩形（矩形 ROI 旋转后外接矩形可能扩大）。</summary>
    public RoiRect BoundingRect { get; set; } = new();

    /// <summary>ROI 区域面积（像素数）。</summary>
    public double Area { get; set; }

    /// <summary>轮廓顶点列表（近似多边形）。</summary>
    public List<RoiPoint> ContourPoints { get; set; } = new();
}

/// <summary>矩形几何数据。</summary>
public class RoiRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>ROI 分区运算结果元数据。</summary>
public class RoiPartitionMetadata
{
    public List<RoiMetadata> Rois { get; set; } = new();
    public double TotalArea { get; set; }

    /// <summary>ROI 编辑底图信息（与输入配置保持一致，便于前端回显）。</summary>
    public RoiBaseImageInfo? BaseImage { get; set; }

    /// <summary>ROI 底图到模型坐标的映射信息。</summary>
    public RoiProjectionMapping? ProjectionMapping { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ROI 分区算子实现
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 工作流算子：ROI 分区掩膜生成。
/// <para>
/// 根据结构化 ROI 分区参数，为每个 ROI 独立生成 OpenCV 图像掩膜（8UC1），
/// 支持矩形、多边形、SVG 路径、圆形、扇形五种类型。
/// 输出每个 ROI 的独立掩膜列表及几何元数据（外接矩形、面积、轮廓点集）。
/// 多 ROI 合并由前端负责，后端仅返回各 ROI 实际的选取区域。
/// </para>
/// <para>端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像，用于确定掩膜尺寸</item>
///   <item>输出 <c>output_masks</c>（List&lt;Mat&gt;）— 每个 ROI 对应的 8UC1 掩膜列表</item>
///   <item>输出 <c>roi_metadata</c>（string）— JSON 格式的几何元数据</item>
/// </list>
/// </para>
/// <para>
/// <b>rect 旋转约定</b>：<c>x, y</c> 为旋转前矩形左上角，
/// <c>rotation</c> 绕矩形中心 <c>(x + width/2, y + height/2)</c> 顺时针旋转。
/// </para>
/// </summary>
[Guid("a1b2c3d4-0001-4000-8000-000000000001")]
[Category("2D预处理")]
[DisplayName("ROI 分区掩膜")]
[Description("圈出感兴趣区域做掩膜，只处理框里那块，支持矩形/多边形/路径/圆形/扇形。")]
public class roi_partition : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new MatImg() { ParameterName = "input_mat" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "primary_mask", DisplayName = "主掩膜" },
            new VisionParameter<List<Mat>>
            {
                ParameterName = "output_masks",
                ParameterType = typeof(List<Mat>),
                DisplayName = "输出掩膜",
            },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
                DisplayName = "ROI元数据",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "roiJson",
                DisplayName = "ROI 配置 JSON",
                ParameterType = typeof(string),
                DefaultValue = null,
                Required = true,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _roiJson;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化 ROI 分区算子。
    /// </summary>
    /// <param name="roiJson">ROI 配置 JSON 字符串，包含 rois 数组。</param>
    public roi_partition(string roiJson)
    {
        _roiJson = roiJson;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ① 读取输入图像
        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入图像为空，无法生成 ROI 掩膜。");

        int imageWidth = inputMat.Width;
        int imageHeight = inputMat.Height;

        // ② 解析 ROI 配置 JSON
        RoiPartitionConfig config;
        try
        {
            config =
                JsonSerializer.Deserialize<RoiPartitionConfig>(_roiJson, JsonOptions)
                ?? throw new InvalidOperationException("ROI 配置 JSON 解析结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"ROI 配置 JSON 解析失败：{ex.Message}。请检查 JSON 格式是否正确。"
            );
        }

        if (config.Rois is null || config.Rois.Count == 0)
            throw new InvalidOperationException("ROI 配置中 rois 数组为空，至少需要一个 ROI。");

        // ③ 为每个 ROI 生成独立掩膜
        List<Mat> roiMasks = new(config.Rois.Count);
        List<RoiMetadata> metadataList = new(config.Rois.Count);

        for (int i = 0; i < config.Rois.Count; i++)
        {
            RoiDefinition roi = config.Rois[i];
            Mat mask = GenerateRoiMask(roi, imageWidth, imageHeight, i);
            roiMasks.Add(mask);

            RoiMetadata meta = ExtractRoiMetadata(mask, roi.Name, roi.Type.ToString(), i);
            metadataList.Add(meta);
        }

        // ④ 计算总面积并组装元数据
        double totalArea = roiMasks.Sum(m => Cv2.CountNonZero(m));

        ValidateBaseImageConsistency(config.BaseImage, imageWidth, imageHeight);

        RoiProjectionMapping? projectionMapping = ResolveProjectionMapping(
            config.BaseImage,
            imageWidth,
            imageHeight
        );

        var metadata = new RoiPartitionMetadata
        {
            Rois = metadataList,
            TotalArea = totalArea,
            BaseImage = config.BaseImage,
            ProjectionMapping = projectionMapping,
        };

        string metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        // ⑤ 输出结果：每个 ROI 的独立掩膜列表 + 元数据 JSON
        if (roiMasks.Count == 1)
        {
            context.Set("primary_mask", roiMasks[0].Clone());
        }

        context.Set("output_masks", roiMasks);
        context.Set("roi_metadata", metadataJson);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 私有方法 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 根据 ROI 类型分发生成对应掩膜。
    /// </summary>
    private static Mat GenerateRoiMask(
        RoiDefinition roi,
        int imageWidth,
        int imageHeight,
        int index
    )
    {
        return roi.Type switch
        {
            RoiType.Rect => GenerateRectMask(roi, imageWidth, imageHeight),
            RoiType.Polygon => GeneratePolygonMask(roi, imageWidth, imageHeight, index),
            RoiType.Path => GeneratePathMask(roi, imageWidth, imageHeight, index),
            RoiType.Circle => GenerateCircleMask(roi, imageWidth, imageHeight),
            RoiType.Sector => GenerateSectorMask(roi, imageWidth, imageHeight, index),
            _ => throw new InvalidOperationException(
                $"不支持的 ROI 类型：{roi.Type}。支持的类型：rect、polygon、path、circle、sector。"
            ),
        };
    }

    /// <summary>
    /// 生成旋转矩形 ROI 掩膜。
    /// <para>
    /// x, y 为旋转前矩形左上角坐标，旋转圆心为矩形中心 (x + width/2, y + height/2)，
    /// rotation 为顺时针旋转角度。
    /// </para>
    /// </summary>
    private static Mat GenerateRectMask(RoiDefinition roi, int imageWidth, int imageHeight)
    {
        ValidateRect(roi);

        Mat mask = Mat.Zeros(imageHeight, imageWidth, MatType.CV_8UC1);

        if (Math.Abs(roi.Rotation) < 1e-6)
        {
            // 无旋转：直接使用轴对齐矩形
            Rect r = ClampRect(
                (int)Math.Round(roi.X),
                (int)Math.Round(roi.Y),
                (int)Math.Round(roi.Width),
                (int)Math.Round(roi.Height),
                imageWidth,
                imageHeight
            );
            if (r.Width > 0 && r.Height > 0)
                mask[r].SetTo(Scalar.White);
        }
        else
        {
            // 有旋转：以矩形中心为轴构建 RotatedRect → FillPoly
            var rotatedRect = new RotatedRect(
                new Point2f((float)(roi.X + roi.Width / 2), (float)(roi.Y + roi.Height / 2)),
                new Size2f((float)roi.Width, (float)roi.Height),
                (float)roi.Rotation
            );
            Point2f[] vertices = rotatedRect.Points();
            List<List<Point>> pts = new()
            {
                vertices
                    .Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
                    .ToList(),
            };
            Cv2.FillPoly(mask, pts, Scalar.White);
        }

        return mask;
    }

    /// <summary>生成多边形 ROI 掩膜。</summary>
    private static Mat GeneratePolygonMask(
        RoiDefinition roi,
        int imageWidth,
        int imageHeight,
        int index
    )
    {
        if (roi.Points is null || roi.Points.Count < 3)
            throw new InvalidOperationException(
                $"第 {index} 个 ROI（polygon）顶点不足 3 个（当前 {roi.Points?.Count ?? 0} 个），"
                    + "多边形至少需要 3 个顶点。"
            );

        List<Point> points = roi
            .Points.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
            .ToList();

        Mat mask = Mat.Zeros(imageHeight, imageWidth, MatType.CV_8UC1);
        Cv2.FillPoly(mask, new List<List<Point>> { points }, Scalar.White);
        return mask;
    }

    /// <summary>生成 SVG Path ROI 掩膜。</summary>
    private static Mat GeneratePathMask(
        RoiDefinition roi,
        int imageWidth,
        int imageHeight,
        int index
    )
    {
        if (string.IsNullOrWhiteSpace(roi.D))
            throw new InvalidOperationException(
                $"第 {index} 个 ROI（path）的 d 属性为空，必须提供有效的 SVG 路径字符串。"
            );

        List<Point> points = ParseSvgPath(roi.D);

        if (points.Count < 3)
            throw new InvalidOperationException(
                $"第 {index} 个 ROI（path）解析后顶点不足 3 个（当前 {points.Count} 个），"
                    + "请确认路径 d 字符串包含足够的几何数据。"
            );

        Mat mask = Mat.Zeros(imageHeight, imageWidth, MatType.CV_8UC1);
        Cv2.FillPoly(mask, new List<List<Point>> { points }, Scalar.White);
        return mask;
    }

    /// <summary>生成圆形 ROI 掩膜。</summary>
    private static Mat GenerateCircleMask(RoiDefinition roi, int imageWidth, int imageHeight)
    {
        if (roi.R <= 0)
            throw new InvalidOperationException($"圆形 ROI 半径必须大于 0，当前值：{roi.R}。");

        Mat mask = Mat.Zeros(imageHeight, imageWidth, MatType.CV_8UC1);
        Cv2.Circle(
            mask,
            (int)Math.Round(roi.Cx),
            (int)Math.Round(roi.Cy),
            (int)Math.Round(roi.R),
            Scalar.White,
            -1
        );
        return mask;
    }

    /// <summary>生成扇形 ROI 掩膜。</summary>
    private static Mat GenerateSectorMask(
        RoiDefinition roi,
        int imageWidth,
        int imageHeight,
        int index
    )
    {
        if (roi.StartPoint is null || roi.EndPoint is null)
            throw new InvalidOperationException(
                $"第 {index} 个 ROI（sector）缺少 startPoint 或 endPoint 字段。"
            );

        double cx = roi.Cx;
        double cy = roi.Cy;
        double sx = roi.StartPoint.X;
        double sy = roi.StartPoint.Y;
        double ex = roi.EndPoint.X;
        double ey = roi.EndPoint.Y;

        // 计算半径：取圆心到两个边界点的距离最大值
        double r1 = Math.Sqrt((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy));
        double r2 = Math.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
        double radius = Math.Max(r1, r2);

        if (radius <= 0)
            throw new InvalidOperationException(
                $"第 {index} 个 ROI（sector）圆心与边界点重合，无法构成扇形。"
            );

        // 计算起始角度和结束角度：0° 沿 x 轴正方向，顺时针为正
        double startAngle = Math.Atan2(sy - cy, sx - cx) * 180.0 / Math.PI;
        double endAngle = Math.Atan2(ey - cy, ex - cx) * 180.0 / Math.PI;

        // 规范化到 [0, 360)
        startAngle = (startAngle + 360.0) % 360.0;
        endAngle = (endAngle + 360.0) % 360.0;

        // 确保 startAngle < endAngle（顺时针方向）
        if (startAngle > endAngle)
            endAngle += 360.0;

        double arcAngle = endAngle - startAngle;

        Mat mask = Mat.Zeros(imageHeight, imageWidth, MatType.CV_8UC1);
        Cv2.Ellipse(
            mask,
            new Point((int)Math.Round(cx), (int)Math.Round(cy)),
            new Size((int)Math.Round(radius), (int)Math.Round(radius)),
            0,
            startAngle,
            startAngle + arcAngle,
            Scalar.White,
            -1
        );

        // 连接圆心到两个边界点，形成完整扇形三角区域
        Point center = new((int)Math.Round(cx), (int)Math.Round(cy));
        Point p1 = new((int)Math.Round(sx), (int)Math.Round(sy));
        Point p2 = new((int)Math.Round(ex), (int)Math.Round(ey));
        List<List<Point>> triangle = new()
        {
            new List<Point> { center, p1, p2 },
        };
        Cv2.FillPoly(mask, triangle, Scalar.White);

        return mask;
    }

    /// <summary>
    /// 提取单个 ROI 掩膜的几何元数据：外接矩形、面积、简化轮廓。
    /// </summary>
    private static RoiMetadata ExtractRoiMetadata(Mat mask, string name, string typeName, int index)
    {
        using Mat nonZeroMat = new Mat();
        Cv2.FindNonZero(mask, nonZeroMat);
        if (nonZeroMat.Empty())
        {
            return new RoiMetadata
            {
                Name = name,
                Index = index,
                Type = typeName,
                BoundingRect = new RoiRect(),
                Area = 0,
                ContourPoints = new List<RoiPoint>(),
            };
        }

        Rect boundingRect = Cv2.BoundingRect(nonZeroMat);
        double area = Cv2.CountNonZero(mask);

        // 轮廓点集
        List<RoiPoint> contourPoints = new();
        using (Mat tempMask = mask.Clone())
        {
            Cv2.FindContours(
                tempMask,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

            if (contours.Length > 0)
            {
                Point[] largest = contours.OrderByDescending(c => c.Length).First();
                foreach (Point pt in largest)
                    contourPoints.Add(new RoiPoint { X = pt.X, Y = pt.Y });
            }
        }

        return new RoiMetadata
        {
            Name = name,
            Index = index,
            Type = typeName,
            BoundingRect = new RoiRect
            {
                X = boundingRect.X,
                Y = boundingRect.Y,
                Width = boundingRect.Width,
                Height = boundingRect.Height,
            },
            Area = area,
            ContourPoints = contourPoints,
        };
    }

    // ── 校验方法 ────────────────────────────────────────────────────────────────

    private static void ValidateRect(RoiDefinition roi)
    {
        if (roi.Width <= 0)
            throw new InvalidOperationException($"矩形 ROI 宽度必须大于 0，当前值：{roi.Width}。");
        if (roi.Height <= 0)
            throw new InvalidOperationException($"矩形 ROI 高度必须大于 0，当前值：{roi.Height}。");
    }

    private static Rect ClampRect(int x, int y, int w, int h, int imageWidth, int imageHeight)
    {
        int x1 = Math.Max(0, x);
        int y1 = Math.Max(0, y);
        int x2 = Math.Min(imageWidth, x + w);
        int y2 = Math.Min(imageHeight, y + h);
        return new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private static RoiProjectionMapping? ResolveProjectionMapping(
        RoiBaseImageInfo? baseImage,
        int imageWidth,
        int imageHeight
    )
    {
        RoiProjectionMapping? mapping = baseImage?.ProjectionMapping;
        if (mapping is null)
        {
            return null;
        }

        if (mapping.WorldMaxX <= mapping.WorldMinX)
        {
            throw new InvalidOperationException("ROI 映射参数无效：worldMaxX 必须大于 worldMinX。");
        }

        if (mapping.WorldMaxY <= mapping.WorldMinY)
        {
            throw new InvalidOperationException("ROI 映射参数无效：worldMaxY 必须大于 worldMinY。");
        }

        if (mapping.ImageWidth <= 0)
        {
            mapping.ImageWidth = imageWidth;
        }

        if (mapping.ImageHeight <= 0)
        {
            mapping.ImageHeight = imageHeight;
        }

        if (string.IsNullOrWhiteSpace(mapping.ViewLabel))
        {
            mapping.ViewLabel = baseImage?.SelectedLabel ?? "XY";
        }

        return mapping;
    }

    private static void ValidateBaseImageConsistency(
        RoiBaseImageInfo? baseImage,
        int imageWidth,
        int imageHeight
    )
    {
        if (baseImage is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(baseImage.SelectedBlobName))
        {
            bool selectedExists = baseImage.PreviewImages.Any(p =>
                string.Equals(
                    p.BlobName,
                    baseImage.SelectedBlobName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (!selectedExists)
            {
                throw new InvalidOperationException(
                    "ROI 底图配置无效：SelectedBlobName 不在 PreviewImages 列表中。"
                );
            }
        }

        if (
            !string.IsNullOrWhiteSpace(baseImage.SelectedLabel)
            && baseImage.PreviewImages.Count > 0
            && !baseImage.PreviewImages.Any(p =>
                string.Equals(p.Label, baseImage.SelectedLabel, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException(
                "ROI 底图配置无效：SelectedLabel 不在 PreviewImages 列表中。"
            );
        }

        RoiProjectionMapping? mapping = baseImage.ProjectionMapping;
        if (mapping is null)
        {
            return;
        }

        if (
            !string.IsNullOrWhiteSpace(baseImage.SelectedLabel)
            && !string.Equals(
                baseImage.SelectedLabel,
                mapping.ViewLabel,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                "ROI 映射参数无效：ProjectionMapping.ViewLabel 与 SelectedLabel 不一致。"
            );
        }

        if (mapping.ImageWidth > 0 && mapping.ImageWidth != imageWidth)
        {
            throw new InvalidOperationException(
                $"ROI 映射参数无效：ProjectionMapping.ImageWidth={mapping.ImageWidth} 与输入图像宽度 {imageWidth} 不一致。"
            );
        }

        if (mapping.ImageHeight > 0 && mapping.ImageHeight != imageHeight)
        {
            throw new InvalidOperationException(
                $"ROI 映射参数无效：ProjectionMapping.ImageHeight={mapping.ImageHeight} 与输入图像高度 {imageHeight} 不一致。"
            );
        }
    }

    // ── SVG Path 解析 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 解析 SVG Path d 字符串为离散点序列。
    /// 支持命令：M/m（移动）、L/l（直线）、H/h（水平线）、V/v（垂直线）、
    /// C/c（三次贝塞尔，近似为线段）、Z/z（闭合）。
    /// </summary>
    private static List<Point> ParseSvgPath(string d)
    {
        List<Point> points = new();
        double curX = 0,
            curY = 0;
        double startX = 0,
            startY = 0;

        string normalized = System.Text.RegularExpressions.Regex.Replace(
            d.Trim(),
            @"([a-zA-Z])",
            " $1 "
        );
        string[] tokens = normalized.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        char currentCommand = ' ';
        List<double> args = new();
        int i = 0;

        while (i < tokens.Length)
        {
            string token = tokens[i];

            if (char.IsLetter(token[0]))
            {
                if (args.Count > 0 && currentCommand != ' ')
                {
                    ApplyCommand(
                        currentCommand,
                        args,
                        ref curX,
                        ref curY,
                        ref startX,
                        ref startY,
                        points
                    );
                    args.Clear();
                }
                currentCommand = token[0];
                i++;
            }
            else
            {
                args.Add(double.Parse(token, CultureInfo.InvariantCulture));
                i++;

                int expectedArgs = currentCommand switch
                {
                    'M' or 'm' or 'L' or 'l' => 2,
                    'H' or 'h' or 'V' or 'v' => 1,
                    'C' or 'c' => 6,
                    'Z' or 'z' => 0,
                    _ => 0,
                };

                if (args.Count >= expectedArgs && expectedArgs > 0)
                {
                    ApplyCommand(
                        currentCommand,
                        args,
                        ref curX,
                        ref curY,
                        ref startX,
                        ref startY,
                        points
                    );
                    args.Clear();
                }
            }
        }

        if (args.Count > 0 && currentCommand != ' ')
        {
            ApplyCommand(currentCommand, args, ref curX, ref curY, ref startX, ref startY, points);
        }

        return points;
    }

    private static void ApplyCommand(
        char command,
        List<double> args,
        ref double curX,
        ref double curY,
        ref double startX,
        ref double startY,
        List<Point> points
    )
    {
        switch (command)
        {
            case 'M':
                curX = args[0];
                curY = args[1];
                startX = curX;
                startY = curY;
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'm':
                curX += args[0];
                curY += args[1];
                startX = curX;
                startY = curY;
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'L':
                curX = args[0];
                curY = args[1];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'l':
                curX += args[0];
                curY += args[1];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'H':
                curX = args[0];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'h':
                curX += args[0];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'V':
                curY = args[0];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'v':
                curY += args[0];
                points.Add(new Point((int)Math.Round(curX), (int)Math.Round(curY)));
                break;

            case 'C':
                SubdivideBezier(
                    curX,
                    curY,
                    args[0],
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args[5],
                    points
                );
                curX = args[4];
                curY = args[5];
                break;

            case 'c':
                SubdivideBezier(
                    curX,
                    curY,
                    curX + args[0],
                    curY + args[1],
                    curX + args[2],
                    curY + args[3],
                    curX + args[4],
                    curY + args[5],
                    points
                );
                curX += args[4];
                curY += args[5];
                break;

            case 'Z':
            case 'z':
                curX = startX;
                curY = startY;
                points.Add(new Point((int)Math.Round(startX), (int)Math.Round(startY)));
                break;
        }
    }

    /// <summary>
    /// 三次贝塞尔曲线细分近似为线段，步长 20 段。
    /// </summary>
    private static void SubdivideBezier(
        double x0,
        double y0,
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3,
        List<Point> points
    )
    {
        const int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1 - t;
            double uu = u * u;
            double uuu = uu * u;
            double tt = t * t;
            double ttt = tt * t;

            double x = uuu * x0 + 3 * uu * t * x1 + 3 * u * tt * x2 + ttt * x3;
            double y = uuu * y0 + 3 * uu * t * y1 + 3 * u * tt * y2 + ttt * y3;
            points.Add(new Point((int)Math.Round(x), (int)Math.Round(y)));
        }
    }
}
