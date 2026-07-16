using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：RANSAC 平面拟合。
/// <para>
/// 使用随机抽样一致算法从点云中鲁棒地拟合平面模型，能够有效滤除离群噪声点的干扰。
/// 平面方程：ax + by + cz + d = 0，其中 a^2 + b^2 + c^2 = 1（法向量单位化）。
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>plane_params</c>（Mat）— 平面参数 [a, b, c, d]，形状 (4, 1)，CV_64FC1</item>
///   <item><c>inlier_points</c>（PointCloudData）— 被平面模型判定为内点的点云</item>
///   <item><c>outlier_points</c>（PointCloudData）— 被平面模型判定为外点的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("d3f12345-6789-0123-4567-89012345670c")]
[Category("3D拟合测量")]
[DisplayName("RANSAC拟合")]
[Description("有离群点也能稳稳拟合平面，自动把内点外点分开。")]
public class ransac_plane_fit : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "plane_params", DisplayName = "平面参数" },
            new PointCloudData() { ParameterName = "inlier_points", DisplayName = "内点点云" },
            new PointCloudData() { ParameterName = "outlier_points", DisplayName = "外点点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "距离阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxIterations",
                DisplayName = "最大迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "1000",
                ValueLimit = new[] { "100", "500", "1000", "2000", "5000" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "probability",
                DisplayName = "置信概率",
                ParameterType = typeof(double),
                DefaultValue = "0.99",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "seed",
                DisplayName = "随机种子",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "refinePlane",
                DisplayName = "内点细化",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "normalConstraint",
                DisplayName = "法向量约束",
                ParameterType = typeof(string),
                DefaultValue = "none",
                ValueLimit = new[] { "none", "z-up", "z-down", "xy-plane" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _distanceThreshold;
    private readonly int _maxIterations;
    private readonly double _probability;
    private readonly int _seed;
    private readonly bool _refinePlane;
    private readonly string _normalConstraint;
    private bool _disposed;

    public ransac_plane_fit(
        double distanceThreshold = 0.01,
        int maxIterations = 1000,
        double probability = 0.99,
        int seed = 0,
        bool refinePlane = true,
        string normalConstraint = "none"
    )
    {
        _distanceThreshold = distanceThreshold;
        _maxIterations = maxIterations;
        _probability = probability;
        _seed = seed;
        _refinePlane = refinePlane;
        _normalConstraint = normalConstraint;
    }

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
            throw new InvalidOperationException("输入点云为空，无法执行 RANSAC 平面拟合。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数少于 3，不足以拟合平面。");

        // RANSAC 迭代
        (double[] bestPlane, List<int> bestInliers) = FindBestPlane(
            pointCloud,
            pointCount,
            _distanceThreshold,
            _maxIterations,
            _probability,
            _seed,
            _refinePlane,
            _normalConstraint
        );

        // 拆分内点和外点
        (var inlierPoints, var inlierColors) = PointCloudUtils.ExtractSubset(
            input,
            bestInliers,
            pointCloud
        );
        var outlierIndices = Enumerable.Range(0, pointCount).Except(bestInliers).ToList();
        (var outlierPoints, var outlierColors) = PointCloudUtils.ExtractSubset(
            input,
            outlierIndices,
            pointCloud
        );

        // 保存平面参数
        Mat planeParams = new Mat(4, 1, MatType.CV_64FC1);
        for (int i = 0; i < 4; i++)
            planeParams.Set(i, 0, bestPlane[i]);

        // 输出结果
        var inlierOutput = new PointCloudData();
        inlierOutput.Value = inlierPoints;
        if (inlierColors != null)
            inlierOutput.SetColors(inlierColors);

        var outlierOutput = new PointCloudData();
        outlierOutput.Value = outlierPoints;
        if (outlierColors != null)
            outlierOutput.SetColors(outlierColors);

        context.Set("plane_params", planeParams);
        context.Set("inlier_points", inlierOutput);
        context.Set("outlier_points", outlierOutput);
    }

    private (double[], List<int>) FindBestPlane(
        Mat pointCloud,
        int pointCount,
        double threshold,
        int maxIter,
        double prob,
        int seed,
        bool refinePlane,
        string normalConstraint
    )
    {
        Random rng = seed > 0 ? new Random(seed) : new Random(Guid.NewGuid().GetHashCode());
        int bestInlierCount = 0;
        double[] bestPlane = new double[4];
        List<int> bestInliers = new List<int>();

        int iterations = 0;
        while (iterations < maxIter)
        {
            var sample = SampleThreePoints(pointCount, rng);
            double[,] pts = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                pts[i, 0] = pointCloud.Get<float>(sample[i], 0);
                pts[i, 1] = pointCloud.Get<float>(sample[i], 1);
                pts[i, 2] = pointCloud.Get<float>(sample[i], 2);
            }

            double[] plane = FitPlaneFromThreePoints(pts);
            if (plane == null)
            {
                iterations++;
                continue;
            }

            plane = ApplyNormalConstraint(plane, normalConstraint);

            List<int> inliers = new List<int>();
            for (int i = 0; i < pointCount; i++)
            {
                double x = pointCloud.Get<float>(i, 0);
                double y = pointCloud.Get<float>(i, 1);
                double z = pointCloud.Get<float>(i, 2);
                double dist = Math.Abs(plane[0] * x + plane[1] * y + plane[2] * z + plane[3]);
                if (dist <= threshold)
                    inliers.Add(i);
            }

            if (inliers.Count > bestInlierCount)
            {
                bestInlierCount = inliers.Count;
                bestPlane = plane;
                bestInliers = inliers;

                double inlierRatio = (double)inliers.Count / pointCount;
                if (inlierRatio > 0)
                {
                    double pNoOutliers = 1 - Math.Pow(1 - Math.Pow(inlierRatio, 3), iterations + 1);
                    if (pNoOutliers >= prob)
                        break;
                }
            }

            iterations++;
        }

        if (refinePlane && bestInliers.Count >= 3)
        {
            bestPlane = RefinePlaneWithInliers(pointCloud, bestInliers, normalConstraint);
        }

        return (bestPlane, bestInliers);
    }

    private static int[] SampleThreePoints(int pointCount, Random rng)
    {
        HashSet<int> selected = new HashSet<int>();
        while (selected.Count < 3)
        {
            selected.Add(rng.Next(pointCount));
        }
        return selected.ToArray();
    }

    private static double[] FitPlaneFromThreePoints(double[,] pts)
    {
        double v1x = pts[1, 0] - pts[0, 0];
        double v1y = pts[1, 1] - pts[0, 1];
        double v1z = pts[1, 2] - pts[0, 2];

        double v2x = pts[2, 0] - pts[0, 0];
        double v2y = pts[2, 1] - pts[0, 1];
        double v2z = pts[2, 2] - pts[0, 2];

        double a = v1y * v2z - v1z * v2y;
        double b = v1z * v2x - v1x * v2z;
        double c = v1x * v2y - v1y * v2x;

        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-10)
            return null!;

        a /= norm;
        b /= norm;
        c /= norm;
        double d = -(a * pts[0, 0] + b * pts[0, 1] + c * pts[0, 2]);

        return new[] { a, b, c, d };
    }

    private static double[] ApplyNormalConstraint(double[] plane, string constraint)
    {
        double a = plane[0],
            b = plane[1],
            c = plane[2],
            d = plane[3];

        return constraint.ToLowerInvariant() switch
        {
            "z-up" when c < 0 => new[] { -a, -b, -c, -d },
            "z-down" when c > 0 => new[] { -a, -b, -c, -d },
            "xy-plane" when Math.Abs(c) < Math.Abs(a) || Math.Abs(c) < Math.Abs(b) => new[]
            {
                -a,
                -b,
                -c,
                -d,
            },
            _ => plane,
        };
    }

    private static double[] RefinePlaneWithInliers(
        Mat pointCloud,
        List<int> inliers,
        string normalConstraint
    )
    {
        int n = inliers.Count;

        // 计算质心
        double cx = 0,
            cy = 0,
            cz = 0;
        foreach (int idx in inliers)
        {
            cx += pointCloud.Get<float>(idx, 0);
            cy += pointCloud.Get<float>(idx, 1);
            cz += pointCloud.Get<float>(idx, 2);
        }
        cx /= n;
        cy /= n;
        cz /= n;

        // 构建中心化协方差矩阵（累加）
        double s00 = 0,
            s01 = 0,
            s02 = 0;
        double s11 = 0,
            s12 = 0;
        double s22 = 0;
        foreach (int idx in inliers)
        {
            double dx = pointCloud.Get<float>(idx, 0) - cx;
            double dy = pointCloud.Get<float>(idx, 1) - cy;
            double dz = pointCloud.Get<float>(idx, 2) - cz;
            s00 += dx * dx;
            s01 += dx * dy;
            s02 += dx * dz;
            s11 += dy * dy;
            s12 += dy * dz;
            s22 += dz * dz;
        }

        // 使用 Jacobi SVD 精确求解平面法向量（除以点数得到真正的协方差）
        double[] refined = Math3D.SolvePlaneByCovariance(
            s00 / n,
            s01 / n,
            s02 / n,
            s11 / n,
            s12 / n,
            s22 / n,
            cx,
            cy,
            cz
        );

        return ApplyNormalConstraint(refined, normalConstraint);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
