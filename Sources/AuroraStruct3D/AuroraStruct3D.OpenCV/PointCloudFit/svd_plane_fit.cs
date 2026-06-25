namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：最小二乘 SVD 平面拟合。
/// <para>
/// 使用奇异值分解对点云进行最小二乘平面拟合，最小化所有点到平面的距离平方和。
/// 适用于点云中噪声近似服从高斯分布的精确拟合场景。
/// 平面方程：ax + by + cz + d = 0，其中 a^2 + b^2 + c^2 = 1（法向量单位化）。
/// </para>
/// <para>
/// 拟合原理：
/// <list type="bullet">
///   <item>计算点云质心并中心化</item>
///   <item>构建协方差矩阵并执行 SVD 分解</item>
///   <item>最小奇异值对应的奇异向量即为平面法向量</item>
///   <item>d = -(a·cx + b·cy + c·cz)，其中 (cx, cy, cz) 为质心坐标</item>
/// </list>
/// </para>
/// <para>
/// 输出：
/// <list type="bullet">
///   <item><c>plane_params</c>（Mat）— 平面参数 [a, b, c, d]，形状 (4, 1)，CV_64FC1</item>
///   <item><c>fitted_plane_points</c>（PointCloudData）— 投影到拟合平面上的点云</item>
///   <item><c>fitting_error</c>（Mat）— 拟合误差（各点到平面的距离均方根），形状 (1, 1)，CV_64FC1</item>
/// </list>
/// </para>
/// </summary>
[Guid("e4f23456-7890-1234-5678-90123456780d")]
[Category("平面拟合")]
[DisplayName("SVD 最小二乘拟合")]
[Description("使用 SVD 奇异值分解对点云做最小二乘平面拟合，最小化距离平方和。")]
public class svd_plane_fit : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new PointCloudData() { ParameterName = "input_point_cloud" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "plane_params" },
            new PointCloudData() { ParameterName = "fitted_plane_points" },
            new MatImg() { ParameterName = "fitting_error" },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    public svd_plane_fit() { }

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
            throw new InvalidOperationException("输入点云为空，无法执行 SVD 平面拟合。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数少于 3，不足以拟合平面。");

        // ① 计算质心
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < pointCount; i++)
        {
            cx += pointCloud.Get<float>(i, 0);
            cy += pointCloud.Get<float>(i, 1);
            cz += pointCloud.Get<float>(i, 2);
        }
        cx /= pointCount;
        cy /= pointCount;
        cz /= pointCount;

        // ② 构建中心化协方差矩阵 (3×3)
        double c00 = 0, c01 = 0, c02 = 0;
        double c11 = 0, c12 = 0;
        double c22 = 0;

        for (int i = 0; i < pointCount; i++)
        {
            double dx = pointCloud.Get<float>(i, 0) - cx;
            double dy = pointCloud.Get<float>(i, 1) - cy;
            double dz = pointCloud.Get<float>(i, 2) - cz;

            c00 += dx * dx;
            c01 += dx * dy;
            c02 += dx * dz;
            c11 += dy * dy;
            c12 += dy * dz;
            c22 += dz * dz;
        }

        // ③ SVD 拟合平面法向量（最小奇异值对应的右奇异向量）
        double[] plane = SolvePlaneBySVD(
            c00 / pointCount,
            c01 / pointCount,
            c02 / pointCount,
            c11 / pointCount,
            c12 / pointCount,
            c22 / pointCount,
            cx,
            cy,
            cz
        );

        // ④ 计算各点到平面的距离，统计拟合误差
        double sumSqDist = 0;
        int colCount = pointCloud.Cols;

        Mat projectedPoints = new Mat(pointCount, colCount, MatType.CV_32FC1);
        for (int i = 0; i < pointCount; i++)
        {
            double x = pointCloud.Get<float>(i, 0);
            double y = pointCloud.Get<float>(i, 1);
            double z = pointCloud.Get<float>(i, 2);

            double dist = plane[0] * x + plane[1] * y + plane[2] * z + plane[3];
            sumSqDist += dist * dist;

            // 投影到平面：p' = p - dist * n
            double projX = (float)(x - dist * plane[0]);
            double projY = (float)(y - dist * plane[1]);
            double projZ = (float)(z - dist * plane[2]);

            projectedPoints.Set(i, 0, (float)projX);
            projectedPoints.Set(i, 1, (float)projY);
            projectedPoints.Set(i, 2, (float)projZ);

            // 复制法向量列（如果有）
            for (int c = 3; c < colCount; c++)
                projectedPoints.Set(i, c, pointCloud.Get<float>(i, c));
        }

        double rmse = Math.Sqrt(sumSqDist / pointCount);

        // ⑤ 输出结果
        Mat planeParams = new Mat(4, 1, MatType.CV_64FC1);
        for (int i = 0; i < 4; i++)
            planeParams.Set(i, 0, plane[i]);

        Mat errorMat = new Mat(1, 1, MatType.CV_64FC1);
        errorMat.Set(0, 0, rmse);

        var outputPointCloud = new PointCloudData();
        outputPointCloud.Value = projectedPoints;
        if (input.HasColors && input.Colors != null)
            outputPointCloud.SetColors(input.Colors.Clone());

        context.Set("plane_params", planeParams);
        context.Set("fitted_plane_points", outputPointCloud);
        context.Set("fitting_error", errorMat);
    }

    /// <summary>
    /// 使用 Jacobi 特征值分解法求解 3×3 协方差矩阵的最小特征值对应的特征向量。
    /// 返回平面参数 [a, b, c, d]。
    /// </summary>
    private static double[] SolvePlaneBySVD(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22,
        double cx,
        double cy,
        double cz
    )
    {
        // 构建对称矩阵
        double[,] M = new double[3, 3]
        {
            { s00, s01, s02 },
            { s01, s11, s12 },
            { s02, s12, s22 },
        };

        // Jacobi 特征值分解求最小特征值对应的特征向量
        double[] eigenvalues = new double[3];
        double[,] eigenvectors = new double[3, 3];

        // 初始化特征向量为单位矩阵
        for (int i = 0; i < 3; i++)
            eigenvectors[i, i] = 1.0;

        for (int iter = 0; iter < 100; iter++)
        {
            // 找最大非对角元素
            int p = 0, q = 1;
            double maxOffDiag = Math.Abs(M[0, 1]);
            if (Math.Abs(M[0, 2]) > maxOffDiag) { maxOffDiag = Math.Abs(M[0, 2]); p = 0; q = 2; }
            if (Math.Abs(M[1, 2]) > maxOffDiag) { maxOffDiag = Math.Abs(M[1, 2]); p = 1; q = 2; }

            if (maxOffDiag < 1e-15)
                break;

            // 计算旋转角度
            double theta = (M[q, q] - M[p, p]) / (2 * M[p, q]);
            double t = theta >= 0
                ? 1.0 / (theta + Math.Sqrt(theta * theta + 1))
                : 1.0 / (theta - Math.Sqrt(theta * theta + 1));
            double cosA = 1.0 / Math.Sqrt(t * t + 1);
            double sinA = t * cosA;

            // 旋转矩阵
            double mpp = M[p, p];
            double mqq = M[q, q];
            double mpq = M[p, q];

            M[p, p] = cosA * cosA * mpp + sinA * sinA * mqq - 2 * sinA * cosA * mpq;
            M[q, q] = sinA * sinA * mpp + cosA * cosA * mqq + 2 * sinA * cosA * mpq;
            M[p, q] = M[q, p] = (cosA * cosA - sinA * sinA) * mpq + sinA * cosA * (mpp - mqq);

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;
                double mrp = M[r, p];
                double mrq = M[r, q];
                M[r, p] = M[p, r] = cosA * mrp - sinA * mrq;
                M[r, q] = M[q, r] = sinA * mrp + cosA * mrq;
            }

            // 更新特征向量
            for (int r = 0; r < 3; r++)
            {
                double vrp = eigenvectors[r, p];
                double vrq = eigenvectors[r, q];
                eigenvectors[r, p] = cosA * vrp - sinA * vrq;
                eigenvectors[r, q] = sinA * vrp + cosA * vrq;
            }
        }

        // 提取特征值
        eigenvalues[0] = M[0, 0];
        eigenvalues[1] = M[1, 1];
        eigenvalues[2] = M[2, 2];

        // 找最小特征值的索引
        int minIdx = 0;
        if (eigenvalues[1] < eigenvalues[minIdx]) minIdx = 1;
        if (eigenvalues[2] < eigenvalues[minIdx]) minIdx = 2;

        // 取最小特征值对应的特征向量作为法向量
        double a = eigenvectors[0, minIdx];
        double b = eigenvectors[1, minIdx];
        double c = eigenvectors[2, minIdx];

        // 确保法向量方向一致（向上为正）
        double norm = Math.Sqrt(a * a + b * b + c * c);
        a /= norm;
        b /= norm;
        c /= norm;

        if (c < 0) { a = -a; b = -b; c = -c; }

        double d = -(a * cx + b * cy + c * cz);

        return new[] { a, b, c, d };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}