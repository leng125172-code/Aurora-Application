using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 工作流算子：ICP 点云配准。
/// <para>
/// 使用迭代最近点（ICP）算法将源点云配准到目标点云，找到最优刚性变换（旋转 + 平移）。
/// 每次迭代先通过空间哈希网格搜索最近邻对应点对，再通过 SVD 分解计算最优刚性变换，
/// 将变换累积应用到源点云，直到收敛或达到最大迭代次数。
/// 适用于 3D 缺陷检测中的模板对齐、多视角点云融合等场景。
/// </para>
/// <para>
/// 算法流程：
/// <list type="number">
///   <item>构建目标点云的空间哈希网格，用于加速最近邻搜索</item>
///   <item>每次迭代：对源点云每个点，搜索目标点云中的最近邻</item>
///   <item>剔除距离超过阈值的对应点对</item>
///   <item>通过 SVD 分解计算最优旋转矩阵 R 和平移向量 t</item>
///   <item>将变换应用到源点云，累积变换矩阵</item>
///   <item>计算 RMSE，若变化量小于收敛阈值则停止</item>
///   <item>输出配准后的点云、变换矩阵和统计信息</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>source_cloud</c>（PointCloudData）— 待配准的源点云</item>
///   <item>输入 <c>target_cloud</c>（PointCloudData）— 目标点云（参考点云）</item>
///   <item>输出 <c>aligned_cloud</c>（PointCloudData）— 配准后的点云</item>
///   <item>输出 <c>transform_matrix</c>（Mat）— 4×4 累积变换矩阵（CV_64FC1）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0008-4000-8000-000000000029")]
[Category("3D配准")]
[DisplayName("ICP配准")]
[Description("把两片点云对齐贴合，点到点迭代，做拼接和模板比对用。")]
public class icp_registration : IOperator
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化 ICP 点云配准算子。
    /// </summary>
    /// <param name="maxIterations">最大迭代次数。</param>
    /// <param name="maxCorrespondenceDistance">最大对应点距离，超过此值的点对将被剔除。</param>
    /// <param name="rmseThreshold">RMSE 收敛阈值，当 RMSE 变化量小于此值时停止迭代。</param>
    /// <param name="transformationEpsilon">变换收敛阈值，当变换矩阵变化量小于此值时停止迭代。</param>
    public icp_registration(
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

    /// <inheritdoc/>
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
            throw new InvalidOperationException("源点云为空，无法执行 ICP 配准。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法执行 ICP 配准。");

        int sourceCount = sourceCloud.Rows;
        int targetCount = targetCloud.Rows;

        // 提取源点云 XYZ 坐标
        float[] srcX = new float[sourceCount];
        float[] srcY = new float[sourceCount];
        float[] srcZ = new float[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            srcX[i] = sourceCloud.Get<float>(i, 0);
            srcY[i] = sourceCloud.Get<float>(i, 1);
            srcZ[i] = sourceCloud.Get<float>(i, 2);
        }

        // 提取目标点云 XYZ 坐标
        float[] tgtX = new float[targetCount];
        float[] tgtY = new float[targetCount];
        float[] tgtZ = new float[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            tgtX[i] = targetCloud.Get<float>(i, 0);
            tgtY[i] = targetCloud.Get<float>(i, 1);
            tgtZ[i] = targetCloud.Get<float>(i, 2);
        }

        // 构建目标点云空间哈希网格
        var targetGrid = new SpatialHashGrid(
            tgtX,
            tgtY,
            tgtZ,
            _maxCorrespondenceDistance,
            targetCount
        );

        // 累积变换矩阵（初始化为 4×4 单位阵）
        double[] accumR = { 1, 0, 0, 0, 1, 0, 0, 0, 1 }; // 3×3 旋转
        double[] accumT = { 0, 0, 0 }; // 平移

        double prevRmse = double.MaxValue;
        int actualIterations = 0;
        List<double> rmseHistory = new List<double>();

        double maxCorrDistSq = _maxCorrespondenceDistance * _maxCorrespondenceDistance;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            actualIterations = iter + 1;

            // ① 搜索最近邻对应点对
            List<(int srcIdx, int tgtIdx)> correspondences = new List<(int, int)>();
            for (int i = 0; i < sourceCount; i++)
            {
                int nearest = targetGrid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out _);

                if (nearest < 0)
                    continue;

                float dx = srcX[i] - tgtX[nearest];
                float dy = srcY[i] - tgtY[nearest];
                float dz = srcZ[i] - tgtZ[nearest];
                double distSq = dx * dx + dy * dy + dz * dz;

                if (distSq <= maxCorrDistSq)
                    correspondences.Add((i, nearest));
            }

            if (correspondences.Count < 3)
                throw new InvalidOperationException(
                    $"有效对应点对不足（{correspondences.Count} < 3），无法计算刚性变换。请增大最大对应点距离或检查输入点云。"
                );

            int corrCount = correspondences.Count;

            // ② 计算质心
            double srcCx = 0,
                srcCy = 0,
                srcCz = 0;
            double tgtCx = 0,
                tgtCy = 0,
                tgtCz = 0;
            foreach (var (si, ti) in correspondences)
            {
                srcCx += srcX[si];
                srcCy += srcY[si];
                srcCz += srcZ[si];
                tgtCx += tgtX[ti];
                tgtCy += tgtY[ti];
                tgtCz += tgtZ[ti];
            }
            srcCx /= corrCount;
            srcCy /= corrCount;
            srcCz /= corrCount;
            tgtCx /= corrCount;
            tgtCy /= corrCount;
            tgtCz /= corrCount;

            // ③ 构建 3×3 互协方差矩阵 H
            double h00 = 0,
                h01 = 0,
                h02 = 0;
            double h10 = 0,
                h11 = 0,
                h12 = 0;
            double h20 = 0,
                h21 = 0,
                h22 = 0;

            foreach (var (si, ti) in correspondences)
            {
                double sdx = srcX[si] - srcCx;
                double sdy = srcY[si] - srcCy;
                double sdz = srcZ[si] - srcCz;
                double tdx = tgtX[ti] - tgtCx;
                double tdy = tgtY[ti] - tgtCy;
                double tdz = tgtZ[ti] - tgtCz;

                h00 += sdx * tdx;
                h01 += sdx * tdy;
                h02 += sdx * tdz;
                h10 += sdy * tdx;
                h11 += sdy * tdy;
                h12 += sdy * tdz;
                h20 += sdz * tdx;
                h21 += sdz * tdy;
                h22 += sdz * tdz;
            }

            // ④ SVD 分解 H = U * Σ * V^T，计算 R = V * U^T
            double[] R = Math3D.RotationFromCrossCovariance(h00, h01, h02, h10, h11, h12, h20, h21, h22);

            // ⑤ 计算平移向量
            double[] t = new double[3];
            t[0] = tgtCx - (R[0] * srcCx + R[1] * srcCy + R[2] * srcCz);
            t[1] = tgtCy - (R[3] * srcCx + R[4] * srcCy + R[5] * srcCz);
            t[2] = tgtCz - (R[6] * srcCx + R[7] * srcCy + R[8] * srcCz);

            // ⑥ 应用变换到源点云
            for (int i = 0; i < sourceCount; i++)
            {
                double x = srcX[i];
                double y = srcY[i];
                double z = srcZ[i];
                srcX[i] = (float)(R[0] * x + R[1] * y + R[2] * z + t[0]);
                srcY[i] = (float)(R[3] * x + R[4] * y + R[5] * z + t[1]);
                srcZ[i] = (float)(R[6] * x + R[7] * y + R[8] * z + t[2]);
            }

            // ⑦ 累积变换矩阵
            double[] newAccumR = new double[9];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    newAccumR[r * 3 + c] =
                        R[r * 3 + 0] * accumR[0 * 3 + c]
                        + R[r * 3 + 1] * accumR[1 * 3 + c]
                        + R[r * 3 + 2] * accumR[2 * 3 + c];
                }
            }
            double[] newAccumT = new double[3];
            newAccumT[0] = R[0] * accumT[0] + R[1] * accumT[1] + R[2] * accumT[2] + t[0];
            newAccumT[1] = R[3] * accumT[0] + R[4] * accumT[1] + R[5] * accumT[2] + t[1];
            newAccumT[2] = R[6] * accumT[0] + R[7] * accumT[1] + R[8] * accumT[2] + t[2];

            accumR = newAccumR;
            accumT = newAccumT;

            // ⑧ 计算 RMSE
            double rmse = 0;
            foreach (var (si, ti) in correspondences)
            {
                float dx = srcX[si] - tgtX[ti];
                float dy = srcY[si] - tgtY[ti];
                float dz = srcZ[si] - tgtZ[ti];
                rmse += dx * dx + dy * dy + dz * dz;
            }
            rmse = Math.Sqrt(rmse / corrCount);
            rmseHistory.Add(rmse);

            // ⑨ 检查收敛
            double rmseChange = Math.Abs(prevRmse - rmse);
            double transformChange =
                Math.Abs(t[0])
                + Math.Abs(t[1])
                + Math.Abs(t[2])
                + Math.Abs(R[0] - 1)
                + Math.Abs(R[4] - 1)
                + Math.Abs(R[8] - 1);

            prevRmse = rmse;

            if (rmseChange < _rmseThreshold && transformChange < _transformationEpsilon)
                break;
        }

        // 构建输出点云
        Mat alignedCloud = new Mat(sourceCount, 3, MatType.CV_32FC1);
        for (int i = 0; i < sourceCount; i++)
        {
            alignedCloud.Set(i, 0, srcX[i]);
            alignedCloud.Set(i, 1, srcY[i]);
            alignedCloud.Set(i, 2, srcZ[i]);
        }

        var outputCloud = new PointCloudData();
        outputCloud.Value = alignedCloud;

        // 构建 4×4 变换矩阵
        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, accumR[r * 3 + c]);
            transformMatrix.Set(r, 3, accumT[r]);
        }

        // 统计信息
        double rotationAngle = ComputeRotationAngle(accumR);
        double translationMagnitude = Math.Sqrt(
            accumT[0] * accumT[0] + accumT[1] * accumT[1] + accumT[2] * accumT[2]
        );

        var stats = new IcpStats
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCount,
            Iterations = actualIterations,
            FinalRmse = prevRmse,
            RotationAngleDeg = rotationAngle * 180.0 / Math.PI,
            TranslationMagnitude = translationMagnitude,
            RotationMatrix = new double[]
            {
                accumR[0],
                accumR[1],
                accumR[2],
                accumR[3],
                accumR[4],
                accumR[5],
                accumR[6],
                accumR[7],
                accumR[8],
            },
            TranslationVector = new double[] { accumT[0], accumT[1], accumT[2] },
            MaxCorrespondenceDistance = _maxCorrespondenceDistance,
            RmseHistory = rmseHistory,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("stats_json", statsJson);
    }

    /// <summary>
    /// 从旋转矩阵计算旋转角度（罗德里格斯公式）。
    /// </summary>
    private static double ComputeRotationAngle(double[] R)
    {
        double trace = R[0] + R[4] + R[8];
        double cosTheta = (trace - 1) / 2.0;
        cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
        return Math.Acos(cosTheta);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────────

    /// <summary>ICP 配准统计信息。</summary>
    public class IcpStats
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public int Iterations { get; set; }
        public double FinalRmse { get; set; }
        public double RotationAngleDeg { get; set; }
        public double TranslationMagnitude { get; set; }
        public double[] RotationMatrix { get; set; } = Array.Empty<double>();
        public double[] TranslationVector { get; set; } = Array.Empty<double>();
        public double MaxCorrespondenceDistance { get; set; }
        public List<double> RmseHistory { get; set; } = new();
    }
}
