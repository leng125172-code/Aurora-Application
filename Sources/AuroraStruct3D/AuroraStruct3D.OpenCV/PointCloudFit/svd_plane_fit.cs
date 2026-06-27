using AuroraStruct3D.OpenCV.Common;

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
[Category("3D拟合测量")]
[DisplayName("SVD拟合")]
[Description("最小二乘把一片点云拟合成平面，噪声均匀时最准。")]
public class svd_plane_fit : IOperator
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
            new PointCloudData()
            {
                ParameterName = "fitted_plane_points",
                DisplayName = "投影点云",
            },
            new MatImg() { ParameterName = "fitting_error", DisplayName = "拟合误差" },
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
        double cx = 0,
            cy = 0,
            cz = 0;
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
        double c00 = 0,
            c01 = 0,
            c02 = 0;
        double c11 = 0,
            c12 = 0;
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
        double[] plane = Math3D.SolvePlaneByCovariance(
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
