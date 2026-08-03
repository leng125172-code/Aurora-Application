using System.Globalization;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;

namespace AuroraStruct3D.OpenCV.RoiOps;

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
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
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
                Name = "regionCount",
                DisplayName = "区域数量",
                ParameterType = typeof(int),
                DefaultValue = "4",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "referenceRegionIndex",
                DisplayName = "基准区域索引",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
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
            new ConfigParameter
            {
                Name = "viewType",
                DisplayName = "视图类型",
                ParameterType = typeof(string),
                DefaultValue = "top",
                ValueLimit = new[] { "top", "tilted" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "tiltAngleY",
                DisplayName = "Y轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "tiltAngleX",
                DisplayName = "X轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "tiltAngleZ",
                DisplayName = "Z轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "0",
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

    private readonly int _regionCount;
    private readonly int _referenceRegionIndex;
    private readonly int _outlineThickness;
    private readonly int _highlightThickness;
    private readonly Scalar _highlightColor;
    private readonly string _viewType;
    private readonly double _tiltAngleYDeg;
    private readonly double _tiltAngleXDeg;
    private readonly double _tiltAngleZDeg;
    private bool _disposed;

    public render_plane_outlines(
        int regionCount = 4,
        int referenceRegionIndex = 0,
        int outlineThickness = 2,
        int highlightThickness = 3,
        string highlightColor = "#00FF00",
        string viewType = "top",
        double tiltAngleY = 30,
        double tiltAngleX = 30,
        double tiltAngleZ = 0
    )
    {
        _regionCount = regionCount;
        _referenceRegionIndex = referenceRegionIndex;
        _outlineThickness = Math.Max(1, outlineThickness);
        _highlightThickness = Math.Max(1, highlightThickness);
        _highlightColor = ParseHexColor(highlightColor);
        _viewType = viewType;
        _tiltAngleYDeg = tiltAngleY;
        _tiltAngleXDeg = tiltAngleX;
        _tiltAngleZDeg = tiltAngleZ;
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

        // 轮廓图作为前端可叠加的图层输出：不复制输入像素，也不填充 ROI 区域。
        // 保留原始尺寸以便与底图精确对齐，背景维持完全透明。
        Mat output = Mat.Zeros(inputMat.Rows, inputMat.Cols, MatType.CV_8UC4);
        int imageWidth = inputMat.Width;
        int imageHeight = inputMat.Height;

        PointCloudData? fullPointCloud = null;
        if (_viewType == "tilted")
        {
            fullPointCloud = context.Get<PointCloudData>("input_point_cloud");
        }

        var regions = new List<RegionInfo>();
        for (int i = 1; i <= _regionCount; i++)
        {
            string? metadataJson = context.Get<string>($"roi_{i}_metadata");
            if (string.IsNullOrWhiteSpace(metadataJson))
                continue;

            RoiPartitionMetadata? meta = null;
            try
            {
                meta = JsonSerializer.Deserialize<RoiPartitionMetadata>(metadataJson, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (meta?.Rois == null || meta.Rois.Count == 0)
                continue;

            var roi = meta.Rois.First();
            var mapping = meta.ProjectionMapping;

            string regionName = string.IsNullOrWhiteSpace(roi.Name) ? $"Region_{i}" : roi.Name;
            bool isSelected = (i == _referenceRegionIndex + 1);

            PointCloudData? inlierCloud = context.Get<PointCloudData>($"region_{i}_inlier_cloud");
            Mat? planeParams = context.Get<Mat>($"plane_{i}_params");

            regions.Add(
                new RegionInfo
                {
                    Name = regionName,
                    Roi = roi,
                    ProjectionMapping = mapping,
                    InlierCloud = inlierCloud,
                    IsSelected = isSelected,
                    PlaneParams = planeParams,
                }
            );
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("未找到有效的区域数据。");
        }

        // 第一步：收集所有区域的内点 Z 值，计算全局 Z 范围，用于 Jet 色彩映射
        double overallMinZ = double.MaxValue;
        double overallMaxZ = double.MinValue;
        foreach (var region in regions)
        {
            if (region.InlierCloud?.PointCloud is null)
                continue;
            Mat cloud = region.InlierCloud.PointCloud;
            int cloudRows = cloud.Rows;
            for (int r = 0; r < cloudRows; r++)
            {
                float z = cloud.Get<float>(r, 2);
                if (z < overallMinZ)
                    overallMinZ = z;
                if (z > overallMaxZ)
                    overallMaxZ = z;
            }
        }
        if (overallMaxZ <= overallMinZ)
            overallMaxZ = overallMinZ + 1d;

        // 第二步：计算每个区域的轮廓点
        var regionOutlines = new List<(RegionInfo region, int index, Point[] outlinePoints)>();
        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            Point[] outlinePoints =
                _viewType == "tilted" && fullPointCloud?.PointCloud is not null
                    ? ComputeTiltedViewOutline(
                        region,
                        fullPointCloud.PointCloud,
                        imageWidth,
                        imageHeight
                    )
                    : ComputeTopViewOutline(region, imageWidth, imageHeight);

            if (outlinePoints.Length < 3)
                continue;

            regionOutlines.Add((region, i, outlinePoints));
        }

        // 仅绘制轮廓和标签，输出背景透明，可由前端与任意底图组合显示。
        foreach (var (region, _, outlinePoints) in regionOutlines)
        {
            // 3D 平面 ROI 的轮廓和名称固定使用黑色；其他结果标注仍可跟随主题。
            Scalar drawColor = new(0, 0, 0, 255);
            int thickness = region.IsSelected ? _highlightThickness : _outlineThickness;

            Cv2.Polylines(output, new[] { outlinePoints }, true, drawColor, thickness);

            Point labelPoint = new(outlinePoints[0].X, Math.Max(20, outlinePoints[0].Y - 8));
            Cv2.PutText(
                output,
                region.Name,
                labelPoint,
                HersheyFonts.HersheySimplex,
                0.45,
                drawColor,
                2
            );
        }

        context.Set("output_mat", output);
    }

    /// <summary>
    /// 使用 ROI 的实际轮廓点（ContourPoints）生成俯视图轮廓。
    /// 轮廓点来自原始掩膜图像，按比例缩放到输出图像坐标系。
    /// </summary>
    private static Point[] ComputeTopViewOutline(RegionInfo region, int imageWidth, int imageHeight)
    {
        if (region.ProjectionMapping is null)
            return Array.Empty<Point>();

        var contourPoints = region.Roi.ContourPoints;
        if (contourPoints is null || contourPoints.Count < 3)
            return Array.Empty<Point>();

        var mapping = region.ProjectionMapping;
        double scaleX = (double)imageWidth / mapping.ImageWidth;
        double scaleY = (double)imageHeight / mapping.ImageHeight;

        return contourPoints
            .Select(p => new Point((int)Math.Round(p.X * scaleX), (int)Math.Round(p.Y * scaleY)))
            .ToArray();
    }

    /// <summary>
    /// 根据拟合平面的 Z 高度，通过 Jet 色彩映射计算平面填充色。
    /// 使用平面参数 [a,b,c,d] 计算 ROI 中心点的 Z 值，确保代表真正的拟合平面而非内点噪声。
    /// </summary>
    private static Scalar ComputePlaneFillColor(
        RegionInfo region,
        double overallMinZ,
        double overallMaxZ
    )
    {
        double zCenter;

        if (
            region.PlaneParams is not null
            && region.PlaneParams.Rows == 1
            && region.PlaneParams.Cols == 4
            && region.ProjectionMapping is not null
        )
        {
            float a = region.PlaneParams.Get<float>(0, 0);
            float b = region.PlaneParams.Get<float>(0, 1);
            float c = region.PlaneParams.Get<float>(0, 2);
            float d = region.PlaneParams.Get<float>(0, 3);

            if (Math.Abs(c) < 1e-9f)
            {
                // 平面垂直于 XY 平面（c≈0），用内点平均 Z 作为回退
                zCenter = ComputeAverageZ(region.InlierCloud);
            }
            else
            {
                var mapping = region.ProjectionMapping;
                double centerX = (mapping.WorldMinX + mapping.WorldMaxX) / 2.0;
                double centerY = (mapping.WorldMinY + mapping.WorldMaxY) / 2.0;
                zCenter = -(a * centerX + b * centerY + d) / (double)c;
            }
        }
        else
        {
            zCenter = ComputeAverageZ(region.InlierCloud);
        }

        double range = overallMaxZ - overallMinZ;
        double ratio = (zCenter - overallMinZ) / range;
        ratio = Math.Clamp(ratio, 0d, 1d);

        using Mat normalized = new Mat(1, 1, MatType.CV_8UC1, Scalar.All((byte)(ratio * 255)));
        using Mat colorized = new();
        Cv2.ApplyColorMap(normalized, colorized, ColormapTypes.Jet);
        Vec3b color = colorized.Get<Vec3b>(0, 0);

        return new Scalar(color.Item0, color.Item1, color.Item2, 180);
    }

    /// <summary>计算内点云的 Z 坐标平均值。</summary>
    private static double ComputeAverageZ(PointCloudData? inlierCloud)
    {
        if (inlierCloud?.PointCloud is null || inlierCloud.PointCloud.Empty())
            return 0d;

        Mat cloud = inlierCloud.PointCloud;
        int count = cloud.Rows;
        if (count == 0)
            return 0d;

        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += cloud.Get<float>(i, 2);

        return sum / count;
    }

    private Point[] ComputeTiltedViewOutline(
        RegionInfo region,
        Mat pointCloud,
        int imageWidth,
        int imageHeight
    )
    {
        if (region.ProjectionMapping is null)
            return Array.Empty<Point>();

        var mapping = region.ProjectionMapping;
        int pointCount = pointCloud.Rows;

        double thetaY = _tiltAngleYDeg * Math.PI / 180.0;
        double cosTY = Math.Cos(thetaY);
        double sinTY = Math.Sin(thetaY);

        double thetaX = _tiltAngleXDeg * Math.PI / 180.0;
        double cosTX = Math.Cos(thetaX);
        double sinTX = Math.Sin(thetaX);

        double thetaZ = _tiltAngleZDeg * Math.PI / 180.0;
        double cosTZ = Math.Cos(thetaZ);
        double sinTZ = Math.Sin(thetaZ);

        double worldMinX = mapping.WorldMinX;
        double worldMaxX = mapping.WorldMaxX;
        double worldMinY = mapping.WorldMinY;
        double worldMaxY = mapping.WorldMaxY;
        double rangeX = worldMaxX - worldMinX;
        double rangeY = worldMaxY - worldMinY;

        if (Math.Abs(rangeX) < 1e-6 || Math.Abs(rangeY) < 1e-6)
            return Array.Empty<Point>();

        var pixelPoints = new List<Point>();

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);

            if (!IsPointInRoi(x, y, region, mapping))
                continue;

            float z = pointCloud.Get<float>(i, 2);

            float rxY = (float)(x * cosTY + z * sinTY);
            float rzY = (float)(-x * sinTY + z * cosTY);

            float rxX = rxY;
            float ryX = (float)(y * cosTX - rzY * sinTX);
            float rzX = (float)(y * sinTX + rzY * cosTX);

            float rx = (float)(rxX * cosTZ - ryX * sinTZ);
            float ry = (float)(rxX * sinTZ + ryX * cosTZ);

            int px = (int)((rx - worldMinX) / rangeX * (imageWidth - 1));
            int py = (int)((ry - worldMinY) / rangeY * (imageHeight - 1));
            px = Math.Clamp(px, 0, imageWidth - 1);
            py = Math.Clamp(py, 0, imageHeight - 1);
            pixelPoints.Add(new Point(px, py));
        }

        if (pixelPoints.Count < 3)
            return Array.Empty<Point>();

        Point[] hull = Cv2.ConvexHull(pixelPoints.ToArray());
        return hull;
    }

    private static bool IsPointInRoi(
        float worldX,
        float worldY,
        RegionInfo region,
        RoiProjectionMapping mapping
    )
    {
        double worldMinX = mapping.WorldMinX;
        double worldMaxX = mapping.WorldMaxX;
        double worldMinY = mapping.WorldMinY;
        double worldMaxY = mapping.WorldMaxY;

        if (worldX < worldMinX || worldX > worldMaxX || worldY < worldMinY || worldY > worldMaxY)
            return false;

        double worldRangeX = worldMaxX - worldMinX;
        double worldRangeY = worldMaxY - worldMinY;

        int px = (int)((worldX - worldMinX) / worldRangeX * (mapping.ImageWidth - 1));
        int py = (int)((worldY - worldMinY) / worldRangeY * (mapping.ImageHeight - 1));

        px = Math.Clamp(px, 0, mapping.ImageWidth - 1);
        py = Math.Clamp(py, 0, mapping.ImageHeight - 1);

        var boundingRect = region.Roi.BoundingRect;
        return px >= boundingRect.X
            && px <= boundingRect.X + boundingRect.Width
            && py >= boundingRect.Y
            && py <= boundingRect.Y + boundingRect.Height;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

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
        switch (inputMat.Channels())
        {
            case 4:
                return inputMat.Clone();
            case 3:
            {
                // BGR → BGRA 转换后 alpha 通道可能为 0，导致颜色变浅，需手动设为 255
                Mat output = new();
                Cv2.CvtColor(inputMat, output, ColorConversionCodes.BGR2BGRA);
                using Mat alphaChannel = new Mat(output.Size(), MatType.CV_8UC1, Scalar.All(255));
                Mat[] channels = Cv2.Split(output);
                channels[3].Dispose();
                channels[3] = alphaChannel.Clone();
                Cv2.Merge(channels, output);
                foreach (var ch in channels)
                    ch.Dispose();
                return output;
            }
            default:
            {
                Mat output = new();
                Cv2.CvtColor(inputMat, output, ColorConversionCodes.GRAY2BGRA);
                using Mat alphaChannel = new Mat(output.Size(), MatType.CV_8UC1, Scalar.All(255));
                Mat[] channels = Cv2.Split(output);
                channels[3].Dispose();
                channels[3] = alphaChannel.Clone();
                Cv2.Merge(channels, output);
                foreach (var ch in channels)
                    ch.Dispose();
                return output;
            }
        }
    }

    private class RegionInfo
    {
        public string Name { get; set; } = string.Empty;
        public RoiMetadata Roi { get; set; } = new();
        public RoiProjectionMapping? ProjectionMapping { get; set; }
        public PointCloudData? InlierCloud { get; set; }
        public bool IsSelected { get; set; }
        public Mat? PlaneParams { get; set; }
    }
}
