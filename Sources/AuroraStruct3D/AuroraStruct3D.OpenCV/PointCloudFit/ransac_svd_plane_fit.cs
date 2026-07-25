using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：RANSAC + 最小二乘复合精拟合。
/// <para>
/// 分两步完成高精度平面拟合：
/// <list type="number">
///   <item><description>RANSAC 粗拟合：使用 RANSAC 算法快速筛选基准内点，剔除离群噪声</description></item>
///   <item><description>SVD 精拟合：对所有 RANSAC 内点执行最小二乘 SVD 拟合，得到精确平面参数</description></item>
/// </list>
/// 这种复合策略兼顾了鲁棒性（RANSAC 抗离群）和精度（最小二乘无偏估计）。
/// 平面方程：ax + by + cz + d = 0，其中 a^2 + b^2 + c^2 = 1。
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>plane_params</c>（Mat）— 经过 SVD 精拟合的平面参数 [a, b, c, d]，形状 (4, 1)，CV_64FC1</item>
///   <item><c>ransac_plane_params</c>（Mat）— RANSAC 粗拟合的平面参数，用于对比，形状 (4, 1)，CV_64FC1</item>
///   <item><c>inlier_points</c>（PointCloudData）— RANSAC 筛选出的内点</item>
///   <item><c>outlier_points</c>（PointCloudData）— RANSAC 筛选出的外点</item>
///   <item><c>fitting_error</c>（Mat）— 精拟合误差（RMSE），形状 (1, 1)，CV_64FC1</item>
/// </list>
/// </para>
/// </summary>
[Guid("f5a34567-8901-2345-6789-01234567890e")]
[Category("3D拟合测量")]
[DisplayName("RANSAC+SVD拟合")]
[Description("先 RANSAC 挑出干净点，再最小二乘精拟合，又稳又准。")]
public class ransac_svd_plane_fit : IOperator
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
            new MatImg() { ParameterName = "ransac_plane_params", DisplayName = "粗拟合参数" },
            new PointCloudData() { ParameterName = "inlier_points", DisplayName = "内点点云" },
            new PointCloudData() { ParameterName = "outlier_points", DisplayName = "外点点云" },
            new MatImg() { ParameterName = "fitting_error", DisplayName = "拟合误差" },
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
        };

    private readonly double _distanceThreshold;
    private readonly int _maxIterations;
    private readonly double _probability;
    private bool _disposed;

    public ransac_svd_plane_fit(
        double distanceThreshold = 0.01,
        int maxIterations = 1000,
        double probability = 0.99
    )
    {
        if (!double.IsFinite(distanceThreshold) || distanceThreshold <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(distanceThreshold),
                "距离阈值必须是大于 0 的有限数值。"
            );
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "最大迭代次数必须大于 0。");
        if (!double.IsFinite(probability) || probability <= 0 || probability >= 1)
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                "置信概率必须位于 (0, 1) 区间。"
            );
        _distanceThreshold = distanceThreshold;
        _maxIterations = maxIterations;
        _probability = probability;
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
            throw new InvalidOperationException("输入点云为空，无法执行复合平面拟合。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数少于 3，不足以拟合平面。");

        // ==========================================
        // 第一步：RANSAC 粗拟合，筛选内点
        // ==========================================
        (double[] ransacPlane, List<int> inlierIndices) = RansacFindBestPlane(
            pointCloud,
            pointCount,
            _distanceThreshold,
            _maxIterations,
            _probability
        );

        if (inlierIndices.Count < 3)
            throw new InvalidOperationException(
                $"RANSAC 筛选出的内点不足（{inlierIndices.Count} 个），无法进行精拟合。请增大距离阈值。"
            );

        // 拆分内点和外点
        (Mat inlierCloud, Mat? inlierColors) = PointCloudUtils.ExtractSubset(
            input,
            inlierIndices,
            pointCloud
        );
        var outlierIndices = Enumerable.Range(0, pointCount).Except(inlierIndices).ToList();
        (Mat outlierCloud, Mat? outlierColors) = PointCloudUtils.ExtractSubset(
            input,
            outlierIndices,
            pointCloud
        );

        // ==========================================
        // 第二步：SVD 最小二乘精拟合（仅对内点）
        // ==========================================
        double[] refinedPlane = SvdRefinePlane(inlierCloud, inlierIndices.Count);

        // 拟合误差应基于用于精拟合的内点。把已被 RANSAC 判定为外点的数据计入
        // RMSE 会让“平面拟合误差”被离群点主导，与算子描述和标准拟合误差定义不符。
        double sumSqDist = 0;
        for (int i = 0; i < inlierIndices.Count; i++)
        {
            double x = inlierCloud.Get<float>(i, 0);
            double y = inlierCloud.Get<float>(i, 1);
            double z = inlierCloud.Get<float>(i, 2);
            double dist =
                refinedPlane[0] * x + refinedPlane[1] * y + refinedPlane[2] * z + refinedPlane[3];
            sumSqDist += dist * dist;
        }
        double rmse = Math.Sqrt(sumSqDist / inlierIndices.Count);

        // ==========================================
        // 输出结果
        // ==========================================
        // 精拟合平面参数
        Mat planeParams = new Mat(4, 1, MatType.CV_64FC1);
        for (int i = 0; i < 4; i++)
            planeParams.Set(i, 0, refinedPlane[i]);

        // RANSAC 粗拟合平面参数（对比用）
        Mat ransacParams = new Mat(4, 1, MatType.CV_64FC1);
        for (int i = 0; i < 4; i++)
            ransacParams.Set(i, 0, ransacPlane[i]);

        // 拟合误差
        Mat errorMat = new Mat(1, 1, MatType.CV_64FC1);
        errorMat.Set(0, 0, rmse);

        // 内点点云
        var inlierOutput = new PointCloudData();
        inlierOutput.Value = inlierCloud;
        if (inlierColors != null)
            inlierOutput.SetColors(inlierColors);

        // 外点点云
        var outlierOutput = new PointCloudData();
        outlierOutput.Value = outlierCloud;
        if (outlierColors != null)
            outlierOutput.SetColors(outlierColors);

        context.Set("plane_params", planeParams);
        context.Set("ransac_plane_params", ransacParams);
        context.Set("inlier_points", inlierOutput);
        context.Set("outlier_points", outlierOutput);
        context.Set("fitting_error", errorMat);
    }

    // ===== RANSAC 实现 =====

    private (double[], List<int>) RansacFindBestPlane(
        Mat pointCloud,
        int pointCount,
        double threshold,
        int maxIter,
        double prob
    )
    {
        Random rng = new Random(Guid.NewGuid().GetHashCode());
        int bestInlierCount = 0;
        double[] bestPlane = new double[4];
        List<int> bestInliers = new List<int>();

        int iterations = 0;
        while (iterations < maxIter)
        {
            var sample = SampleThreePoints(pointCount, rng);
            double[]? plane = FitPlaneThreePoints(pointCloud, sample);
            if (plane == null)
            {
                iterations++;
                continue;
            }

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

        return (bestPlane, bestInliers);
    }

    private static int[] SampleThreePoints(int pointCount, Random rng)
    {
        HashSet<int> selected = new HashSet<int>();
        while (selected.Count < 3)
            selected.Add(rng.Next(pointCount));
        return selected.ToArray();
    }

    private static double[]? FitPlaneThreePoints(Mat pointCloud, int[] indices)
    {
        double x0 = pointCloud.Get<float>(indices[0], 0);
        double y0 = pointCloud.Get<float>(indices[0], 1);
        double z0 = pointCloud.Get<float>(indices[0], 2);

        double v1x = pointCloud.Get<float>(indices[1], 0) - x0;
        double v1y = pointCloud.Get<float>(indices[1], 1) - y0;
        double v1z = pointCloud.Get<float>(indices[1], 2) - z0;

        double v2x = pointCloud.Get<float>(indices[2], 0) - x0;
        double v2y = pointCloud.Get<float>(indices[2], 1) - y0;
        double v2z = pointCloud.Get<float>(indices[2], 2) - z0;

        double a = v1y * v2z - v1z * v2y;
        double b = v1z * v2x - v1x * v2z;
        double c = v1x * v2y - v1y * v2x;

        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-10)
            return null;

        a /= norm;
        b /= norm;
        c /= norm;
        double d = -(a * x0 + b * y0 + c * z0);

        return new[] { a, b, c, d };
    }

    // ===== SVD 精拟合实现 =====

    private static double[] SvdRefinePlane(Mat inlierCloud, int inlierCount)
    {
        // 计算质心
        double cx = 0,
            cy = 0,
            cz = 0;
        for (int i = 0; i < inlierCount; i++)
        {
            cx += inlierCloud.Get<float>(i, 0);
            cy += inlierCloud.Get<float>(i, 1);
            cz += inlierCloud.Get<float>(i, 2);
        }
        cx /= inlierCount;
        cy /= inlierCount;
        cz /= inlierCount;

        // 构建协方差矩阵
        double c00 = 0,
            c01 = 0,
            c02 = 0;
        double c11 = 0,
            c12 = 0;
        double c22 = 0;

        for (int i = 0; i < inlierCount; i++)
        {
            double dx = inlierCloud.Get<float>(i, 0) - cx;
            double dy = inlierCloud.Get<float>(i, 1) - cy;
            double dz = inlierCloud.Get<float>(i, 2) - cz;

            c00 += dx * dx;
            c01 += dx * dy;
            c02 += dx * dz;
            c11 += dy * dy;
            c12 += dy * dz;
            c22 += dz * dz;
        }

        double[] plane = Math3D.SolvePlaneByCovariance(
            c00 / inlierCount,
            c01 / inlierCount,
            c02 / inlierCount,
            c11 / inlierCount,
            c12 / inlierCount,
            c22 / inlierCount,
            cx,
            cy,
            cz
        );

        return plane;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
