using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudSegmentation;

/// <summary>
/// 工作流算子：区域生长分割。
/// <para>
/// 基于法向量角度和曲率约束，将点云分割为多个平滑区域。
/// 从曲率最小的未访问点开始作为种子，通过检查相邻点法向量夹角和曲率值，
/// 将满足平滑条件的点归入同一区域。适用于 3D 缺陷检测中分离不同曲面。
/// </para>
/// <para>
/// 算法流程：
/// <list type="number">
///   <item>计算/读取每个点的曲率值，按曲率升序排列</item>
///   <item>从曲率最小的未访问点开始作为种子点</item>
///   <item>对种子点搜索 K 近邻，检查法向量夹角是否小于平滑阈值</item>
///   <item>满足条件的邻域点加入当前区域；若该点曲率也小于阈值，则加入种子队列</item>
///   <item>过滤点数不在 [minSize, maxSize] 范围内的区域，其余标记为噪声（-1）</item>
///   <item>生成分割标签、可视化图像和统计信息</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输入 <c>normals</c>（Mat）— 点云法向量（N×3，CV_32FC1）</item>
///   <item>输入 <c>curvature</c>（Mat，可选）— 点云曲率（N×1，CV_64FC1），不提供则内部计算</item>
///   <item>输出 <c>segment_labels</c>（Mat）— 分割标签（N×1，CV_32SC1，-1=噪声）</item>
///   <item>输出 <c>segment_cloud</c>（PointCloudData）— 带标签的点云（x,y,z,label），4 列</item>
///   <item>输出 <c>segment_image</c>（Mat）— 分割可视化图像（CV_8UC3，伪彩色）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0007-4000-8000-000000000028")]
[Category("3D重建分割")]
[DisplayName("区域生长")]
[Description("从种子点按法向和曲率往外长成一片，分曲面分区域用。")]
public class region_growing_segmentation : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg() { ParameterName = "normals", DisplayName = "法向量" },
            new MatImg() { ParameterName = "curvature", DisplayName = "曲率" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "segment_labels", DisplayName = "分割标签" },
            new PointCloudData() { ParameterName = "segment_cloud", DisplayName = "分割点云" },
            new MatImg() { ParameterName = "segment_image", DisplayName = "分割图像" },
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
                Name = "smoothnessThreshold",
                DisplayName = "平滑角度阈值（度）",
                ParameterType = typeof(double),
                DefaultValue = "5.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "curvatureThreshold",
                DisplayName = "曲率阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minSegmentSize",
                DisplayName = "最小区域点数",
                ParameterType = typeof(int),
                DefaultValue = "30",
                ValueLimit = new[] { "10", "30", "50", "100", "200" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxSegmentSize",
                DisplayName = "最大区域点数",
                ParameterType = typeof(int),
                DefaultValue = "100000",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "neighborCount",
                DisplayName = "近邻搜索点数",
                ParameterType = typeof(int),
                DefaultValue = "30",
                ValueLimit = new[] { "10", "20", "30", "50" },
                Required = false,
                ControlType = PortControlType.Select,
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

    private readonly double _smoothnessThreshold;
    private readonly double _curvatureThreshold;
    private readonly int _minSegmentSize;
    private readonly int _maxSegmentSize;
    private readonly int _neighborCount;
    private readonly int _imageResolution;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化区域生长分割算子。
    /// </summary>
    /// <param name="smoothnessThreshold">平滑角度阈值（度），两相邻点法向量夹角小于此值视为平滑。</param>
    /// <param name="curvatureThreshold">曲率阈值，曲率小于此值的点可作为种子点。</param>
    /// <param name="minSegmentSize">最小区域点数，小于此值的区域将被丢弃。</param>
    /// <param name="maxSegmentSize">最大区域点数，大于此值的区域将被丢弃。</param>
    /// <param name="neighborCount">K 近邻搜索点数。</param>
    /// <param name="imageResolution">可视化图像分辨率（边长像素数）。</param>
    public region_growing_segmentation(
        double smoothnessThreshold = 5.0,
        double curvatureThreshold = 0.05,
        int minSegmentSize = 30,
        int maxSegmentSize = 100000,
        int neighborCount = 30,
        int imageResolution = 512
    )
    {
        _smoothnessThreshold = smoothnessThreshold;
        _curvatureThreshold = curvatureThreshold;
        _minSegmentSize = minSegmentSize;
        _maxSegmentSize = maxSegmentSize;
        _neighborCount = neighborCount;
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
            throw new InvalidOperationException("输入点云为空，无法执行区域生长分割。");

        Mat normals =
            context.Get<Mat>("normals")
            ?? throw new InvalidOperationException(
                "上下文变量 'normals' 为空，请确认已连接法向量计算算子。"
            );

        if (normals.Empty() || normals.Cols < 3)
            throw new InvalidOperationException("法向量格式无效，期望 N×3 的 CV_32FC1 矩阵。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数不足，无法执行分割。");

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

        // 提取法向量
        float[] nxs = new float[pointCount];
        float[] nys = new float[pointCount];
        float[] nzs = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            nxs[i] = normals.Get<float>(i, 0);
            nys[i] = normals.Get<float>(i, 1);
            nzs[i] = normals.Get<float>(i, 2);

            // 归一化法向量
            float len = MathF.Sqrt(nxs[i] * nxs[i] + nys[i] * nys[i] + nzs[i] * nzs[i]);
            if (len > 1e-10f)
            {
                nxs[i] /= len;
                nys[i] /= len;
                nzs[i] /= len;
            }
        }

        // 读取或计算曲率
        double[] curvatures;
        Mat curvatureMat = context.Get<Mat>("curvature");
        if (curvatureMat != null && !curvatureMat.Empty() && curvatureMat.Rows == pointCount)
        {
            curvatures = new double[pointCount];
            for (int i = 0; i < pointCount; i++)
                curvatures[i] = curvatureMat.Get<double>(i, 0);
        }
        else
        {
            // 内部计算曲率（基于 PCA 局部邻域协方差矩阵特征值比）
            curvatures = ComputeCurvature(xs, ys, zs, pointCount, _neighborCount);
        }

        // 将平滑角度阈值转为弧度制余弦值
        double smoothRad = _smoothnessThreshold * Math.PI / 180.0;
        double cosThreshold = Math.Cos(smoothRad);

        // 按曲率升序排列点索引
        int[] sortedIndices = Enumerable.Range(0, pointCount).ToArray();
        Array.Sort(sortedIndices, (a, b) => curvatures[a].CompareTo(curvatures[b]));

        // 分割标签：-1 = 未访问/噪声，>=0 = 区域 ID
        int[] labels = new int[pointCount];
        for (int i = 0; i < pointCount; i++)
            labels[i] = -1;

        List<List<int>> segments = new List<List<int>>();
        int[] segmentIds = new int[pointCount];
        for (int i = 0; i < pointCount; i++)
            segmentIds[i] = -1;

        // 构建空间哈希网格用于加速近邻搜索
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, 0.05);

        // 区域生长主循环
        for (int si = 0; si < pointCount; si++)
        {
            int seedIdx = sortedIndices[si];

            // 跳过已访问的点
            if (labels[seedIdx] != -1)
                continue;

            // 曲率过大的点不能作为种子
            if (curvatures[seedIdx] > _curvatureThreshold)
                continue;

            // 以当前点为种子，启动区域生长
            List<int> currentSegment = new List<int>();
            Queue<int> seedQueue = new Queue<int>();
            seedQueue.Enqueue(seedIdx);
            labels[seedIdx] = -2; // 临时标记：已入队

            while (seedQueue.Count > 0)
            {
                int current = seedQueue.Dequeue();
                currentSegment.Add(current);

                // 搜索当前点的 K 近邻
                List<int> neighbors = FindKNearestNeighbors(
                    xs,
                    ys,
                    zs,
                    current,
                    pointCount,
                    _neighborCount,
                    grid
                );

                foreach (int neighbor in neighbors)
                {
                    if (labels[neighbor] != -1)
                        continue;

                    // 检查法向量角度
                    float dot =
                        nxs[current] * nxs[neighbor]
                        + nys[current] * nys[neighbor]
                        + nzs[current] * nzs[neighbor];
                    dot = Math.Clamp(dot, -1f, 1f);

                    if (dot >= cosThreshold)
                    {
                        labels[neighbor] = -2; // 已入队
                        currentSegment.Add(neighbor);

                        // 曲率小的点加入种子队列继续生长
                        if (curvatures[neighbor] <= _curvatureThreshold)
                            seedQueue.Enqueue(neighbor);
                    }
                }
            }

            // 过滤区域大小
            if (currentSegment.Count >= _minSegmentSize && currentSegment.Count <= _maxSegmentSize)
            {
                int segmentId = segments.Count;
                foreach (int idx in currentSegment)
                {
                    labels[idx] = segmentId;
                    segmentIds[idx] = segmentId;
                }
                segments.Add(currentSegment);
            }
            else
            {
                // 不满足大小要求的，标记为噪声
                foreach (int idx in currentSegment)
                    labels[idx] = -1;
            }
        }

        int validSegmentCount = segments.Count;

        // 构建输出标签矩阵
        Mat labelsMat = new Mat(pointCount, 1, MatType.CV_32SC1);
        for (int i = 0; i < pointCount; i++)
            labelsMat.Set(i, 0, labels[i]);

        // 构建带标签的点云（x, y, z, label）
        Mat segmentCloud = new Mat(pointCount, 4, MatType.CV_32FC1);
        for (int i = 0; i < pointCount; i++)
        {
            segmentCloud.Set(i, 0, xs[i]);
            segmentCloud.Set(i, 1, ys[i]);
            segmentCloud.Set(i, 2, zs[i]);
            segmentCloud.Set(i, 3, (float)labels[i]);
        }

        var outputCloud = new PointCloudData();
        outputCloud.Value = segmentCloud;

        // 生成分割可视化图像
        Mat segmentImage = GenerateSegmentImage(
            xs,
            ys,
            labels,
            pointCount,
            validSegmentCount,
            _imageResolution
        );

        // 统计信息
        var segmentStats = new List<SegmentInfo>();
        for (int s = 0; s < validSegmentCount; s++)
        {
            var pts = segments[s];
            double cx = 0,
                cy = 0,
                cz = 0;
            double minX = double.MaxValue,
                maxX = double.MinValue;
            double minY = double.MaxValue,
                maxY = double.MinValue;
            double minZ = double.MaxValue,
                maxZ = double.MinValue;
            double meanCurv = 0;

            foreach (int idx in pts)
            {
                float x = xs[idx],
                    y = ys[idx],
                    z = zs[idx];
                cx += x;
                cy += y;
                cz += z;
                meanCurv += curvatures[idx];
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

            int n = pts.Count;
            cx /= n;
            cy /= n;
            cz /= n;
            meanCurv /= n;

            segmentStats.Add(
                new SegmentInfo
                {
                    SegmentId = s,
                    PointCount = n,
                    CentroidX = cx,
                    CentroidY = cy,
                    CentroidZ = cz,
                    MeanCurvature = meanCurv,
                    BoundingBox = new SegmentBoundingBox
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

        int noiseCount = pointCount - segments.Sum(s => s.Count);

        var stats = new SegmentStats
        {
            TotalPoints = pointCount,
            SegmentCount = validSegmentCount,
            NoiseCount = noiseCount,
            NoiseRatio = pointCount > 0 ? (double)noiseCount / pointCount : 0,
            SmoothnessThresholdDeg = _smoothnessThreshold,
            CurvatureThreshold = _curvatureThreshold,
            MinSegmentSize = _minSegmentSize,
            MaxSegmentSize = _maxSegmentSize,
            Segments = segmentStats,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("segment_labels", labelsMat);
        context.Set("segment_cloud", outputCloud);
        context.Set("segment_image", segmentImage);
        context.Set("stats_json", statsJson);
    }

    // ===== 曲率计算 =====

    /// <summary>
    /// 基于 PCA 局部邻域协方差矩阵特征值比计算曲率。
    /// 曲率 = λ₃ / (λ₁ + λ₂ + λ₃)，其中 λ₁ ≥ λ₂ ≥ λ₃。
    /// </summary>
    private static double[] ComputeCurvature(
        float[] xs,
        float[] ys,
        float[] zs,
        int pointCount,
        int knnCount
    )
    {
        double[] curvatures = new double[pointCount];
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, 0.05);

        for (int i = 0; i < pointCount; i++)
        {
            List<int> neighbors = FindKNearestNeighbors(xs, ys, zs, i, pointCount, knnCount, grid);
            int n = neighbors.Count;
            if (n < 3)
            {
                curvatures[i] = 0;
                continue;
            }

            // 计算邻域质心
            double cx = 0,
                cy = 0,
                cz = 0;
            foreach (int ni in neighbors)
            {
                cx += xs[ni];
                cy += ys[ni];
                cz += zs[ni];
            }
            cx /= n;
            cy /= n;
            cz /= n;

            // 构建 3×3 协方差矩阵
            double s00 = 0,
                s01 = 0,
                s02 = 0;
            double s11 = 0,
                s12 = 0;
            double s22 = 0;

            foreach (int ni in neighbors)
            {
                double dx = xs[ni] - cx;
                double dy = ys[ni] - cy;
                double dz = zs[ni] - cz;
                s00 += dx * dx;
                s01 += dx * dy;
                s02 += dx * dz;
                s11 += dy * dy;
                s12 += dy * dz;
                s22 += dz * dz;
            }

            // Jacobi 特征值分解
            var (eigenvalues, _) = JacobiEigenDecomposition(s00, s01, s02, s11, s12, s22);
            double sum = eigenvalues[0] + eigenvalues[1] + eigenvalues[2];
            curvatures[i] = sum > 1e-12 ? eigenvalues[2] / sum : 0;
        }

        return curvatures;
    }

    /// <summary>
    /// 3×3 对称矩阵 Jacobi 特征值分解，返回特征值（降序）和特征向量。
    /// </summary>
    private static (double[] eigenvalues, double[,] eigenvectors) JacobiEigenDecomposition(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22
    )
    {
        double[] ev = { s00, s11, s22 };
        double[,] V =
        {
            { 1, 0, 0 },
            { 0, 1, 0 },
            { 0, 0, 1 },
        };
        double[] off = { s01, s02, s12 };

        const int maxIter = 50;
        for (int iter = 0; iter < maxIter; iter++)
        {
            // 找到绝对值最大的非对角元素
            int p = 0,
                q = 1;
            double maxOff = Math.Abs(off[0]);
            if (Math.Abs(off[1]) > maxOff)
            {
                maxOff = Math.Abs(off[1]);
                p = 0;
                q = 2;
            }
            if (Math.Abs(off[2]) > maxOff)
            {
                maxOff = Math.Abs(off[2]);
                p = 1;
                q = 2;
            }

            if (maxOff < 1e-12)
                break;

            double theta = (ev[q] - ev[p]) / (2 * off[p == 0 ? (q == 1 ? 0 : 1) : 2]);
            double t =
                theta >= 0
                    ? 1.0 / (theta + Math.Sqrt(theta * theta + 1))
                    : 1.0 / (theta - Math.Sqrt(theta * theta + 1));
            double c = 1.0 / Math.Sqrt(t * t + 1);
            double s = t * c;

            double tau = s / (1 + c);
            double app = ev[p];
            double aqq = ev[q];
            int offIdx = p == 0 ? (q == 1 ? 0 : 1) : 2;
            double apq = off[offIdx];

            ev[p] = app - t * apq;
            ev[q] = aqq + t * apq;

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;
                int rpIdx = Math.Min(r, p) == 0 ? (Math.Max(r, p) == 1 ? 0 : 1) : 2;
                int rqIdx = Math.Min(r, q) == 0 ? (Math.Max(r, q) == 1 ? 0 : 1) : 2;
                double arp = off[rpIdx];
                double arq = off[rqIdx];
                off[rpIdx] = arp - s * (arq + tau * arp);
                off[rqIdx] = arq + s * (arp - tau * arq);
            }

            off[offIdx] = 0;

            // 更新特征向量
            for (int r = 0; r < 3; r++)
            {
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = c * vrp - s * vrq;
                V[r, q] = s * vrp + c * vrq;
            }
        }

        // 按降序排列特征值
        double[] sortedEigenvalues = { ev[0], ev[1], ev[2] };
        Array.Sort(sortedEigenvalues);
        Array.Reverse(sortedEigenvalues);

        return (sortedEigenvalues, V);
    }

    // ===== K 近邻搜索 =====

    /// <summary>
    /// 基于空间哈希网格的 K 近邻搜索。
    /// 从查询点所在网格及相邻网格中收集候选点，计算精确距离后取最近的 K 个。
    /// </summary>
    private static List<int> FindKNearestNeighbors(
        float[] xs,
        float[] ys,
        float[] zs,
        int queryIdx,
        int pointCount,
        int k,
        SpatialGrid grid
    )
    {
        float qx = xs[queryIdx];
        float qy = ys[queryIdx];
        float qz = zs[queryIdx];

        List<int> candidates = grid.GetNeighborCandidates(qx, qy, qz);

        if (candidates.Count <= k)
            return candidates.Where(c => c != queryIdx).ToList();

        // 计算距离并排序，取最近的 K 个
        var distList = new List<(int idx, float distSq)>(candidates.Count);
        foreach (int c in candidates)
        {
            if (c == queryIdx)
                continue;
            float dx = xs[c] - qx;
            float dy = ys[c] - qy;
            float dz = zs[c] - qz;
            distList.Add((c, dx * dx + dy * dy + dz * dz));
        }

        distList.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        return distList.Take(k).Select(d => d.idx).ToList();
    }

    // ===== 分割可视化 =====

    /// <summary>
    /// 将分割标签投影到 XY 平面，生成伪彩色可视化图像。
    /// </summary>
    private static Mat GenerateSegmentImage(
        float[] xs,
        float[] ys,
        int[] labels,
        int pointCount,
        int segmentCount,
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
            if (labels[i] < 0)
                continue;
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

        // 为每个区域分配颜色（HSV 色相均匀分布）
        int colorCount = Math.Max(segmentCount, 1);
        byte[][] segmentColors = new byte[colorCount + 1][];
        for (int s = 0; s < colorCount; s++)
        {
            double hue = (double)s / colorCount * 360.0;
            segmentColors[s] = HsvToBgr(hue, 1.0, 1.0);
        }
        segmentColors[colorCount] = new byte[] { 64, 64, 64 }; // 噪声颜色

        // 创建 BGR 图像
        Mat image = new Mat(resolution, resolution, MatType.CV_8UC3, new Scalar(0, 0, 0));

        // 记录每个像素的标签投票
        int[,] pixelVotes = new int[resolution, resolution];
        int[,] pixelBestLabel = new int[resolution, resolution];

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((xs[i] - minX) / rangeX * (resolution - 1));
            int py = (int)((ys[i] - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            pixelVotes[py, px]++;
            pixelBestLabel[py, px] = labels[i] >= 0 ? labels[i] : colorCount;
        }

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                if (pixelVotes[y, x] > 0)
                {
                    int label = pixelBestLabel[y, x];
                    byte[] color = segmentColors[Math.Min(label, colorCount)];
                    image.Set(y, x, new Vec3b(color[0], color[1], color[2]));
                }
            }
        }

        return image;
    }

    /// <summary>
    /// HSV 转 BGR（用于生成分割区域颜色）。
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

    // ── 空间哈希网格 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 3D 空间哈希网格，用于加速邻域搜索。
    /// 体素边长由构造参数指定，查询时搜索相邻 27 个体素。
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

    // ── 输出 DTO ────────────────────────────────────────────────────────────────

    /// <summary>分割统计信息。</summary>
    public class SegmentStats
    {
        public int TotalPoints { get; set; }
        public int SegmentCount { get; set; }
        public int NoiseCount { get; set; }
        public double NoiseRatio { get; set; }
        public double SmoothnessThresholdDeg { get; set; }
        public double CurvatureThreshold { get; set; }
        public int MinSegmentSize { get; set; }
        public int MaxSegmentSize { get; set; }
        public List<SegmentInfo> Segments { get; set; } = new();
    }

    /// <summary>单个分割区域信息。</summary>
    public class SegmentInfo
    {
        public int SegmentId { get; set; }
        public int PointCount { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public double CentroidZ { get; set; }
        public double MeanCurvature { get; set; }
        public SegmentBoundingBox BoundingBox { get; set; } = new();
    }

    /// <summary>3D 包围盒。</summary>
    public class SegmentBoundingBox
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
    }
}
