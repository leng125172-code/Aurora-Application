using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 工作流算子：点到面 ICP 点云配准。
/// <para>
/// 相比经典点到点 ICP，点到面 ICP 最小化源点到目标局部切平面的距离，
/// 利用目标点云法向量信息，通常收敛更快、精度更高，尤其适合平面/曲面丰富的工件。
/// 每次迭代通过空间哈希网格搜索最近邻，构建 6×6 线性最小二乘（小角度线性化旋转）求解增量变换。
/// </para>
/// <para>
/// 若目标点云不含法向量列（列数 &lt; 6），将自动用 K 近邻 PCA 估计法向量。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>source_cloud</c>（PointCloudData）— 待配准源点云</item>
///   <item>输入 <c>target_cloud</c>（PointCloudData）— 目标点云（参考）</item>
///   <item>输出 <c>aligned_cloud</c>（PointCloudData）— 配准后的源点云</item>
///   <item>输出 <c>transform_matrix</c>（Mat）— 4×4 累积变换矩阵（CV_64FC1）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10006-0006-4000-8000-000000000036")]
[Category("3D配准")]
[DisplayName("点到面ICP")]
[Description("点对着面来对齐，收敛更快更准，平面/曲面多的工件首选。")]
public class icp_point_to_plane : IOperator
{
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
                Name = "maxIterations",
                DisplayName = "最大迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "50",
                ValueLimit = new[] { "10", "20", "50", "100", "200" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxCorrespondenceDistance",
                DisplayName = "最大对应点距离",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "rmseThreshold",
                DisplayName = "RMSE 收敛阈值",
                ParameterType = typeof(double),
                DefaultValue = "1e-6",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "transformationEpsilon",
                DisplayName = "变换收敛阈值",
                ParameterType = typeof(double),
                DefaultValue = "1e-8",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _maxIterations;
    private readonly double _maxCorrespondenceDistance;
    private readonly double _rmseThreshold;
    private readonly double _transformationEpsilon;
    private bool _disposed;

    private const int NormalEstimationK = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public icp_point_to_plane(
        int maxIterations = 50,
        double maxCorrespondenceDistance = 0.1,
        double rmseThreshold = 1e-6,
        double transformationEpsilon = 1e-8
    )
    {
        _maxIterations = maxIterations;
        _maxCorrespondenceDistance = maxCorrespondenceDistance;
        _rmseThreshold = rmseThreshold;
        _transformationEpsilon = transformationEpsilon;
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
            throw new InvalidOperationException("源点云为空，无法执行点到面 ICP。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法执行点到面 ICP。");

        int sourceCount = sourceCloud.Rows;
        int targetCount = targetCloud.Rows;

        float[] srcX = new float[sourceCount],
            srcY = new float[sourceCount],
            srcZ = new float[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            srcX[i] = sourceCloud.Get<float>(i, 0);
            srcY[i] = sourceCloud.Get<float>(i, 1);
            srcZ[i] = sourceCloud.Get<float>(i, 2);
        }

        float[] tgtX = new float[targetCount],
            tgtY = new float[targetCount],
            tgtZ = new float[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            tgtX[i] = targetCloud.Get<float>(i, 0);
            tgtY[i] = targetCloud.Get<float>(i, 1);
            tgtZ[i] = targetCloud.Get<float>(i, 2);
        }

        // 目标法向量：读取或估计
        double[] tgtNx = new double[targetCount],
            tgtNy = new double[targetCount],
            tgtNz = new double[targetCount];
        if (targetCloud.Cols >= 6)
        {
            for (int i = 0; i < targetCount; i++)
            {
                tgtNx[i] = targetCloud.Get<float>(i, 3);
                tgtNy[i] = targetCloud.Get<float>(i, 4);
                tgtNz[i] = targetCloud.Get<float>(i, 5);
            }
        }
        else
        {
            using Mat normals = PointCloudUtils.EstimateNormals(targetCloud, NormalEstimationK);
            for (int i = 0; i < targetCount; i++)
            {
                tgtNx[i] = normals.Get<float>(i, 0);
                tgtNy[i] = normals.Get<float>(i, 1);
                tgtNz[i] = normals.Get<float>(i, 2);
            }
        }

        var grid = new SpatialHashGrid(tgtX, tgtY, tgtZ, _maxCorrespondenceDistance, targetCount);

        double[] accumR = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] accumT = { 0, 0, 0 };

        double maxCorrDistSq = _maxCorrespondenceDistance * _maxCorrespondenceDistance;
        double prevRmse = double.MaxValue;
        int actualIterations = 0;
        var rmseHistory = new List<double>();

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            actualIterations = iter + 1;

            // 6×6 正规方程 A·x = b（x = [α,β,γ, tx,ty,tz]）
            double[,] A = new double[6, 6];
            double[] b = new double[6];
            int corrCount = 0;

            for (int i = 0; i < sourceCount; i++)
            {
                int nn = grid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out double distSq);
                if (nn < 0 || distSq > maxCorrDistSq)
                    continue;

                corrCount++;
                double sx = srcX[i],
                    sy = srcY[i],
                    sz = srcZ[i];
                double qx = tgtX[nn],
                    qy = tgtY[nn],
                    qz = tgtZ[nn];
                double nx = tgtNx[nn],
                    ny = tgtNy[nn],
                    nz = tgtNz[nn];

                // 雅可比行 a = [ s×n , n ]，残差 e = (s − q)·n
                double cx = sy * nz - sz * ny;
                double cy = sz * nx - sx * nz;
                double cz = sx * ny - sy * nx;
                double[] a = { cx, cy, cz, nx, ny, nz };
                double e = (sx - qx) * nx + (sy - qy) * ny + (sz - qz) * nz;

                for (int r = 0; r < 6; r++)
                {
                    for (int c = 0; c < 6; c++)
                        A[r, c] += a[r] * a[c];
                    b[r] += -a[r] * e;
                }
            }

            if (corrCount < 6)
                throw new InvalidOperationException(
                    $"有效对应点对不足（{corrCount} < 6），无法求解点到面增量变换。请增大最大对应点距离或检查输入点云。"
                );

            double[] x = Solve6(A, b);

            // 增量旋转（旋转向量 → 矩阵）+ 平移
            double[] Rinc = RodriguesToMatrix(x[0], x[1], x[2]);
            double[] tinc = { x[3], x[4], x[5] };

            // 应用到源点云
            for (int i = 0; i < sourceCount; i++)
            {
                double px = srcX[i],
                    py = srcY[i],
                    pz = srcZ[i];
                srcX[i] = (float)(Rinc[0] * px + Rinc[1] * py + Rinc[2] * pz + tinc[0]);
                srcY[i] = (float)(Rinc[3] * px + Rinc[4] * py + Rinc[5] * pz + tinc[1]);
                srcZ[i] = (float)(Rinc[6] * px + Rinc[7] * py + Rinc[8] * pz + tinc[2]);
            }

            // 累积变换
            double[] newR = new double[9];
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                newR[r * 3 + c] =
                    Rinc[r * 3 + 0] * accumR[0 * 3 + c]
                    + Rinc[r * 3 + 1] * accumR[1 * 3 + c]
                    + Rinc[r * 3 + 2] * accumR[2 * 3 + c];
            double[] newT =
            {
                Rinc[0] * accumT[0] + Rinc[1] * accumT[1] + Rinc[2] * accumT[2] + tinc[0],
                Rinc[3] * accumT[0] + Rinc[4] * accumT[1] + Rinc[5] * accumT[2] + tinc[1],
                Rinc[6] * accumT[0] + Rinc[7] * accumT[1] + Rinc[8] * accumT[2] + tinc[2],
            };
            accumR = newR;
            accumT = newT;

            // 点到面 RMSE（更新后重新计算残差）
            double sumSq = 0;
            int cnt = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                int nn = grid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out double distSq);
                if (nn < 0 || distSq > maxCorrDistSq)
                    continue;
                double e =
                    (srcX[i] - tgtX[nn]) * tgtNx[nn]
                    + (srcY[i] - tgtY[nn]) * tgtNy[nn]
                    + (srcZ[i] - tgtZ[nn]) * tgtNz[nn];
                sumSq += e * e;
                cnt++;
            }
            double rmse = cnt > 0 ? Math.Sqrt(sumSq / cnt) : 0;
            rmseHistory.Add(rmse);

            double rmseChange = Math.Abs(prevRmse - rmse);
            double transformChange =
                Math.Abs(tinc[0]) + Math.Abs(tinc[1]) + Math.Abs(tinc[2]) + Math.Abs(x[0]) + Math.Abs(x[1]) + Math.Abs(x[2]);
            prevRmse = rmse;

            if (rmseChange < _rmseThreshold && transformChange < _transformationEpsilon)
                break;
        }

        // 输出点云
        Mat alignedCloud = new Mat(sourceCount, 3, MatType.CV_32FC1);
        for (int i = 0; i < sourceCount; i++)
        {
            alignedCloud.Set(i, 0, srcX[i]);
            alignedCloud.Set(i, 1, srcY[i]);
            alignedCloud.Set(i, 2, srcZ[i]);
        }
        var outputCloud = new PointCloudData { Value = alignedCloud };

        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, accumR[r * 3 + c]);
            transformMatrix.Set(r, 3, accumT[r]);
        }

        double trace = accumR[0] + accumR[4] + accumR[8];
        double rotAngle = Math.Acos(Math.Clamp((trace - 1) / 2.0, -1.0, 1.0));
        var stats = new IcpPlaneStats
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCount,
            Iterations = actualIterations,
            FinalRmse = prevRmse,
            RotationAngleDeg = rotAngle * 180.0 / Math.PI,
            TranslationMagnitude = Math.Sqrt(
                accumT[0] * accumT[0] + accumT[1] * accumT[1] + accumT[2] * accumT[2]
            ),
            RmseHistory = rmseHistory,
        };

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("stats_json", JsonSerializer.Serialize(stats, JsonOptions));
    }

    /// <summary>解 6×6 线性方程组 A·x = b（对称正定，用 Cholesky）。</summary>
    private static double[] Solve6(double[,] A, double[] b)
    {
        using Mat matA = new Mat(6, 6, MatType.CV_64FC1);
        using Mat matB = new Mat(6, 1, MatType.CV_64FC1);
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
                matA.Set(r, c, A[r, c]);
            matB.Set(r, 0, b[r]);
        }

        using Mat sol = new Mat();
        // 正规方程可能近奇异，回退到 SVD 保证稳健
        if (!Cv2.Solve(matA, matB, sol, DecompTypes.Cholesky))
        {
            if (!Cv2.Solve(matA, matB, sol, DecompTypes.SVD))
                throw new InvalidOperationException("点到面 ICP 增量方程求解失败。");
        }

        double[] x = new double[6];
        for (int i = 0; i < 6; i++)
            x[i] = sol.Get<double>(i, 0);
        return x;
    }

    /// <summary>旋转向量（轴角）→ 3×3 旋转矩阵（行主序），通过 Rodrigues 公式。</summary>
    private static double[] RodriguesToMatrix(double rx, double ry, double rz)
    {
        using Mat rvec = new Mat(3, 1, MatType.CV_64FC1);
        rvec.Set(0, 0, rx);
        rvec.Set(1, 0, ry);
        rvec.Set(2, 0, rz);
        using Mat rmat = new Mat();
        Cv2.Rodrigues(rvec, rmat);

        double[] R = new double[9];
        for (int r = 0; r < 3; r++)
        for (int c = 0; c < 3; c++)
            R[r * 3 + c] = rmat.Get<double>(r, c);
        return R;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>点到面 ICP 统计信息。</summary>
    public class IcpPlaneStats
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public int Iterations { get; set; }
        public double FinalRmse { get; set; }
        public double RotationAngleDeg { get; set; }
        public double TranslationMagnitude { get; set; }
        public List<double> RmseHistory { get; set; } = new();
    }
}
