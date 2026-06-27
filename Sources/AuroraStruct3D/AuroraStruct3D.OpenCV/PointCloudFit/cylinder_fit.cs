using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：圆柱面拟合。
/// <para>
/// 基于点的法向量用 RANSAC 鲁棒拟合圆柱：每次随机取 2 个点及其法向，
/// 以两法向叉积为圆柱轴向，在垂直于轴向的平面内由两条法向射线求交得到轴线位置与半径，
/// 统计点到圆柱面距离在阈值内的内点，取最优模型。常用于管件、圆柱孔、轴类零件检测。
/// </para>
/// <para>
/// 若输入点云不含法向量列（列数 &lt; 6），将自动用 K 近邻 PCA 估计法向量。
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>cylinder_params</c>（Mat）— 圆柱参数 [px, py, pz, ax, ay, az, r]，形状 (7, 1)，CV_64FC1；
///   (px,py,pz) 轴线上一点、(ax,ay,az) 单位轴向、r 半径</item>
///   <item><c>inlier_points</c>（PointCloudData）— 内点点云</item>
///   <item><c>outlier_points</c>（PointCloudData）— 外点点云</item>
///   <item><c>fitting_error</c>（Mat）— 内点到圆柱面距离的 RMSE，形状 (1, 1)，CV_64FC1</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10002-0002-4000-8000-000000000032")]
[Category("3D拟合测量")]
[DisplayName("圆柱拟合")]
[Description("拟合管子/圆柱的轴线和半径，靠点的法向量来算。")]
public class cylinder_fit : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "cylinder_params", DisplayName = "圆柱参数" },
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
            new ConfigParameter
            {
                Name = "normalK",
                DisplayName = "法向估计K近邻",
                ParameterType = typeof(int),
                DefaultValue = "30",
                ValueLimit = new[] { "10", "20", "30", "50", "80" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _distanceThreshold;
    private readonly int _maxIterations;
    private readonly double _probability;
    private readonly int _normalK;
    private bool _disposed;

    public cylinder_fit(
        double distanceThreshold = 0.01,
        int maxIterations = 1000,
        double probability = 0.99,
        int normalK = 30
    )
    {
        _distanceThreshold = distanceThreshold;
        _maxIterations = maxIterations;
        _probability = probability;
        _normalK = normalK;
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
            throw new InvalidOperationException("输入点云为空，无法执行圆柱拟合。");

        int n = cloud.Rows;
        if (n < 2)
            throw new InvalidOperationException("点云点数少于 2，不足以拟合圆柱。");

        double[] xs = new double[n],
            ys = new double[n],
            zs = new double[n];
        double[] nxs = new double[n],
            nys = new double[n],
            nzs = new double[n];

        for (int i = 0; i < n; i++)
        {
            xs[i] = cloud.Get<float>(i, 0);
            ys[i] = cloud.Get<float>(i, 1);
            zs[i] = cloud.Get<float>(i, 2);
        }

        // 法向量：直接读取或估计
        if (cloud.Cols >= 6)
        {
            for (int i = 0; i < n; i++)
            {
                nxs[i] = cloud.Get<float>(i, 3);
                nys[i] = cloud.Get<float>(i, 4);
                nzs[i] = cloud.Get<float>(i, 5);
            }
        }
        else
        {
            using Mat normals = PointCloudUtils.EstimateNormals(cloud, _normalK);
            for (int i = 0; i < n; i++)
            {
                nxs[i] = normals.Get<float>(i, 0);
                nys[i] = normals.Get<float>(i, 1);
                nzs[i] = normals.Get<float>(i, 2);
            }
        }

        // ① RANSAC
        Random rng = new Random();
        int bestCount = 0;
        Cylinder? best = null;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            int i1 = rng.Next(n);
            int i2 = rng.Next(n);
            if (i1 == i2)
                continue;

            Cylinder? cyl = BuildCylinder(i1, i2, xs, ys, zs, nxs, nys, nzs);
            if (cyl is null)
                continue;

            int count = 0;
            for (int i = 0; i < n; i++)
                if (DistanceToCylinder(xs[i], ys[i], zs[i], cyl.Value) <= _distanceThreshold)
                    count++;

            if (count > bestCount)
            {
                bestCount = count;
                best = cyl;

                double ratio = (double)count / n;
                if (ratio > 0)
                {
                    double pNoOutliers = 1 - Math.Pow(1 - ratio * ratio, iter + 1);
                    if (pNoOutliers >= _probability)
                        break;
                }
            }
        }

        if (best is null)
            throw new InvalidOperationException(
                "RANSAC 未能拟合出有效圆柱，请检查法向量质量或调整参数。"
            );

        // ② 用最优模型分类并统计 RMSE
        Cylinder model = best.Value;
        var inlierIdx = new List<int>();
        var outlierIdx = new List<int>();
        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double d = DistanceToCylinder(xs[i], ys[i], zs[i], model);
            if (d <= _distanceThreshold)
            {
                inlierIdx.Add(i);
                sumSq += d * d;
            }
            else
            {
                outlierIdx.Add(i);
            }
        }

        double rmse = inlierIdx.Count > 0 ? Math.Sqrt(sumSq / inlierIdx.Count) : 0;

        // ③ 输出
        Mat cylParams = new Mat(7, 1, MatType.CV_64FC1);
        cylParams.Set(0, 0, model.Px);
        cylParams.Set(1, 0, model.Py);
        cylParams.Set(2, 0, model.Pz);
        cylParams.Set(3, 0, model.Ax);
        cylParams.Set(4, 0, model.Ay);
        cylParams.Set(5, 0, model.Az);
        cylParams.Set(6, 0, model.R);

        Mat errorMat = new Mat(1, 1, MatType.CV_64FC1);
        errorMat.Set(0, 0, rmse);

        var (inPts, inColors) = PointCloudUtils.ExtractSubset(input, inlierIdx, cloud);
        var (outPts, outColors) = PointCloudUtils.ExtractSubset(input, outlierIdx, cloud);

        context.Set("cylinder_params", cylParams);
        context.Set("inlier_points", PointCloudUtils.BuildCloud(inPts, inColors));
        context.Set("outlier_points", PointCloudUtils.BuildCloud(outPts, outColors));
        context.Set("fitting_error", errorMat);
    }

    private readonly struct Cylinder
    {
        public readonly double Px,
            Py,
            Pz,
            Ax,
            Ay,
            Az,
            R;

        public Cylinder(double px, double py, double pz, double ax, double ay, double az, double r)
        {
            Px = px;
            Py = py;
            Pz = pz;
            Ax = ax;
            Ay = ay;
            Az = az;
            R = r;
        }
    }

    /// <summary>由两点及其法向构造圆柱模型；退化（法向平行、射线平行）时返回 null。</summary>
    private static Cylinder? BuildCylinder(
        int i1,
        int i2,
        double[] xs,
        double[] ys,
        double[] zs,
        double[] nxs,
        double[] nys,
        double[] nzs
    )
    {
        // 轴向 = n1 × n2
        double wx = nys[i1] * nzs[i2] - nzs[i1] * nys[i2];
        double wy = nzs[i1] * nxs[i2] - nxs[i1] * nzs[i2];
        double wz = nxs[i1] * nys[i2] - nys[i1] * nxs[i2];
        double wn = Math.Sqrt(wx * wx + wy * wy + wz * wz);
        if (wn < 1e-6)
            return null; // 法向接近平行
        wx /= wn;
        wy /= wn;
        wz /= wn;

        // 垂直于轴向的平面内正交基 (u, v)
        BuildBasis(wx, wy, wz, out double ux, out double uy, out double uz, out double vx, out double vy, out double vz);

        // 两点及其法向投影到 (u, v) 平面
        double p1u = xs[i1] * ux + ys[i1] * uy + zs[i1] * uz;
        double p1v = xs[i1] * vx + ys[i1] * vy + zs[i1] * vz;
        double p2u = xs[i2] * ux + ys[i2] * uy + zs[i2] * uz;
        double p2v = xs[i2] * vx + ys[i2] * vy + zs[i2] * vz;

        double d1u = nxs[i1] * ux + nys[i1] * uy + nzs[i1] * uz;
        double d1v = nxs[i1] * vx + nys[i1] * vy + nzs[i1] * vz;
        double d2u = nxs[i2] * ux + nys[i2] * uy + nzs[i2] * uz;
        double d2v = nxs[i2] * vx + nys[i2] * vy + nzs[i2] * vz;

        double l1 = Math.Sqrt(d1u * d1u + d1v * d1v);
        double l2 = Math.Sqrt(d2u * d2u + d2v * d2v);
        if (l1 < 1e-9 || l2 < 1e-9)
            return null; // 法向几乎平行于轴
        d1u /= l1;
        d1v /= l1;
        d2u /= l2;
        d2v /= l2;

        // 求两射线交点：P1 + s·d1 = P2 + t·d2
        double det = d1u * (-d2v) - (-d2u) * d1v;
        if (Math.Abs(det) < 1e-9)
            return null; // 射线平行
        double rhsU = p2u - p1u;
        double rhsV = p2v - p1v;
        double s = (rhsU * (-d2v) - (-d2u) * rhsV) / det;

        double cu = p1u + s * d1u;
        double cv = p1v + s * d1v;

        double r = Math.Sqrt((cu - p1u) * (cu - p1u) + (cv - p1v) * (cv - p1v));
        if (r < 1e-9 || double.IsNaN(r))
            return null;

        // 轴线上一点（w 分量取 0）
        double px = cu * ux + cv * vx;
        double py = cu * uy + cv * vy;
        double pz = cu * uz + cv * vz;

        return new Cylinder(px, py, pz, wx, wy, wz, r);
    }

    /// <summary>点到圆柱面的距离：|点到轴线垂距 − r|。</summary>
    private static double DistanceToCylinder(double x, double y, double z, Cylinder c)
    {
        double vx = x - c.Px,
            vy = y - c.Py,
            vz = z - c.Pz;
        double proj = vx * c.Ax + vy * c.Ay + vz * c.Az;
        double px = vx - proj * c.Ax;
        double py = vy - proj * c.Ay;
        double pz = vz - proj * c.Az;
        double axisDist = Math.Sqrt(px * px + py * py + pz * pz);
        return Math.Abs(axisDist - c.R);
    }

    /// <summary>由单位向量构建与之正交的平面内正交基 (u, v)。</summary>
    private static void BuildBasis(
        double nx,
        double ny,
        double nz,
        out double ux,
        out double uy,
        out double uz,
        out double vx,
        out double vy,
        out double vz
    )
    {
        double ax = Math.Abs(nx),
            ay = Math.Abs(ny),
            az = Math.Abs(nz);
        double hx = 0,
            hy = 0,
            hz = 0;
        if (ax <= ay && ax <= az)
            hx = 1;
        else if (ay <= az)
            hy = 1;
        else
            hz = 1;

        ux = hy * nz - hz * ny;
        uy = hz * nx - hx * nz;
        uz = hx * ny - hy * nx;
        double un = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= un;
        uy /= un;
        uz /= un;

        vx = ny * uz - nz * uy;
        vy = nz * ux - nx * uz;
        vz = nx * uy - ny * ux;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
