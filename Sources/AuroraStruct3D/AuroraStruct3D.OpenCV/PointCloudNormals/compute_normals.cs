using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudNormals;

/// <summary>
/// 工作流算子：点云法向量计算。
/// <para>
/// 对点云中每个点，基于其 k 近邻邻域进行 PCA（主成分分析），
/// 协方差矩阵最小特征值对应的特征向量即为该点的法向量。
/// 同时输出曲率估计值，用于后续 3D 缺陷检测和曲面分析。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>对每个点，搜索其 k 个最近邻点</item>
///   <item>计算邻域质心，构建 3×3 协方差矩阵</item>
///   <item>Jacobi 特征值分解，取最小特征值对应的特征向量作为法向量</item>
///   <item>曲率估计：λ₃ / (λ₁ + λ₂ + λ₃)，其中 λ₁ ≥ λ₂ ≥ λ₃</item>
///   <item>可选：法向量方向一致性调整（基于视点或邻域传播）</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>normal_cloud</c>（PointCloudData）— 带法向量的点云，每点 6 列 (x,y,z,nx,ny,nz)</item>
///   <item>输出 <c>curvature_mat</c>（Mat）— 每个点的曲率估计值（N×1，CV_64FC1）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0004-4000-8000-000000000011")]
[Category("3D拟合测量")]
[DisplayName("法向量计算")]
[Description("给每个点算法向量和曲率，配准、分割、检测前常先算这个。")]
public class compute_normals : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "normal_cloud", DisplayName = "法向量点云" },
            new MatImg() { ParameterName = "curvature_mat", DisplayName = "曲率矩阵" },
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
                Name = "knnCount",
                DisplayName = "K 近邻数",
                ParameterType = typeof(int),
                DefaultValue = "20",
                ValueLimit = new[] { "10", "15", "20", "30", "50" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "consistentOrientation",
                DisplayName = "法向量方向一致化",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "viewpointX",
                DisplayName = "视点 X",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "viewpointY",
                DisplayName = "视点 Y",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "viewpointZ",
                DisplayName = "视点 Z",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _knnCount;
    private readonly bool _consistentOrientation;
    private readonly double _viewpointX;
    private readonly double _viewpointY;
    private readonly double _viewpointZ;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化点云法向量计算算子。
    /// </summary>
    /// <param name="knnCount">K 近邻搜索的点数，越大越平滑但细节越少。</param>
    /// <param name="consistentOrientation">是否启用法向量方向一致性调整。</param>
    /// <param name="viewpointX">视点 X 坐标，用于法向量方向统一。</param>
    /// <param name="viewpointY">视点 Y 坐标，用于法向量方向统一。</param>
    /// <param name="viewpointZ">视点 Z 坐标，用于法向量方向统一。</param>
    public compute_normals(
        int knnCount = 20,
        bool consistentOrientation = true,
        double viewpointX = 0,
        double viewpointY = 0,
        double viewpointZ = 0
    )
    {
        _knnCount = knnCount;
        _consistentOrientation = consistentOrientation;
        _viewpointX = viewpointX;
        _viewpointY = viewpointY;
        _viewpointZ = viewpointZ;
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
            throw new InvalidOperationException("输入点云为空，无法计算法向量。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数少于 3，无法计算法向量。");

        int knn = Math.Min(_knnCount, pointCount - 1);
        if (knn < 3)
            throw new InvalidOperationException(
                $"K 近邻数 {knn} 不足，至少需要 3 个邻域点才能拟合局部平面。"
            );

        // 提取 XYZ 坐标到数组（加速随机访问）
        float[] xs = new float[pointCount];
        float[] ys = new float[pointCount];
        float[] zs = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
        }

        // 输出：法向量 (nx, ny, nz) 和曲率
        float[] normalsX = new float[pointCount];
        float[] normalsY = new float[pointCount];
        float[] normalsZ = new float[pointCount];
        double[] curvatures = new double[pointCount];

        // 对每个点计算法向量
        for (int i = 0; i < pointCount; i++)
        {
            // ① 搜索 k 近邻
            int[] neighborIndices = FindKNearestNeighbors(xs, ys, zs, i, knn, pointCount);

            // ② 计算邻域质心
            double cx = 0,
                cy = 0,
                cz = 0;
            for (int j = 0; j < knn; j++)
            {
                int idx = neighborIndices[j];
                cx += xs[idx];
                cy += ys[idx];
                cz += zs[idx];
            }
            cx /= knn;
            cy /= knn;
            cz /= knn;

            // ③ 构建协方差矩阵
            double c00 = 0,
                c01 = 0,
                c02 = 0;
            double c11 = 0,
                c12 = 0;
            double c22 = 0;

            for (int j = 0; j < knn; j++)
            {
                int idx = neighborIndices[j];
                double dx = xs[idx] - cx;
                double dy = ys[idx] - cy;
                double dz = zs[idx] - cz;

                c00 += dx * dx;
                c01 += dx * dy;
                c02 += dx * dz;
                c11 += dy * dy;
                c12 += dy * dz;
                c22 += dz * dz;
            }

            // ④ Jacobi 特征值分解（降序），最小特征值对应的特征向量即法向量
            (double[] eigenValues, double[,] eigenVectors) = Math3D.Jacobi3x3(
                c00 / knn,
                c01 / knn,
                c02 / knn,
                c11 / knn,
                c12 / knn,
                c22 / knn
            );

            // 最小特征值位于索引 2
            normalsX[i] = (float)eigenVectors[0, 2];
            normalsY[i] = (float)eigenVectors[1, 2];
            normalsZ[i] = (float)eigenVectors[2, 2];

            // ⑤ 曲率估计：λ_min / (λ₁ + λ₂ + λ₃)
            double sumEigen = eigenValues[0] + eigenValues[1] + eigenValues[2];
            curvatures[i] = sumEigen > 1e-15 ? eigenValues[2] / sumEigen : 0;
        }

        // ⑥ 法向量方向一致性调整
        if (_consistentOrientation)
        {
            MakeNormalsConsistent(normalsX, normalsY, normalsZ, xs, ys, zs, pointCount);
        }

        // ⑦ 构建输出点云（x, y, z, nx, ny, nz）
        Mat normalCloud = new Mat(pointCount, 6, MatType.CV_32FC1);
        for (int i = 0; i < pointCount; i++)
        {
            normalCloud.Set(i, 0, xs[i]);
            normalCloud.Set(i, 1, ys[i]);
            normalCloud.Set(i, 2, zs[i]);
            normalCloud.Set(i, 3, normalsX[i]);
            normalCloud.Set(i, 4, normalsY[i]);
            normalCloud.Set(i, 5, normalsZ[i]);
        }

        // 曲率矩阵
        Mat curvatureMat = new Mat(pointCount, 1, MatType.CV_64FC1);
        for (int i = 0; i < pointCount; i++)
            curvatureMat.Set(i, 0, curvatures[i]);

        // 统计信息
        double maxCurvature = curvatures.Max();
        double meanCurvature = curvatures.Average();
        double stdCurvature = Math.Sqrt(
            curvatures.Average(c => (c - meanCurvature) * (c - meanCurvature))
        );

        var stats = new NormalStats
        {
            PointCount = pointCount,
            KnnCount = knn,
            MaxCurvature = maxCurvature,
            MeanCurvature = meanCurvature,
            StdCurvature = stdCurvature,
            ConsistentOrientation = _consistentOrientation,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        var outputCloud = new PointCloudData();
        outputCloud.Value = normalCloud;

        context.Set("normal_cloud", outputCloud);
        context.Set("curvature_mat", curvatureMat);
        context.Set("stats_json", statsJson);
    }

    // ===== K 近邻搜索（暴力搜索） =====

    /// <summary>
    /// 暴力搜索 k 个最近邻，排除自身。
    /// </summary>
    private static int[] FindKNearestNeighbors(
        float[] xs,
        float[] ys,
        float[] zs,
        int queryIndex,
        int k,
        int pointCount
    )
    {
        float qx = xs[queryIndex];
        float qy = ys[queryIndex];
        float qz = zs[queryIndex];

        // 使用优先队列（最大堆）维护最近的 k 个点
        var heap = new SortedList<double, int>(new DuplicateKeyComparer());
        int maxHeapSize = k;

        for (int i = 0; i < pointCount; i++)
        {
            if (i == queryIndex)
                continue;

            float dx = xs[i] - qx;
            float dy = ys[i] - qy;
            float dz = zs[i] - qz;
            double distSq = dx * dx + dy * dy + dz * dz;

            if (heap.Count < maxHeapSize)
            {
                heap.Add(distSq, i);
            }
            else if (distSq < heap.Keys[heap.Count - 1])
            {
                heap.RemoveAt(heap.Count - 1);
                heap.Add(distSq, i);
            }
        }

        int[] result = new int[k];
        for (int i = 0; i < k && i < heap.Count; i++)
            result[i] = heap.Values[i];

        return result;
    }

    // ===== 法向量方向一致性 =====

    /// <summary>
    /// 调整法向量方向，使其指向一致的方向（基于视点或基于邻域传播）。
    /// </summary>
    private void MakeNormalsConsistent(
        float[] normalsX,
        float[] normalsY,
        float[] normalsZ,
        float[] xs,
        float[] ys,
        float[] zs,
        int pointCount
    )
    {
        // 策略：如果设置了视点，则所有法向量指向视点方向；否则基于质心方向
        bool hasViewpoint =
            Math.Abs(_viewpointX) > 1e-10
            || Math.Abs(_viewpointY) > 1e-10
            || Math.Abs(_viewpointZ) > 1e-10;

        if (hasViewpoint)
        {
            // 基于视点统一方向：法向量应指向视点（即 (视点 - 点) 与法向量点积 > 0）
            for (int i = 0; i < pointCount; i++)
            {
                double vx = _viewpointX - xs[i];
                double vy = _viewpointY - ys[i];
                double vz = _viewpointZ - zs[i];

                double dot = vx * normalsX[i] + vy * normalsY[i] + vz * normalsZ[i];
                if (dot < 0)
                {
                    normalsX[i] = -normalsX[i];
                    normalsY[i] = -normalsY[i];
                    normalsZ[i] = -normalsZ[i];
                }
            }
        }
        else
        {
            // 基于质心：所有法向量指向远离质心的方向
            double cx = 0,
                cy = 0,
                cz = 0;
            for (int i = 0; i < pointCount; i++)
            {
                cx += xs[i];
                cy += ys[i];
                cz += zs[i];
            }
            cx /= pointCount;
            cy /= pointCount;
            cz /= pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                double vx = xs[i] - cx;
                double vy = ys[i] - cy;
                double vz = zs[i] - cz;

                double dot = vx * normalsX[i] + vy * normalsY[i] + vz * normalsZ[i];
                if (dot < 0)
                {
                    normalsX[i] = -normalsX[i];
                    normalsY[i] = -normalsY[i];
                    normalsZ[i] = -normalsZ[i];
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 辅助类型 ────────────────────────────────────────────────────────────

    /// <summary>支持重复键的 SortedList 比较器。</summary>
    private class DuplicateKeyComparer : IComparer<double>
    {
        public int Compare(double x, double y)
        {
            int result = x.CompareTo(y);
            return result == 0 ? 1 : result;
        }
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>法向量计算统计信息。</summary>
    public class NormalStats
    {
        public int PointCount { get; set; }
        public int KnnCount { get; set; }
        public double MaxCurvature { get; set; }
        public double MeanCurvature { get; set; }
        public double StdCurvature { get; set; }
        public bool ConsistentOrientation { get; set; }
    }
}
