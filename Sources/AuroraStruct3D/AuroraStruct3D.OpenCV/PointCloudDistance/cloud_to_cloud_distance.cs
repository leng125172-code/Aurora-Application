using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudDistance;

/// <summary>
/// 工作流算子：点云到点云距离计算。
/// <para>
/// 计算源点云中每个点到目标点云的最近欧氏距离，输出距离矩阵、距离热力图和统计信息。
/// 常用于 3D 缺陷检测中比较实际点云与参考模板的差异，距离值越大表示偏差越严重。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>构建目标点云的空间哈希网格，用于加速最近邻搜索</item>
///   <item>对源点云中每个点，在目标点云中搜索最近邻点</item>
///   <item>计算两点间的欧氏距离，存入距离矩阵</item>
///   <item>统计最大距离、平均距离、标准差、超出阈值的点数</item>
///   <item>生成 2D 距离热力图用于可视化</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>source_cloud</c>（PointCloudData）— 源点云（待比较点云）</item>
///   <item>输入 <c>target_cloud</c>（PointCloudData）— 目标点云（参考点云）</item>
///   <item>输出 <c>distance_mat</c>（Mat）— 每个点的距离值（N×1，CV_64FC1）</item>
///   <item>输出 <c>distance_image</c>（Mat）— 2D 距离热力图（CV_8UC3，伪彩色）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0003-4000-8000-000000000014")]
[Category("3D拟合测量")]
[DisplayName("点云距离")]
[Description("比对两片点云的偏差，出张热力图看哪儿差得多，检缺陷用。")]
public class cloud_to_cloud_distance : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "source_cloud", DisplayName = "源点云" },
            new PointCloudData() { ParameterName = "target_cloud", DisplayName = "目标点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "distance_mat", DisplayName = "距离矩阵" },
            new MatImg() { ParameterName = "distance_image", DisplayName = "距离图像" },
            new VisionParameter<string>
            {
                ParameterName = "stats_json",
                DisplayName = "统计信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "缺陷阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxSearchDistance",
                DisplayName = "最大搜索距离",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "投影分辨率",
                ParameterType = typeof(int),
                DefaultValue = "512",
                ValueLimit = new[] { "256", "512", "1024", "2048" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _distanceThreshold;
    private readonly double _maxSearchDistance;
    private readonly int _imageResolution;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化点云到点云距离算子。
    /// </summary>
    /// <param name="distanceThreshold">缺陷阈值，距离超过此值视为缺陷点。</param>
    /// <param name="maxSearchDistance">最大搜索距离，超过此距离的点标记为无效。</param>
    /// <param name="imageResolution">2D 投影热力图分辨率（边长像素数）。</param>
    public cloud_to_cloud_distance(
        double distanceThreshold = 0.1,
        double maxSearchDistance = 0.5,
        int imageResolution = 512
    )
    {
        _distanceThreshold = distanceThreshold;
        _maxSearchDistance = maxSearchDistance;
        _imageResolution = imageResolution;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData sourceData =
            context.Get<PointCloudData>("source_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'source_cloud' 为空，请确认输入绑定已正确设置。"
            );

        PointCloudData targetData =
            context.Get<PointCloudData>("target_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'target_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat sourceCloud = sourceData.PointCloud!;
        Mat targetCloud = targetData.PointCloud!;

        if (sourceCloud is null || sourceCloud.Empty())
            throw new InvalidOperationException("源点云为空，无法计算点云到点云距离。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法计算点云到点云距离。");

        int sourceCount = sourceCloud.Rows;
        int targetCount = targetCloud.Rows;

        if (sourceCount < 1)
            throw new InvalidOperationException("源点云点数不足，无法计算。");
        if (targetCount < 1)
            throw new InvalidOperationException("目标点云点数不足，无法计算。");

        // 提取目标点云坐标用于构建空间网格
        float[] tgtX = new float[targetCount];
        float[] tgtY = new float[targetCount];
        float[] tgtZ = new float[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            tgtX[i] = targetCloud.Get<float>(i, 0);
            tgtY[i] = targetCloud.Get<float>(i, 1);
            tgtZ[i] = targetCloud.Get<float>(i, 2);
        }

        // 构建空间哈希网格加速最近邻搜索
        var targetGrid = new SpatialHashGrid(tgtX, tgtY, tgtZ, _maxSearchDistance, targetCount);

        double maxSearchDistSq = _maxSearchDistance * _maxSearchDistance;

        // 计算每个源点到目标点云的最近距离
        Mat distances = new Mat(sourceCount, 1, MatType.CV_64FC1);
        double maxDist = 0;
        double sumDist = 0;
        int defectCount = 0;
        int invalidCount = 0;

        for (int i = 0; i < sourceCount; i++)
        {
            float sx = sourceCloud.Get<float>(i, 0);
            float sy = sourceCloud.Get<float>(i, 1);
            float sz = sourceCloud.Get<float>(i, 2);

            // 搜索最近邻
            int nearestIdx = targetGrid.FindNearestNeighbor(sx, sy, sz, out _);

            double dist;
            if (nearestIdx >= 0)
            {
                float dx = sx - tgtX[nearestIdx];
                float dy = sy - tgtY[nearestIdx];
                float dz = sz - tgtZ[nearestIdx];
                dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist > maxSearchDistSq)
                {
                    dist = -1;
                    invalidCount++;
                }
            }
            else
            {
                dist = -1;
                invalidCount++;
            }

            if (dist > 0)
            {
                distances.Set(i, 0, dist);
                sumDist += dist;
                if (dist > maxDist)
                    maxDist = dist;
                if (dist > _distanceThreshold)
                    defectCount++;
            }
            else
            {
                distances.Set(i, 0, double.MaxValue);
            }
        }

        int validCount = sourceCount - invalidCount;
        double meanDist = validCount > 0 ? sumDist / validCount : 0;

        // 计算标准差
        double sumSq = 0;
        for (int i = 0; i < sourceCount; i++)
        {
            double d = distances.Get<double>(i, 0);
            if (d >= 0 && d < double.MaxValue)
            {
                double diff = d - meanDist;
                sumSq += diff * diff;
            }
        }
        double stdDev = validCount > 1 ? Math.Sqrt(sumSq / validCount) : 0;

        // 生成 2D 距离热力图
        Mat distanceImage = GenerateDistanceHeatmap(
            sourceCloud,
            distances,
            _imageResolution,
            maxDist
        );

        // 统计信息
        var stats = new CloudToCloudStats
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCount,
            ValidPointCount = validCount,
            InvalidPointCount = invalidCount,
            MaxDistance = maxDist,
            MeanDistance = meanDist,
            StdDev = stdDev,
            DefectCount = defectCount,
            DefectRatio = validCount > 0 ? (double)defectCount / validCount : 0,
            Threshold = _distanceThreshold,
            MaxSearchDistance = _maxSearchDistance,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("distance_mat", distances);
        context.Set("distance_image", distanceImage);
        context.Set("stats_json", statsJson);
    }

    /// <summary>
    /// 将距离值投影到 XY 平面，生成伪彩色热力图。
    /// </summary>
    private static Mat GenerateDistanceHeatmap(
        Mat pointCloud,
        Mat distances,
        int resolution,
        double maxDist
    )
    {
        int pointCount = pointCloud.Rows;

        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            if (x < minX)
                minX = x;
            if (x > maxX)
                maxX = x;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-6f)
            rangeX = 1;
        if (rangeY < 1e-6f)
            rangeY = 1;

        using Mat accumImage = Mat.Zeros(resolution, resolution, MatType.CV_64FC1);
        using Mat countImage = Mat.Zeros(resolution, resolution, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            double d = distances.Get<double>(i, 0);
            if (d < 0 || d >= double.MaxValue)
                continue;

            int px = (int)((pointCloud.Get<float>(i, 0) - minX) / rangeX * (resolution - 1));
            int py = (int)((pointCloud.Get<float>(i, 1) - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            accumImage.Set(py, px, accumImage.Get<double>(py, px) + d);
            countImage.Set(py, px, countImage.Get<int>(py, px) + 1);
        }

        using Mat normalizedMat = new Mat(resolution, resolution, MatType.CV_8UC1);
        double effectiveMax = maxDist > 0 ? maxDist : 1.0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int count = countImage.Get<int>(y, x);
                if (count > 0)
                {
                    double avg = accumImage.Get<double>(y, x) / count;
                    byte val = (byte)Math.Clamp(avg / effectiveMax * 255, 0, 255);
                    normalizedMat.Set(y, x, val);
                }
            }
        }

        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(normalizedMat, colorMap, ColormapTypes.Jet);
        return colorMap;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    public class CloudToCloudStats
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public int ValidPointCount { get; set; }
        public int InvalidPointCount { get; set; }
        public double MaxDistance { get; set; }
        public double MeanDistance { get; set; }
        public double StdDev { get; set; }
        public int DefectCount { get; set; }
        public double DefectRatio { get; set; }
        public double Threshold { get; set; }
        public double MaxSearchDistance { get; set; }
    }
}
