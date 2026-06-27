using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudClustering;

/// <summary>
/// 工作流算子：欧式聚类提取。
/// <para>
/// 基于欧氏距离将点云分割为多个聚类（连通区域）。使用空间网格加速邻域搜索，
/// 通过 BFS 区域生长将距离小于阈值的点归入同一聚类，过滤掉点数过少或过多的聚类。
/// 广泛应用于 3D 缺陷检测——将检测出的异常点分组为独立的缺陷区域。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>构建空间哈希网格（体素边长 = 聚类距离阈值），加速邻域搜索</item>
///   <item>遍历未访问点，以该点为种子启动 BFS 区域生长</item>
///   <item>在 BFS 中，搜索当前点所在网格及相邻 26 个网格内的所有未访问点</item>
///   <item>距离小于阈值的邻域点加入队列，归入同一聚类</item>
///   <item>过滤点数不在 [minSize, maxSize] 范围内的聚类，其余标记为噪声（-1）</item>
///   <item>生成聚类标签、可视化图像和统计信息</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>cluster_labels</c>（Mat）— 聚类标签（N×1，CV_32SC1，-1=噪声）</item>
///   <item>输出 <c>cluster_cloud</c>（PointCloudData）— 带标签的点云（x,y,z,label），4 列</item>
///   <item>输出 <c>cluster_image</c>（Mat）— 聚类可视化图像（CV_8UC3，伪彩色）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0006-4000-8000-000000000013")]
[Category("3D重建分割")]
[DisplayName("欧式聚类")]
[Description("按距离把点云分成一堆堆，分拣多个目标用。")]
public class euclidean_cluster_extraction : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "cluster_labels", DisplayName = "聚类标签" },
            new PointCloudData() { ParameterName = "cluster_cloud", DisplayName = "聚类点云" },
            new MatImg() { ParameterName = "cluster_image", DisplayName = "聚类图像" },
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
                Name = "clusterTolerance",
                DisplayName = "聚类距离阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.02",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minClusterSize",
                DisplayName = "最小聚类点数",
                ParameterType = typeof(int),
                DefaultValue = "10",
                ValueLimit = new[] { "5", "10", "20", "50", "100" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxClusterSize",
                DisplayName = "最大聚类点数",
                ParameterType = typeof(int),
                DefaultValue = "100000",
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

    private readonly double _clusterTolerance;
    private readonly int _minClusterSize;
    private readonly int _maxClusterSize;
    private readonly int _imageResolution;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化欧式聚类提取算子。
    /// </summary>
    /// <param name="clusterTolerance">聚类距离阈值，两点距离小于此值视为同一聚类。</param>
    /// <param name="minClusterSize">最小聚类点数，小于此值的聚类将被丢弃。</param>
    /// <param name="maxClusterSize">最大聚类点数，大于此值的聚类将被丢弃。</param>
    /// <param name="imageResolution">聚类可视化图像分辨率（边长像素数）。</param>
    public euclidean_cluster_extraction(
        double clusterTolerance = 0.02,
        int minClusterSize = 10,
        int maxClusterSize = 100000,
        int imageResolution = 512
    )
    {
        _clusterTolerance = clusterTolerance;
        _minClusterSize = minClusterSize;
        _maxClusterSize = maxClusterSize;
        _imageResolution = imageResolution;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行聚类。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 2)
            throw new InvalidOperationException("点云点数不足，无法执行聚类。");

        // 提取 XYZ 坐标
        float[] xs = new float[pointCount];
        float[] ys = new float[pointCount];
        float[] zs = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
        }

        // 构建空间哈希网格
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, _clusterTolerance);

        // 聚类标签：-1 = 噪声/未访问，>=0 = 聚类 ID
        int[] labels = new int[pointCount];
        for (int i = 0; i < pointCount; i++)
            labels[i] = -1;

        List<List<int>> clusters = new List<List<int>>();
        double toleranceSq = _clusterTolerance * _clusterTolerance;

        // BFS 区域生长
        for (int i = 0; i < pointCount; i++)
        {
            if (labels[i] != -1)
                continue;

            // 以当前点为种子，启动 BFS
            List<int> cluster = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(i);
            labels[i] = -2; // 临时标记：已入队

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                cluster.Add(current);

                // 搜索当前点所在网格及相邻网格内的邻域点
                List<int> candidates = grid.GetNeighborCandidates(
                    xs[current],
                    ys[current],
                    zs[current]
                );

                foreach (int neighbor in candidates)
                {
                    if (labels[neighbor] != -1)
                        continue;

                    // 计算精确距离
                    float dx = xs[neighbor] - xs[current];
                    float dy = ys[neighbor] - ys[current];
                    float dz = zs[neighbor] - zs[current];
                    double distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq <= toleranceSq)
                    {
                        labels[neighbor] = -2; // 已入队
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // 过滤聚类大小
            if (cluster.Count >= _minClusterSize && cluster.Count <= _maxClusterSize)
            {
                int clusterId = clusters.Count;
                foreach (int idx in cluster)
                    labels[idx] = clusterId;
                clusters.Add(cluster);
            }
            else
            {
                // 不满足大小要求的，标记为噪声
                foreach (int idx in cluster)
                    labels[idx] = -1;
            }
        }

        int validClusterCount = clusters.Count;

        // 构建输出标签矩阵
        Mat labelsMat = new Mat(pointCount, 1, MatType.CV_32SC1);
        for (int i = 0; i < pointCount; i++)
            labelsMat.Set(i, 0, labels[i]);

        // 构建带标签的点云（x, y, z, label）
        Mat clusterCloud = new Mat(pointCount, 4, MatType.CV_32FC1);
        for (int i = 0; i < pointCount; i++)
        {
            clusterCloud.Set(i, 0, xs[i]);
            clusterCloud.Set(i, 1, ys[i]);
            clusterCloud.Set(i, 2, zs[i]);
            clusterCloud.Set(i, 3, (float)labels[i]);
        }

        var outputCloud = new PointCloudData();
        outputCloud.Value = clusterCloud;

        // 生成聚类可视化图像
        Mat clusterImage = GenerateClusterImage(
            xs,
            ys,
            labels,
            pointCount,
            validClusterCount,
            _imageResolution
        );

        // 统计信息
        var clusterStats = new List<ClusterInfo>();
        for (int c = 0; c < validClusterCount; c++)
        {
            var points = clusters[c];
            double cx = 0,
                cy = 0,
                cz = 0;
            double minX = double.MaxValue,
                maxX = double.MinValue;
            double minY = double.MaxValue,
                maxY = double.MinValue;
            double minZ = double.MaxValue,
                maxZ = double.MinValue;

            foreach (int idx in points)
            {
                float x = xs[idx],
                    y = ys[idx],
                    z = zs[idx];
                cx += x;
                cy += y;
                cz += z;
                if (x < minX)
                    minX = x;
                if (x > maxX)
                    maxX = x;
                if (y < minY)
                    minY = y;
                if (y > maxY)
                    maxY = y;
                if (z < minZ)
                    minZ = z;
                if (z > maxZ)
                    maxZ = z;
            }

            int n = points.Count;
            cx /= n;
            cy /= n;
            cz /= n;

            clusterStats.Add(
                new ClusterInfo
                {
                    ClusterId = c,
                    PointCount = n,
                    CentroidX = cx,
                    CentroidY = cy,
                    CentroidZ = cz,
                    BoundingBox = new BoundingBox
                    {
                        MinX = minX,
                        MaxX = maxX,
                        MinY = minY,
                        MaxY = maxY,
                        MinZ = minZ,
                        MaxZ = maxZ,
                    },
                }
            );
        }

        int noiseCount = pointCount - clusters.Sum(c => c.Count);

        var stats = new ClusterStats
        {
            TotalPoints = pointCount,
            ClusterCount = validClusterCount,
            NoiseCount = noiseCount,
            NoiseRatio = pointCount > 0 ? (double)noiseCount / pointCount : 0,
            ClusterTolerance = _clusterTolerance,
            MinClusterSize = _minClusterSize,
            MaxClusterSize = _maxClusterSize,
            Clusters = clusterStats,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("cluster_labels", labelsMat);
        context.Set("cluster_cloud", outputCloud);
        context.Set("cluster_image", clusterImage);
        context.Set("stats_json", statsJson);
    }

    // ===== 聚类可视化 =====

    /// <summary>
    /// 将聚类标签投影到 XY 平面，生成伪彩色可视化图像。
    /// </summary>
    private static Mat GenerateClusterImage(
        float[] xs,
        float[] ys,
        int[] labels,
        int pointCount,
        int clusterCount,
        int resolution
    )
    {
        // 计算 XY 范围
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            if (xs[i] < minX)
                minX = xs[i];
            if (xs[i] > maxX)
                maxX = xs[i];
            if (ys[i] < minY)
                minY = ys[i];
            if (ys[i] > maxY)
                maxY = ys[i];
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-6f)
            rangeX = 1;
        if (rangeY < 1e-6f)
            rangeY = 1;

        // 为每个聚类分配颜色（HSV 色相均匀分布）
        byte[][] clusterColors = new byte[clusterCount + 1][];
        for (int c = 0; c < clusterCount; c++)
        {
            double hue = (double)c / clusterCount * 360.0;
            clusterColors[c] = HsvToBgr(hue, 1.0, 1.0);
        }
        clusterColors[clusterCount] = new byte[] { 64, 64, 64 }; // 噪声颜色

        // 创建 BGR 图像
        Mat image = new Mat(resolution, resolution, MatType.CV_8UC3, new Scalar(0, 0, 0));

        // 记录每个像素的聚类投票
        int[,] pixelVotes = new int[resolution, resolution];
        int[,] pixelBestLabel = new int[resolution, resolution];

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((xs[i] - minX) / rangeX * (resolution - 1));
            int py = (int)((ys[i] - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            pixelVotes[py, px]++;
            pixelBestLabel[py, px] = labels[i] >= 0 ? labels[i] : clusterCount;
        }

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                if (pixelVotes[y, x] > 0)
                {
                    int label = pixelBestLabel[y, x];
                    byte[] color = clusterColors[Math.Min(label, clusterCount)];
                    image.Set(y, x, new Vec3b(color[0], color[1], color[2]));
                }
            }
        }

        return image;
    }

    /// <summary>
    /// HSV 转 BGR（用于生成聚类颜色）。
    /// </summary>
    private static byte[] HsvToBgr(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double r,
            g,
            b;
        if (h < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return new byte[] { (byte)((b + m) * 255), (byte)((g + m) * 255), (byte)((r + m) * 255) };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 空间哈希网格 ────────────────────────────────────────────────────────

    /// <summary>
    /// 3D 空间哈希网格，用于加速固定半径内的邻域搜索。
    /// 体素边长 = 聚类距离阈值，查询时只需搜索相邻 27 个体素。
    /// </summary>
    private class SpatialGrid
    {
        private readonly float _invCellSize;
        private readonly float _minX,
            _minY,
            _minZ;
        private readonly int _gridSizeX,
            _gridSizeY,
            _gridSizeZ;
        private readonly Dictionary<int, List<int>> _cells;

        /// <summary>
        /// 构建空间哈希网格。
        /// </summary>
        public SpatialGrid(float[] xs, float[] ys, float[] zs, int pointCount, double cellSize)
        {
            _invCellSize = 1.0f / (float)cellSize;

            // 计算包围盒
            _minX = xs.Min();
            _minY = ys.Min();
            _minZ = zs.Min();
            float maxX = xs.Max();
            float maxY = ys.Max();
            float maxZ = zs.Max();

            _gridSizeX = Math.Max(1, (int)((maxX - _minX) * _invCellSize) + 1);
            _gridSizeY = Math.Max(1, (int)((maxY - _minY) * _invCellSize) + 1);
            _gridSizeZ = Math.Max(1, (int)((maxZ - _minZ) * _invCellSize) + 1);

            _cells = new Dictionary<int, List<int>>();

            // 将每个点分配到对应体素
            for (int i = 0; i < pointCount; i++)
            {
                int key = GetCellKey(xs[i], ys[i], zs[i]);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _cells[key] = list;
                }
                list.Add(i);
            }
        }

        /// <summary>
        /// 获取查询点所在体素及相邻 26 个体素内的所有候选点索引。
        /// </summary>
        public List<int> GetNeighborCandidates(float x, float y, float z)
        {
            int cx = (int)((x - _minX) * _invCellSize);
            int cy = (int)((y - _minY) * _invCellSize);
            int cz = (int)((z - _minZ) * _invCellSize);

            List<int> candidates = new List<int>();

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        int nz = cz + dz;

                        if (
                            nx < 0
                            || nx >= _gridSizeX
                            || ny < 0
                            || ny >= _gridSizeY
                            || nz < 0
                            || nz >= _gridSizeZ
                        )
                            continue;

                        int key = nz * _gridSizeY * _gridSizeX + ny * _gridSizeX + nx;
                        if (_cells.TryGetValue(key, out var cell))
                            candidates.AddRange(cell);
                    }
                }
            }

            return candidates;
        }

        private int GetCellKey(float x, float y, float z)
        {
            int cx = (int)((x - _minX) * _invCellSize);
            int cy = (int)((y - _minY) * _invCellSize);
            int cz = (int)((z - _minZ) * _invCellSize);

            cx = Math.Clamp(cx, 0, _gridSizeX - 1);
            cy = Math.Clamp(cy, 0, _gridSizeY - 1);
            cz = Math.Clamp(cz, 0, _gridSizeZ - 1);

            return cz * _gridSizeY * _gridSizeX + cy * _gridSizeX + cx;
        }
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>聚类统计信息。</summary>
    public class ClusterStats
    {
        public int TotalPoints { get; set; }
        public int ClusterCount { get; set; }
        public int NoiseCount { get; set; }
        public double NoiseRatio { get; set; }
        public double ClusterTolerance { get; set; }
        public int MinClusterSize { get; set; }
        public int MaxClusterSize { get; set; }
        public List<ClusterInfo> Clusters { get; set; } = new();
    }

    /// <summary>单个聚类信息。</summary>
    public class ClusterInfo
    {
        public int ClusterId { get; set; }
        public int PointCount { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public double CentroidZ { get; set; }
        public BoundingBox BoundingBox { get; set; } = new();
    }

    /// <summary>3D 包围盒。</summary>
    public class BoundingBox
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
    }
}
