using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 工作流算子：基于特征的点云粗配准（简化 FPFH + SAC-IA）。
/// <para>
/// 为源/目标点云估计法向量后，计算每点的简化 FPFH 风格直方图特征（Darboux 框架下的三个角度特征分桶），
/// 通过特征最近邻建立对应关系，再用 RANSAC（采样一致初始配准 SAC-IA 思想）鲁棒求解刚体变换。
/// 该算子用于在两片点云初始位姿差异较大时提供<b>粗配准</b>，通常作为精配准（ICP/点到面 ICP）的前置步骤。
/// </para>
/// <para>
/// 为控制计算量，特征匹配在两片点云的均匀子采样子集上进行（由 <c>sampleCount</c> 控制）。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>source_cloud</c>（PointCloudData）— 待配准源点云</item>
///   <item>输入 <c>target_cloud</c>（PointCloudData）— 目标点云（参考）</item>
///   <item>输出 <c>aligned_cloud</c>（PointCloudData）— 粗配准后的源点云</item>
///   <item>输出 <c>transform_matrix</c>（Mat）— 4×4 累积变换矩阵（CV_64FC1）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10008-0008-4000-8000-000000000038")]
[Category("3D配准")]
[DisplayName("特征粗配准")]
[Description("两片云差得远时先用特征大致对上，再交给 ICP 精修。")]
public class feature_coarse_registration : IOperator
{
    private const int HistogramBins = 11;
    private const int DescriptorLength = HistogramBins * 3;

    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "source_cloud", DisplayName = "源点云" },
            new PointCloudData() { ParameterName = "target_cloud", DisplayName = "目标点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "aligned_cloud", DisplayName = "配准点云" },
            new MatImg() { ParameterName = "transform_matrix", DisplayName = "变换矩阵" },
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
                Name = "normalK",
                DisplayName = "法向估计K近邻",
                ParameterType = typeof(int),
                DefaultValue = "20",
                ValueLimit = new[] { "10", "20", "30", "50" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "featureRadius",
                DisplayName = "特征邻域半径",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxIterations",
                DisplayName = "RANSAC最大迭代",
                ParameterType = typeof(int),
                DefaultValue = "1000",
                ValueLimit = new[] { "500", "1000", "2000", "5000" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "sampleCount",
                DisplayName = "特征采样点数",
                ParameterType = typeof(int),
                DefaultValue = "300",
                ValueLimit = new[] { "100", "200", "300", "500" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "内点距离阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _normalK;
    private readonly double _featureRadius;
    private readonly int _maxIterations;
    private readonly int _sampleCount;
    private readonly double _distanceThreshold;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public feature_coarse_registration(
        int normalK = 20,
        double featureRadius = 0.05,
        int maxIterations = 1000,
        int sampleCount = 300,
        double distanceThreshold = 0.05
    )
    {
        if (normalK < 3)
            throw new ArgumentOutOfRangeException(nameof(normalK), "法向估计近邻数不能小于 3。");
        if (!double.IsFinite(featureRadius) || featureRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(featureRadius), "特征邻域半径必须为正数。");
        if (maxIterations < 1 || maxIterations > 100_000)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "RANSAC 迭代次数必须在 1–100000 之间。");
        if (sampleCount < 3 || sampleCount > 2_000)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "特征采样点数必须在 3–2000 之间。");
        if (!double.IsFinite(distanceThreshold) || distanceThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(distanceThreshold), "内点距离阈值必须为正数。");

        _normalK = normalK;
        _featureRadius = featureRadius;
        _maxIterations = maxIterations;
        _sampleCount = sampleCount;
        _distanceThreshold = distanceThreshold;
    }

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
            throw new InvalidOperationException("源点云为空，无法执行特征粗配准。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法执行特征粗配准。");

        // 法向估计当前采用暴力 KNN。若直接在完整扫描点云上执行会产生 O(N²)
        // 复杂度并触发工作流节点超时。粗配准只需要稀疏特征，因此先构造有上限的
        // 工作云；最终变换仍应用到完整源点云，不降低输出点云分辨率。
        int workingPointLimit = Math.Max(500, Math.Min(1_000, _sampleCount * 2));
        using Mat sourceWorkingCloud = UniformDownsampleCloud(sourceCloud, workingPointLimit);
        using Mat targetWorkingCloud = UniformDownsampleCloud(targetCloud, workingPointLimit);

        var src = CloudArrays.From(sourceWorkingCloud, _normalK);
        var tgt = CloudArrays.From(targetWorkingCloud, _normalK);

        // 均匀子采样用于特征匹配
        int[] srcSamples = UniformSample(src.Count, _sampleCount);
        int[] tgtSamples = UniformSample(tgt.Count, _sampleCount);

        var srcGrid = new SpatialHashGrid(src.Fx, src.Fy, src.Fz, _featureRadius, src.Count);
        var tgtGrid = new SpatialHashGrid(tgt.Fx, tgt.Fy, tgt.Fz, _featureRadius, tgt.Count);

        double[][] srcFeat = ComputeFeatures(src, srcGrid, srcSamples, _featureRadius);
        double[][] tgtFeat = ComputeFeatures(tgt, tgtGrid, tgtSamples, _featureRadius);

        // 特征最近邻 → 对应点对（源采样点 → 目标采样点）
        var correspondences = new List<(int s, int t)>();
        for (int i = 0; i < srcSamples.Length; i++)
        {
            int bestJ = -1;
            double bestDist = double.MaxValue;
            for (int j = 0; j < tgtSamples.Length; j++)
            {
                double d = L2Sq(srcFeat[i], tgtFeat[j]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJ = j;
                }
            }
            if (bestJ >= 0)
                correspondences.Add((srcSamples[i], tgtSamples[bestJ]));
        }

        if (correspondences.Count < 3)
            throw new InvalidOperationException("有效特征对应点对不足 3，无法估计粗配准变换。");

        // RANSAC（SAC-IA 思想）
        Random rng = new Random(0);
        double[] bestR = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] bestT = { 0, 0, 0 };
        int bestInliers = -1;
        double threshSq = _distanceThreshold * _distanceThreshold;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            int[] pick = SampleDistinct(correspondences.Count, 3, rng);
            var sPts = new List<(double, double, double)>(3);
            var tPts = new List<(double, double, double)>(3);
            foreach (int p in pick)
            {
                var (si, ti) = correspondences[p];
                sPts.Add((src.X[si], src.Y[si], src.Z[si]));
                tPts.Add((tgt.X[ti], tgt.Y[ti], tgt.Z[ti]));
            }

            double[] R,
                t;
            try
            {
                (R, t) = Math3D.ComputeRigidTransform(sPts, tPts);
            }
            catch (ArgumentException)
            {
                continue;
            }

            int inliers = 0;
            foreach (var (si, ti) in correspondences)
            {
                double tx = R[0] * src.X[si] + R[1] * src.Y[si] + R[2] * src.Z[si] + t[0];
                double ty = R[3] * src.X[si] + R[4] * src.Y[si] + R[5] * src.Z[si] + t[1];
                double tz = R[6] * src.X[si] + R[7] * src.Y[si] + R[8] * src.Z[si] + t[2];
                double dx = tx - tgt.X[ti],
                    dy = ty - tgt.Y[ti],
                    dz = tz - tgt.Z[ti];
                if (dx * dx + dy * dy + dz * dz <= threshSq)
                    inliers++;
            }

            if (inliers > bestInliers)
            {
                bestInliers = inliers;
                bestR = R;
                bestT = t;
            }
        }

        // 应用到完整源点云
        int sourcePointCount = sourceCloud.Rows;
        Mat alignedCloud = new Mat(sourcePointCount, 3, MatType.CV_32FC1);
        for (int i = 0; i < sourcePointCount; i++)
        {
            double x = sourceCloud.Get<float>(i, 0),
                y = sourceCloud.Get<float>(i, 1),
                z = sourceCloud.Get<float>(i, 2);
            alignedCloud.Set(i, 0, (float)(bestR[0] * x + bestR[1] * y + bestR[2] * z + bestT[0]));
            alignedCloud.Set(i, 1, (float)(bestR[3] * x + bestR[4] * y + bestR[5] * z + bestT[1]));
            alignedCloud.Set(i, 2, (float)(bestR[6] * x + bestR[7] * y + bestR[8] * z + bestT[2]));
        }
        var outputCloud = new PointCloudData { Value = alignedCloud };

        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, bestR[r * 3 + c]);
            transformMatrix.Set(r, 3, bestT[r]);
        }

        var stats = new CoarseStats
        {
            SourcePointCount = sourcePointCount,
            TargetPointCount = targetCloud.Rows,
            CorrespondenceCount = correspondences.Count,
            BestInlierCount = bestInliers,
            FeatureSampleCount = srcSamples.Length,
            SourceWorkingPointCount = src.Count,
            TargetWorkingPointCount = tgt.Count,
        };

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("stats_json", JsonSerializer.Serialize(stats, JsonOptions));
    }

    // ── 特征计算 ──────────────────────────────────────────────────────────

    /// <summary>对指定采样点计算简化 FPFH 风格直方图特征。</summary>
    private static double[][] ComputeFeatures(
        CloudArrays cloud,
        SpatialHashGrid grid,
        int[] samples,
        double radius
    )
    {
        double radiusSq = radius * radius;
        double[][] features = new double[samples.Length][];

        for (int s = 0; s < samples.Length; s++)
        {
            int i = samples[s];
            double[] hist = new double[DescriptorLength];

            var candidates = grid.CollectCandidates(
                (float)cloud.X[i],
                (float)cloud.Y[i],
                (float)cloud.Z[i],
                1
            );

            int used = 0;
            foreach (int j in candidates)
            {
                if (j == i)
                    continue;
                double dx = cloud.X[j] - cloud.X[i];
                double dy = cloud.Y[j] - cloud.Y[i];
                double dz = cloud.Z[j] - cloud.Z[i];
                double dSq = dx * dx + dy * dy + dz * dz;
                if (dSq > radiusSq || dSq < 1e-18)
                    continue;

                double dist = Math.Sqrt(dSq);
                double ddx = dx / dist,
                    ddy = dy / dist,
                    ddz = dz / dist;

                // Darboux 框架：u = n_p, v = (d̂ × u) 归一化, w = u × v
                double ux = cloud.Nx[i],
                    uy = cloud.Ny[i],
                    uz = cloud.Nz[i];
                double vx = ddy * uz - ddz * uy;
                double vy = ddz * ux - ddx * uz;
                double vz = ddx * uy - ddy * ux;
                double vn = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                if (vn < 1e-9)
                    continue;
                vx /= vn;
                vy /= vn;
                vz /= vn;
                double wx = uy * vz - uz * vy;
                double wy = uz * vx - ux * vz;
                double wz = ux * vy - uy * vx;

                double nqx = cloud.Nx[j],
                    nqy = cloud.Ny[j],
                    nqz = cloud.Nz[j];

                double f1 = vx * nqx + vy * nqy + vz * nqz; // [-1,1]
                double f2 = ux * ddx + uy * ddy + uz * ddz; // [-1,1]
                double f3 = Math.Atan2(
                    wx * nqx + wy * nqy + wz * nqz,
                    ux * nqx + uy * nqy + uz * nqz
                ); // [-π,π]

                hist[Bin(f1, -1, 1)]++;
                hist[HistogramBins + Bin(f2, -1, 1)]++;
                hist[2 * HistogramBins + Bin(f3, -Math.PI, Math.PI)]++;
                used++;
            }

            // L1 归一化
            if (used > 0)
            {
                double sum = 0;
                for (int b = 0; b < DescriptorLength; b++)
                    sum += hist[b];
                if (sum > 0)
                    for (int b = 0; b < DescriptorLength; b++)
                        hist[b] /= sum;
            }

            features[s] = hist;
        }

        return features;
    }

    private static int Bin(double value, double min, double max)
    {
        double t = (value - min) / (max - min);
        int b = (int)(t * HistogramBins);
        return Math.Clamp(b, 0, HistogramBins - 1);
    }

    private static double L2Sq(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }

    private static int[] UniformSample(int n, int count)
    {
        if (count >= n)
            return Enumerable.Range(0, n).ToArray();
        int[] result = new int[count];
        // 均匀步长采样
        double step = (double)n / count;
        for (int i = 0; i < count; i++)
            result[i] = Math.Min(n - 1, (int)(i * step));
        return result;
    }

    private static Mat UniformDownsampleCloud(Mat cloud, int maxPoints)
    {
        int rows = cloud.Rows;
        int cols = cloud.Cols;
        int count = Math.Min(rows, maxPoints);
        int[] indices = UniformSample(rows, count);
        Mat result = new Mat(count, cols, MatType.CV_32FC1);
        for (int row = 0; row < indices.Length; row++)
        {
            int sourceRow = indices[row];
            for (int col = 0; col < cols; col++)
                result.Set(row, col, cloud.Get<float>(sourceRow, col));
        }
        return result;
    }

    private static int[] SampleDistinct(int n, int k, Random rng)
    {
        var set = new HashSet<int>();
        while (set.Count < k)
            set.Add(rng.Next(n));
        return set.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>点云坐标 + 法向量的展开数组表示。</summary>
    private sealed class CloudArrays
    {
        public required double[] X;
        public required double[] Y;
        public required double[] Z;
        public required float[] Fx;
        public required float[] Fy;
        public required float[] Fz;
        public required double[] Nx;
        public required double[] Ny;
        public required double[] Nz;
        public required int Count;

        public static CloudArrays From(Mat cloud, int normalK)
        {
            int n = cloud.Rows;
            var x = new double[n];
            var y = new double[n];
            var z = new double[n];
            var fx = new float[n];
            var fy = new float[n];
            var fz = new float[n];
            var nx = new double[n];
            var ny = new double[n];
            var nz = new double[n];

            for (int i = 0; i < n; i++)
            {
                float vx = cloud.Get<float>(i, 0);
                float vy = cloud.Get<float>(i, 1);
                float vz = cloud.Get<float>(i, 2);
                x[i] = vx;
                y[i] = vy;
                z[i] = vz;
                fx[i] = vx;
                fy[i] = vy;
                fz[i] = vz;
            }

            if (cloud.Cols >= 6)
            {
                for (int i = 0; i < n; i++)
                {
                    nx[i] = cloud.Get<float>(i, 3);
                    ny[i] = cloud.Get<float>(i, 4);
                    nz[i] = cloud.Get<float>(i, 5);
                }
            }
            else
            {
                using Mat normals = PointCloudUtils.EstimateNormals(cloud, normalK);
                for (int i = 0; i < n; i++)
                {
                    nx[i] = normals.Get<float>(i, 0);
                    ny[i] = normals.Get<float>(i, 1);
                    nz[i] = normals.Get<float>(i, 2);
                }
            }

            return new CloudArrays
            {
                X = x,
                Y = y,
                Z = z,
                Fx = fx,
                Fy = fy,
                Fz = fz,
                Nx = nx,
                Ny = ny,
                Nz = nz,
                Count = n,
            };
        }
    }

    /// <summary>粗配准统计信息。</summary>
    public class CoarseStats
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public int CorrespondenceCount { get; set; }
        public int BestInlierCount { get; set; }
        public int FeatureSampleCount { get; set; }
        public int SourceWorkingPointCount { get; set; }
        public int TargetWorkingPointCount { get; set; }
    }
}
