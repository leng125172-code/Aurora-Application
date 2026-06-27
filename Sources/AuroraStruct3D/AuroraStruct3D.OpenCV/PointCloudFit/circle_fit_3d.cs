using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：3D 空间圆拟合。
/// <para>
/// 先用 RANSAC 鲁棒筛选内点（每次随机取 3 点构造唯一外接圆，统计点到圆距离在阈值内的点），
/// 再对内点做精拟合：最小二乘拟合支撑平面 → 将内点投影到平面内 → 平面内 Kåsa 代数圆拟合 →
/// 圆心映射回 3D。适用于圆孔、法兰、圆形边界等环状结构提取。
/// </para>
/// <para>
/// 点到圆的距离定义为 √(d_plane² + (r_radial − r)²)，其中 d_plane 为点到圆所在平面的距离，
/// r_radial 为点在平面内投影到圆心的距离。
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>circle_params</c>（Mat）— 圆参数 [cx, cy, cz, nx, ny, nz, r]，形状 (7, 1)，CV_64FC1；
///   (cx,cy,cz) 圆心、(nx,ny,nz) 单位法向、r 半径</item>
///   <item><c>inlier_points</c>（PointCloudData）— 内点点云</item>
///   <item><c>outlier_points</c>（PointCloudData）— 外点点云</item>
///   <item><c>fitting_error</c>（Mat）— 内点到圆距离的 RMSE，形状 (1, 1)，CV_64FC1</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10004-0004-4000-8000-000000000034")]
[Category("3D拟合测量")]
[DisplayName("3D圆拟合")]
[Description("拟合空间里的圆，定位圆孔、法兰、圆形边界用。")]
public class circle_fit_3d : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "circle_params", DisplayName = "圆参数" },
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

    public circle_fit_3d(
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
            throw new InvalidOperationException("输入点云为空，无法执行圆拟合。");

        int n = cloud.Rows;
        if (n < 3)
            throw new InvalidOperationException("点云点数少于 3，不足以拟合圆。");

        double[] xs = new double[n],
            ys = new double[n],
            zs = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = cloud.Get<float>(i, 0);
            ys[i] = cloud.Get<float>(i, 1);
            zs[i] = cloud.Get<float>(i, 2);
        }

        // ① RANSAC：三点外接圆筛选内点
        Random rng = new Random();
        int bestCount = 0;
        Circle? bestCircle = null;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            int[] s = SampleDistinct(n, 3, rng);
            Circle? circle = CircumCircle(s[0], s[1], s[2], xs, ys, zs);
            if (circle is null)
                continue;

            int count = 0;
            for (int i = 0; i < n; i++)
                if (DistanceToCircle(xs[i], ys[i], zs[i], circle.Value) <= _distanceThreshold)
                    count++;

            if (count > bestCount)
            {
                bestCount = count;
                bestCircle = circle;

                double ratio = (double)count / n;
                if (ratio > 0)
                {
                    double pNoOutliers = 1 - Math.Pow(1 - Math.Pow(ratio, 3), iter + 1);
                    if (pNoOutliers >= _probability)
                        break;
                }
            }
        }

        if (bestCircle is null)
            throw new InvalidOperationException("RANSAC 未能拟合出有效圆，请检查输入点云或调整参数。");

        // 收集 RANSAC 最优内点
        var ransacInliers = new List<int>();
        for (int i = 0; i < n; i++)
            if (DistanceToCircle(xs[i], ys[i], zs[i], bestCircle.Value) <= _distanceThreshold)
                ransacInliers.Add(i);

        // ② 精拟合：平面 + Kåsa
        Circle refined =
            ransacInliers.Count >= 3
                ? FitCircleByPlaneProjection(ransacInliers, xs, ys, zs)
                : bestCircle.Value;

        // ③ 用精拟合结果重新分类并统计 RMSE
        var inlierIdx = new List<int>();
        var outlierIdx = new List<int>();
        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double d = DistanceToCircle(xs[i], ys[i], zs[i], refined);
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

        // ④ 输出
        Mat circleParams = new Mat(7, 1, MatType.CV_64FC1);
        circleParams.Set(0, 0, refined.Cx);
        circleParams.Set(1, 0, refined.Cy);
        circleParams.Set(2, 0, refined.Cz);
        circleParams.Set(3, 0, refined.Nx);
        circleParams.Set(4, 0, refined.Ny);
        circleParams.Set(5, 0, refined.Nz);
        circleParams.Set(6, 0, refined.R);

        Mat errorMat = new Mat(1, 1, MatType.CV_64FC1);
        errorMat.Set(0, 0, rmse);

        var (inPts, inColors) = PointCloudUtils.ExtractSubset(input, inlierIdx, cloud);
        var (outPts, outColors) = PointCloudUtils.ExtractSubset(input, outlierIdx, cloud);

        context.Set("circle_params", circleParams);
        context.Set("inlier_points", PointCloudUtils.BuildCloud(inPts, inColors));
        context.Set("outlier_points", PointCloudUtils.BuildCloud(outPts, outColors));
        context.Set("fitting_error", errorMat);
    }

    private readonly struct Circle
    {
        public readonly double Cx,
            Cy,
            Cz,
            Nx,
            Ny,
            Nz,
            R;

        public Circle(double cx, double cy, double cz, double nx, double ny, double nz, double r)
        {
            Cx = cx;
            Cy = cy;
            Cz = cz;
            Nx = nx;
            Ny = ny;
            Nz = nz;
            R = r;
        }
    }

    private static int[] SampleDistinct(int n, int k, Random rng)
    {
        var set = new HashSet<int>();
        while (set.Count < k)
            set.Add(rng.Next(n));
        return set.ToArray();
    }

    /// <summary>由 3 点求外接圆（圆心、单位法向、半径）；共线时返回 null。</summary>
    private static Circle? CircumCircle(int i1, int i2, int i3, double[] xs, double[] ys, double[] zs)
    {
        // 以第 3 点为参考
        double acx = xs[i1] - xs[i3],
            acy = ys[i1] - ys[i3],
            acz = zs[i1] - zs[i3];
        double bcx = xs[i2] - xs[i3],
            bcy = ys[i2] - ys[i3],
            bcz = zs[i2] - zs[i3];

        // cross = ac × bc
        double crx = acy * bcz - acz * bcy;
        double cry = acz * bcx - acx * bcz;
        double crz = acx * bcy - acy * bcx;
        double crossNormSq = crx * crx + cry * cry + crz * crz;
        if (crossNormSq < 1e-20)
            return null; // 共线

        double acSq = acx * acx + acy * acy + acz * acz;
        double bcSq = bcx * bcx + bcy * bcy + bcz * bcz;

        // num = (acSq·bc − bcSq·ac) × cross
        double tx = acSq * bcx - bcSq * acx;
        double ty = acSq * bcy - bcSq * acy;
        double tz = acSq * bcz - bcSq * acz;
        double nx = ty * crz - tz * cry;
        double ny = tz * crx - tx * crz;
        double nz = tx * cry - ty * crx;

        double inv = 1.0 / (2.0 * crossNormSq);
        double ox = nx * inv,
            oy = ny * inv,
            oz = nz * inv; // 相对参考点的圆心偏移

        double cx = xs[i3] + ox;
        double cy = ys[i3] + oy;
        double cz = zs[i3] + oz;
        double r = Math.Sqrt(ox * ox + oy * oy + oz * oz);

        double cn = Math.Sqrt(crossNormSq);
        return new Circle(cx, cy, cz, crx / cn, cry / cn, crz / cn, r);
    }

    /// <summary>点到 3D 圆的最近距离：√(平面外距离² + (径向距离 − r)²)。</summary>
    private static double DistanceToCircle(double x, double y, double z, Circle c)
    {
        double vx = x - c.Cx,
            vy = y - c.Cy,
            vz = z - c.Cz;
        double dPlane = vx * c.Nx + vy * c.Ny + vz * c.Nz;
        // 平面内分量
        double px = vx - dPlane * c.Nx;
        double py = vy - dPlane * c.Ny;
        double pz = vz - dPlane * c.Nz;
        double rRadial = Math.Sqrt(px * px + py * py + pz * pz);
        double dr = rRadial - c.R;
        return Math.Sqrt(dPlane * dPlane + dr * dr);
    }

    /// <summary>对内点：拟合平面 → 投影 → 平面内 Kåsa 圆拟合 → 圆心映射回 3D。</summary>
    private static Circle FitCircleByPlaneProjection(
        List<int> idx,
        double[] xs,
        double[] ys,
        double[] zs
    )
    {
        int m = idx.Count;

        // 质心
        double ox = 0,
            oy = 0,
            oz = 0;
        foreach (int i in idx)
        {
            ox += xs[i];
            oy += ys[i];
            oz += zs[i];
        }
        ox /= m;
        oy /= m;
        oz /= m;

        // 中心化协方差 → 平面法向
        double c00 = 0,
            c01 = 0,
            c02 = 0,
            c11 = 0,
            c12 = 0,
            c22 = 0;
        foreach (int i in idx)
        {
            double dx = xs[i] - ox,
                dy = ys[i] - oy,
                dz = zs[i] - oz;
            c00 += dx * dx;
            c01 += dx * dy;
            c02 += dx * dz;
            c11 += dy * dy;
            c12 += dy * dz;
            c22 += dz * dz;
        }
        double[] plane = Math3D.SolvePlaneByCovariance(
            c00 / m,
            c01 / m,
            c02 / m,
            c11 / m,
            c12 / m,
            c22 / m,
            ox,
            oy,
            oz
        );
        double nx = plane[0],
            ny = plane[1],
            nz = plane[2];

        // 构建平面内正交基 (u, v)
        BuildBasis(nx, ny, nz, out double ux, out double uy, out double uz, out double vx, out double vy, out double vz);

        // 投影到 2D 并做 Kåsa 拟合
        using Mat A = new Mat(m, 3, MatType.CV_64FC1);
        using Mat b = new Mat(m, 1, MatType.CV_64FC1);
        for (int k = 0; k < m; k++)
        {
            int i = idx[k];
            double lx = xs[i] - ox,
                ly = ys[i] - oy,
                lz = zs[i] - oz;
            double u = lx * ux + ly * uy + lz * uz;
            double v = lx * vx + ly * vy + lz * vz;
            A.Set(k, 0, u);
            A.Set(k, 1, v);
            A.Set(k, 2, 1.0);
            b.Set(k, 0, -(u * u + v * v));
        }

        using Mat sol = new Mat();
        if (!Cv2.Solve(A, b, sol, DecompTypes.SVD))
            throw new InvalidOperationException("圆 Kåsa 最小二乘求解失败。");

        double D = sol.Get<double>(0, 0);
        double E = sol.Get<double>(1, 0);
        double F = sol.Get<double>(2, 0);
        double cu = -D / 2.0;
        double cv = -E / 2.0;
        double r2 = cu * cu + cv * cv - F;
        double r = r2 > 0 ? Math.Sqrt(r2) : 0;

        // 圆心映射回 3D
        double cx = ox + cu * ux + cv * vx;
        double cy = oy + cu * uy + cv * vy;
        double cz = oz + cu * uz + cv * vz;

        return new Circle(cx, cy, cz, nx, ny, nz, r);
    }

    /// <summary>由单位法向构建平面内正交基 (u, v)。</summary>
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
        // 选与法向最不平行的坐标轴做叉积，避免退化
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

        // u = normalize(h × n)
        ux = hy * nz - hz * ny;
        uy = hz * nx - hx * nz;
        uz = hx * ny - hy * nx;
        double un = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= un;
        uy /= un;
        uz /= un;

        // v = n × u
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
