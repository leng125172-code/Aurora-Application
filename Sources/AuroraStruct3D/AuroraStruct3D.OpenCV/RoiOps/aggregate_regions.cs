using System.Text.Json;

namespace AuroraStruct3D.OpenCV.RoiOps;

[Guid("a8b3c4d5-e6f7-890a-bcde-f01234567890")]
[Category("2D预处理")]
[DisplayName("聚合区域数据")]
[Description(
    "将多个 ROI 区域的元数据和高度值聚合为统一的 JSON 数组，供结果标注算子和接口返回使用。"
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

        for (int i = 1; i <= _regionCount; i++)
        {
            string? metadataJson = context.Get<string>($"roi_{i}_metadata");
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                Console.WriteLine($"[aggregate_regions] roi_{i}_metadata 为空，跳过");
                continue;
            }

            double height = context.Get<double>($"height_{i}");

            RoiPartitionMetadata? meta = null;
            try
            {
                meta = JsonSerializer.Deserialize<RoiPartitionMetadata>(metadataJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"[aggregate_regions] roi_{i}_metadata JSON解析失败: {ex.Message}"
                );
                continue;
            }

            if (meta?.Rois == null || meta.Rois.Count == 0)
            {
                Console.WriteLine($"[aggregate_regions] roi_{i}_metadata 中未找到有效的 ROIs");
                continue;
            }

            var roi = meta.Rois.First();
            var mapping = meta.ProjectionMapping;

            Console.WriteLine($"[aggregate_regions] roi_{i} 解析结果:");
            Console.WriteLine($"  - roi.Name = '{roi.Name}'");
            Console.WriteLine($"  - roi.Type = '{roi.Type}'");
            Console.WriteLine($"  - roi.Index = {roi.Index}");
            Console.WriteLine(
                $"  - roi.BoundingRect = ({roi.BoundingRect.X}, {roi.BoundingRect.Y}, {roi.BoundingRect.Width}, {roi.BoundingRect.Height})"
            );

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
            Console.WriteLine(
                $"[aggregate_regions] roi_{i} 使用的区域名称: '{regionName}' (原始 roi.Name 为空? {string.IsNullOrWhiteSpace(roi.Name)})"
            );

            regions.Add(
                new annotate_height_diff_result.AnnotateRegion
                {
                    Name = regionName,
                    Height = height,
                    Roi = roi,
                    ProjectionMapping = mapping,
                }
            );

            resultRegions.Add(
                new RegionMeasurementResult
                {
                    Name = regionName,
                    X = worldX,
                    Y = worldY,
                    Z = height,
                }
            );
        }

        if (regions.Count == 0)
        {
            throw new InvalidOperationException("未找到有效的区域数据。");
        }

        var result = new HeightDiffResult { Regions = resultRegions };

        string regionsJson = JsonSerializer.Serialize(regions, JsonOptions);
        string resultJson = JsonSerializer.Serialize(result, JsonOptions);

        context.Set("regions_json", regionsJson);
        context.Set("result_json", resultJson);
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
    }

    public class HeightDiffResult
    {
        public List<RegionMeasurementResult> Regions { get; set; } = new();
    }
}
