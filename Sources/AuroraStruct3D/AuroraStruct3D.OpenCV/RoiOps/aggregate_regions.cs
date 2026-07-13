using System.Text.Json;
using OpenCvSharp;

namespace AuroraStruct3D.OpenCV.RoiOps;

[Guid("a8b3c4d5-e6f7-890a-bcde-f01234567890")]
[Category("2D预处理")]
[DisplayName("聚合区域数据")]
[Description(
    "将多个 ROI 区域的元数据、高度值和平面轮廓聚合为统一的 JSON 数组，供结果标注算子和接口返回使用。"
)]
public class aggregate_regions : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters => null;

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<string>
            {
                ParameterName = "regions_json",
                ParameterType = typeof(string),
                DisplayName = "区域数据JSON",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<string>
            {
                ParameterName = "result_json",
                ParameterType = typeof(string),
                DisplayName = "测量结果JSON",
                ControlType = PortControlType.Variable,
            },
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                ParameterType = typeof(bool),
                DisplayName = "汇总判定",
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "regionCount",
                DisplayName = "区域数量",
                ParameterType = typeof(int),
                DefaultValue = "2",
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
        };

    private readonly int _regionCount;
    private readonly int _referenceRegionIndex;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private bool _disposed;

    public aggregate_regions(int regionCount = 2, int referenceRegionIndex = 0)
    {
        _regionCount = regionCount;
        _referenceRegionIndex = referenceRegionIndex;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_regionCount <= 0)
        {
            throw new InvalidOperationException("区域数量必须是正整数。");
        }

        var regions = new List<annotate_height_diff_result.AnnotateRegion>();
        var resultRegions = new List<RegionMeasurementResult>();
        // 收集所有区域的高度差结果
        var diffs = new List<PlaneDiffResult>();

        // 基准区域的高度值（用于计算高度差）
        double refHeight = double.NaN;

        for (int i = 1; i <= _regionCount; i++)
        {
            string? metadataJson = context.Get<string>($"roi_{i}_metadata");
            if (string.IsNullOrWhiteSpace(metadataJson))
                continue;

            double height = context.Get<double>($"height_{i}");

            if (i == _referenceRegionIndex + 1)
            {
                refHeight = height;
            }

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

            double centerPx = roi.BoundingRect.X + roi.BoundingRect.Width / 2;
            double centerPy = roi.BoundingRect.Y + roi.BoundingRect.Height / 2;

            double worldX = 0,
                worldY = 0;
            if (mapping != null)
            {
                worldX =
                    mapping.WorldMinX
                    + centerPx / mapping.ImageWidth * (mapping.WorldMaxX - mapping.WorldMinX);
                worldY =
                    mapping.WorldMinY
                    + centerPy / mapping.ImageHeight * (mapping.WorldMaxY - mapping.WorldMinY);
            }

            string regionName = string.IsNullOrWhiteSpace(roi.Name) ? $"Region_{i}" : roi.Name;

            // 读取该区域的平面参数（由独立 RANSAC 拟合产生）
            double[]? planeParams = ReadPlaneParams(context, $"plane_{i}_params");

            // 计算该区域平面内点的 2D 投影轮廓
            List<RoiPoint> outlinePoints = ComputePlaneOutline(
                context,
                $"region_{i}_inlier_cloud",
                mapping
            );

            bool isSelected = (i == _referenceRegionIndex + 1);

            regions.Add(
                new annotate_height_diff_result.AnnotateRegion
                {
                    Name = regionName,
                    Height = height,
                    Roi = roi,
                    ProjectionMapping = mapping,
                    PlaneParams = planeParams,
                    OutlinePoints = outlinePoints,
                    IsSelected = isSelected,
                }
            );

            resultRegions.Add(
                new RegionMeasurementResult
                {
                    Name = regionName,
                    X = worldX,
                    Y = worldY,
                    Z = height,
                    PlaneParams = planeParams,
                }
            );

            // 计算与基准面的高度差（非基准区域，从 plane_height_diff 算子读取）
            if (i != _referenceRegionIndex + 1 && !double.IsNaN(refHeight))
            {
                double signedDiff = context.Get<double>($"signed_diff_{i}");
                double absDiff = context.Get<double>($"abs_diff_{i}");
                bool isOk = context.Get<bool>($"is_ok_{i}");

                diffs.Add(
                    new PlaneDiffResult
                    {
                        Ref = regions[_referenceRegionIndex].Name,
                        Target = regionName,
                        SignedDiff = signedDiff,
                        AbsDiff = absDiff,
                        IsOk = isOk,
                    }
                );
            }
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("未找到有效的区域数据。");
        }

        var result = new HeightDiffResult { Regions = resultRegions, Diffs = diffs };

        string regionsJson = JsonSerializer.Serialize(regions, JsonOptions);
        string resultJson = JsonSerializer.Serialize(result, JsonOptions);

        // 汇总判定：所有高度差均 OK 才算 OK
        bool overallOk = diffs.Count == 0 || diffs.All(d => d.IsOk);

        context.Set("regions_json", regionsJson);
        context.Set("result_json", resultJson);
        context.Set("is_ok", overallOk);
    }

    /// <summary>
    /// 从上下文中读取平面参数 Mat 并转为 double 数组。
    /// </summary>
    private static double[]? ReadPlaneParams(IWorkflowContext context, string variableName)
    {
        Mat? planeMat = context.Get<Mat>(variableName);
        if (planeMat is null || planeMat.Empty() || planeMat.Rows < 4)
            return null;

        return new[]
        {
            planeMat.Get<double>(0, 0),
            planeMat.Get<double>(1, 0),
            planeMat.Get<double>(2, 0),
            planeMat.Get<double>(3, 0),
        };
    }

    /// <summary>
    /// 计算平面内点点云在 2D 图像上的投影轮廓（凸包）。
    /// </summary>
    private static List<RoiPoint> ComputePlaneOutline(
        IWorkflowContext context,
        string variableName,
        RoiProjectionMapping? mapping
    )
    {
        var result = new List<RoiPoint>();

        PointCloudData? inlierCloud = context.Get<PointCloudData>(variableName);
        if (inlierCloud?.PointCloud is null || inlierCloud.PointCloud.Empty() || mapping is null)
            return result;

        Mat points = inlierCloud.PointCloud;
        int pointCount = points.Rows;

        double worldMinX = mapping.WorldMinX;
        double worldMaxX = mapping.WorldMaxX;
        double worldMinY = mapping.WorldMinY;
        double worldMaxY = mapping.WorldMaxY;
        double worldRangeX = worldMaxX - worldMinX;
        double worldRangeY = worldMaxY - worldMinY;
        if (Math.Abs(worldRangeX) < 1e-6 || Math.Abs(worldRangeY) < 1e-6)
            return result;

        int imageWidth = mapping.ImageWidth;
        int imageHeight = mapping.ImageHeight;

        // 投影到 2D 像素坐标
        var projected = new List<Point>();
        for (int i = 0; i < pointCount; i++)
        {
            float worldX = points.Get<float>(i, 0);
            float worldY = points.Get<float>(i, 1);

            int px = (int)((worldX - worldMinX) / worldRangeX * (imageWidth - 1));
            int py = (int)((worldY - worldMinY) / worldRangeY * (imageHeight - 1));

            px = Math.Clamp(px, 0, imageWidth - 1);
            py = Math.Clamp(py, 0, imageHeight - 1);

            projected.Add(new Point(px, py));
        }

        if (projected.Count < 3)
            return result;

        // 计算凸包
        Point[] hull = Cv2.ConvexHull(projected.ToArray());

        foreach (Point pt in hull)
        {
            result.Add(new RoiPoint { X = pt.X, Y = pt.Y });
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class RegionMeasurementResult
    {
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double[]? PlaneParams { get; set; }
    }

    public class PlaneDiffResult
    {
        public string Ref { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public double SignedDiff { get; set; }
        public double AbsDiff { get; set; }
        public bool IsOk { get; set; }
    }

    public class HeightDiffResult
    {
        public List<RegionMeasurementResult> Regions { get; set; } = new();
        public List<PlaneDiffResult> Diffs { get; set; } = new();
    }
}
