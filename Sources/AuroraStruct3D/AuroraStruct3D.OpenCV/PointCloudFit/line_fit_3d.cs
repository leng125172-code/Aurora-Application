using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：3D 空间直线拟合。
/// <para>
/// 先用 RANSAC 鲁棒筛选内点（每次随机取 2 点定直线，统计点到直线距离在阈值内的点），
/// 再对内点做 PCA（主成分分析），以最大特征值对应的特征向量作为直线方向、内点质心为直线上一点，
/// 完成最小二乘精拟合。适用于焊缝、棱边、管材轴线等线状结构提取。
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>line_params</c>（Mat）— 直线参数 [px, py, pz, dx, dy, dz]，形状 (6, 1)，CV_64FC1；
///   (px,py,pz) 为线上一点，(dx,dy,dz) 为单位方向向量</item>
///   <item><c>inlier_points</c>（PointCloudData）— 内点点云</item>
///   <item><c>outlier_points</c>（PointCloudData）— 外点点云</item>
///   <item><c>fitting_error</c>（Mat）— 内点到直线距离的 RMSE，形状 (1, 1)，CV_64FC1</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10003-0003-4000-8000-000000000033")]
[Category("3D拟合测量")]
[DisplayName("3D直线拟合")]
[Description("拟合空间直线，提取焊缝、棱边、轴线用。")]
public class line_fit_3d : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "line_params", DisplayName = "直线参数" },
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

    public line_fit_3d(
        double distanceThreshold = 0.01,
        int maxIterations = 1000,
        double probability = 0.99
    )
    {
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

        Mat cloud = input.PointCloud!;
        if (cloud is null || cloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行直线拟合。");

        int n = cloud.Rows;
        if (n < 2)
            throw new InvalidOperationException("点云点数少于 2，不足以拟合直线。");

        float[] xs = new float[n],
            ys = new float[n],
            zs = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = cloud.Get<float>(i, 0);
            ys[i] = cloud.Get<float>(i, 1);
            zs[i] = cloud.Get<float>(i, 2);
        }

        // ① RANSAC 筛选内点
        List<int> bestInliers = RansacInliers(xs, ys, zs, n);

        // ② PCA 精拟合（内点不足时退化到全部点）
        List<int> fitSet = bestInliers.Count >= 2 ? bestInliers : Enumerable.Range(0, n).ToList();
        (double[] point, double[] dir) = FitLinePca(xs, ys, zs, fitSet);

        // ③ 用精拟合直线重新判定内点/外点并统计 RMSE
        var inlierIdx = new List<int>();
        var outlierIdx = new List<int>();
        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double dist = PointToLineDistance(xs[i], ys[i], zs[i], point, dir);
            if (dist <= _distanceThreshold)
            {
                inlierIdx.Add(i);
                sumSq += dist * dist;
            }
            else
            {
                outlierIdx.Add(i);
            }
        }

        double rmse = inlierIdx.Count > 0 ? Math.Sqrt(sumSq / inlierIdx.Count) : 0;

        // ④ 输出
        Mat lineParams = new Mat(6, 1, MatType.CV_64FC1);
        for (int i = 0; i < 3; i++)
        {
            lineParams.Set(i, 0, point[i]);
            lineParams.Set(i + 3, 0, dir[i]);
        }

        Mat errorMat = new Mat(1, 1, MatType.CV_64FC1);
        errorMat.Set(0, 0, rmse);

        var (inPts, inColors) = PointCloudUtils.ExtractSubset(input, inlierIdx, cloud);
        var (outPts, outColors) = PointCloudUtils.ExtractSubset(input, outlierIdx, cloud);

        context.Set("line_params", lineParams);
        context.Set("inlier_points", PointCloudUtils.BuildCloud(inPts, inColors));
        context.Set("outlier_points", PointCloudUtils.BuildCloud(outPts, outColors));
        context.Set("fitting_error", errorMat);
    }

    private List<int> RansacInliers(float[] xs, float[] ys, float[] zs, int n)
    {
        Random rng = new Random();
        int bestCount = 0;
        List<int> best = new List<int>();

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            int i1 = rng.Next(n);
            int i2 = rng.Next(n);
            if (i1 == i2)
                continue;

            double dx = xs[i2] - xs[i1];
            double dy = ys[i2] - ys[i1];
            double dz = zs[i2] - zs[i1];
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-10)
                continue;

            double[] dir = { dx / len, dy / len, dz / len };
            double[] p0 = { xs[i1], ys[i1], zs[i1] };

            var inliers = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (PointToLineDistance(xs[i], ys[i], zs[i], p0, dir) <= _distanceThreshold)
                    inliers.Add(i);
            }

            if (inliers.Count > bestCount)
            {
                bestCount = inliers.Count;
                best = inliers;

                double ratio = (double)inliers.Count / n;
                if (ratio > 0)
                {
                    double pNoOutliers = 1 - Math.Pow(1 - ratio * ratio, iter + 1);
                    if (pNoOutliers >= _probability)
                        break;
                }
            }
        }

        return best;
    }

    private static (double[] point, double[] dir) FitLinePca(
        float[] xs,
        float[] ys,
        float[] zs,
        List<int> indices
    )
    {
        int m = indices.Count;
        double cx = 0,
            cy = 0,
            cz = 0;
        foreach (int idx in indices)
        {
            cx += xs[idx];
            cy += ys[idx];
            cz += zs[idx];
        }
        cx /= m;
        cy /= m;
        cz /= m;

        double c00 = 0,
            c01 = 0,
            c02 = 0,
            c11 = 0,
            c12 = 0,
            c22 = 0;
        foreach (int idx in indices)
        {
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

        var (_, V) = Math3D.Jacobi3x3(c00, c01, c02, c11, c12, c22);
        // 最大特征值位于索引 0（降序）→ 主方向即直线方向
        double[] dir = { V[0, 0], V[1, 0], V[2, 0] };
        double norm = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
        if (norm > 1e-12)
        {
            dir[0] /= norm;
            dir[1] /= norm;
            dir[2] /= norm;
        }

        return (new[] { cx, cy, cz }, dir);
    }

    /// <summary>点到直线的垂直距离：|(p - p0) × dir|（dir 为单位向量）。</summary>
    private static double PointToLineDistance(double x, double y, double z, double[] p0, double[] dir)
    {
        double vx = x - p0[0];
        double vy = y - p0[1];
        double vz = z - p0[2];

        double cx = vy * dir[2] - vz * dir[1];
        double cy = vz * dir[0] - vx * dir[2];
        double cz = vx * dir[1] - vy * dir[0];

        return Math.Sqrt(cx * cx + cy * cy + cz * cz);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
