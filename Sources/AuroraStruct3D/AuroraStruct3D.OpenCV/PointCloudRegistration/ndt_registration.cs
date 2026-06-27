using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 工作流算子：NDT（正态分布变换）点云配准。
/// <para>
/// 将目标点云体素化，每个体素用其内部点的均值与协方差描述为一个 3D 正态分布；
/// 配准时将源点云在当前位姿下落入各体素，以所有源点的分布概率之和作为得分，
/// 通过阻尼牛顿法迭代优化 6 自由度位姿（旋转向量 + 平移）使总得分最大化。
/// 相比 ICP，NDT 无需显式点对点最近邻，对初始位姿差异和噪声更鲁棒，常用于较粗位姿下的配准/精化。
/// </para>
/// <para>
/// 优化以单位变换为起点，因此若初始位姿差异很大，建议先用<b>特征粗配准</b>再接 NDT。
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
[Guid("b1a10007-0007-4000-8000-000000000037")]
[Category("3D配准")]
[DisplayName("NDT配准")]
[Description("正态分布方式对齐，初值不太准、噪声大时更扛造。")]
public class ndt_registration : IOperator
{
    private const int MinPointsPerVoxel = 5;
    private const int MaxScorePoints = 2000;

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
                Name = "resolution",
                DisplayName = "体素分辨率",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxIterations",
                DisplayName = "最大迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "35",
                ValueLimit = new[] { "10", "20", "35", "50", "100" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "stepSize",
                DisplayName = "牛顿步长上限",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
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

    private readonly double _resolution;
    private readonly int _maxIterations;
    private readonly double _stepSize;
    private readonly double _transformationEpsilon;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ndt_registration(
        double resolution = 1.0,
        int maxIterations = 35,
        double stepSize = 0.1,
        double transformationEpsilon = 1e-8
    )
    {
        _resolution = resolution;
        _maxIterations = maxIterations;
        _stepSize = stepSize;
        _transformationEpsilon = transformationEpsilon;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_resolution <= 0)
            throw new InvalidOperationException("体素分辨率必须为正数。");

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
            throw new InvalidOperationException("源点云为空，无法执行 NDT 配准。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法执行 NDT 配准。");

        int sourceCount = sourceCloud.Rows;
        double[] srcX = new double[sourceCount],
            srcY = new double[sourceCount],
            srcZ = new double[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            srcX[i] = sourceCloud.Get<float>(i, 0);
            srcY[i] = sourceCloud.Get<float>(i, 1);
            srcZ[i] = sourceCloud.Get<float>(i, 2);
        }

        // 目标体素化为高斯分布
        var voxels = BuildVoxels(targetCloud);
        if (voxels.Count == 0)
            throw new InvalidOperationException(
                "目标点云体素化后无有效体素（每体素需至少 5 点），请增大体素分辨率。"
            );

        // 用于评分的源点子采样（控制计算量）
        int[] scoreIdx = UniformSample(sourceCount, MaxScorePoints);

        // 阻尼牛顿法优化 6 自由度 p = [tx,ty,tz, rx,ry,rz]
        double[] p = new double[6];
        double[] hStep = { 1e-3, 1e-3, 1e-3, 1e-4, 1e-4, 1e-4 };
        double prevScore = ScoreFromParams(p, srcX, srcY, srcZ, scoreIdx, voxels);
        int actualIterations = 0;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            actualIterations = iter + 1;

            double[] g = NumericalGradient(p, hStep, srcX, srcY, srcZ, scoreIdx, voxels);
            double[,] H = NumericalHessian(p, hStep, srcX, srcY, srcZ, scoreIdx, voxels);

            // 最大化：阻尼牛顿 (H - μI) Δ = -g（μ 使方向上升），失败回退到梯度上升
            double[] delta = SolveNewtonStep(H, g);

            double gradNorm = Math.Sqrt(g.Sum(v => v * v));
            if (delta is null || gradNorm < 1e-12)
            {
                if (gradNorm < 1e-12)
                    break;
                delta = g; // 梯度方向
            }

            // 限制步长
            double dnorm = Math.Sqrt(delta.Sum(v => v * v));
            if (dnorm > _stepSize)
                for (int k = 0; k < 6; k++)
                    delta[k] *= _stepSize / dnorm;

            // 回溯线搜索，保证得分上升
            double bestScore = prevScore;
            double[] bestP = (double[])p.Clone();
            double scale = 1.0;
            bool improved = false;
            for (int ls = 0; ls < 10; ls++)
            {
                double[] trial = new double[6];
                for (int k = 0; k < 6; k++)
                    trial[k] = p[k] + scale * delta[k];
                double sc = ScoreFromParams(trial, srcX, srcY, srcZ, scoreIdx, voxels);
                if (sc > bestScore)
                {
                    bestScore = sc;
                    bestP = trial;
                    improved = true;
                    break;
                }
                scale *= 0.5;
            }

            if (!improved)
                break;

            double change = 0;
            for (int k = 0; k < 6; k++)
                change += Math.Abs(bestP[k] - p[k]);

            p = bestP;
            prevScore = bestScore;

            if (change < _transformationEpsilon)
                break;
        }

        // 最终变换
        double[] R = RodriguesToMatrix(p[3], p[4], p[5]);
        double[] t = { p[0], p[1], p[2] };

        Mat alignedCloud = new Mat(sourceCount, 3, MatType.CV_32FC1);
        for (int i = 0; i < sourceCount; i++)
        {
            double x = srcX[i],
                y = srcY[i],
                z = srcZ[i];
            alignedCloud.Set(i, 0, (float)(R[0] * x + R[1] * y + R[2] * z + t[0]));
            alignedCloud.Set(i, 1, (float)(R[3] * x + R[4] * y + R[5] * z + t[1]));
            alignedCloud.Set(i, 2, (float)(R[6] * x + R[7] * y + R[8] * z + t[2]));
        }
        var outputCloud = new PointCloudData { Value = alignedCloud };

        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, R[r * 3 + c]);
            transformMatrix.Set(r, 3, t[r]);
        }

        var stats = new NdtStats
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCloud.Rows,
            VoxelCount = voxels.Count,
            Iterations = actualIterations,
            FinalScore = prevScore,
            Resolution = _resolution,
        };

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("stats_json", JsonSerializer.Serialize(stats, JsonOptions));
    }

    // ── 体素化 ────────────────────────────────────────────────────────────

    private readonly struct Voxel
    {
        public readonly double Mx,
            My,
            Mz; // 均值
        public readonly double[] InvCov; // 行主序 3×3 协方差逆

        public Voxel(double mx, double my, double mz, double[] invCov)
        {
            Mx = mx;
            My = my;
            Mz = mz;
            InvCov = invCov;
        }
    }

    private Dictionary<(int, int, int), Voxel> BuildVoxels(Mat target)
    {
        int n = target.Rows;
        double inv = 1.0 / _resolution;

        var acc = new Dictionary<(int, int, int), Accum>();
        for (int i = 0; i < n; i++)
        {
            double x = target.Get<float>(i, 0);
            double y = target.Get<float>(i, 1);
            double z = target.Get<float>(i, 2);
            var key = ((int)Math.Floor(x * inv), (int)Math.Floor(y * inv), (int)Math.Floor(z * inv));
            if (!acc.TryGetValue(key, out var a))
            {
                a = new Accum();
                acc[key] = a;
            }
            a.Add(x, y, z);
        }

        var result = new Dictionary<(int, int, int), Voxel>();
        foreach (var (key, a) in acc)
        {
            if (a.Count < MinPointsPerVoxel)
                continue;
            if (a.TryFinalize(out double mx, out double my, out double mz, out double[] invCov))
                result[key] = new Voxel(mx, my, mz, invCov);
        }
        return result;
    }

    private sealed class Accum
    {
        public int Count;
        private double _sx,
            _sy,
            _sz;
        private double _sxx,
            _sxy,
            _sxz,
            _syy,
            _syz,
            _szz;

        public void Add(double x, double y, double z)
        {
            Count++;
            _sx += x;
            _sy += y;
            _sz += z;
            _sxx += x * x;
            _sxy += x * y;
            _sxz += x * z;
            _syy += y * y;
            _syz += y * z;
            _szz += z * z;
        }

        public bool TryFinalize(
            out double mx,
            out double my,
            out double mz,
            out double[] invCov
        )
        {
            mx = _sx / Count;
            my = _sy / Count;
            mz = _sz / Count;

            double c00 = _sxx / Count - mx * mx;
            double c01 = _sxy / Count - mx * my;
            double c02 = _sxz / Count - mx * mz;
            double c11 = _syy / Count - my * my;
            double c12 = _syz / Count - my * mz;
            double c22 = _szz / Count - mz * mz;

            // 正则化：避免接近奇异（薄片/共线体素）
            double scale = (c00 + c11 + c22) / 3.0;
            double reg = Math.Max(scale * 1e-3, 1e-9);
            c00 += reg;
            c11 += reg;
            c22 += reg;

            invCov = Inverse3x3(c00, c01, c02, c11, c12, c22);
            return invCov is not null;
        }
    }

    /// <summary>对称 3×3 矩阵求逆（行主序 9 元）；不可逆返回 null。</summary>
    private static double[] Inverse3x3(
        double a,
        double b,
        double c,
        double d,
        double e,
        double f
    )
    {
        // 矩阵 [[a,b,c],[b,d,e],[c,e,f]]
        double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
        if (Math.Abs(det) < 1e-18)
            return null!;
        double invDet = 1.0 / det;

        double i00 = (d * f - e * e) * invDet;
        double i01 = (c * e - b * f) * invDet;
        double i02 = (b * e - c * d) * invDet;
        double i11 = (a * f - c * c) * invDet;
        double i12 = (b * c - a * e) * invDet;
        double i22 = (a * d - b * b) * invDet;

        return new[] { i00, i01, i02, i01, i11, i12, i02, i12, i22 };
    }

    // ── 评分与优化 ────────────────────────────────────────────────────────

    private double ScoreFromParams(
        double[] p,
        double[] srcX,
        double[] srcY,
        double[] srcZ,
        int[] idx,
        Dictionary<(int, int, int), Voxel> voxels
    )
    {
        double[] R = RodriguesToMatrix(p[3], p[4], p[5]);
        double tx = p[0],
            ty = p[1],
            tz = p[2];
        double inv = 1.0 / _resolution;

        double score = 0;
        foreach (int i in idx)
        {
            double x = srcX[i],
                y = srcY[i],
                z = srcZ[i];
            double px = R[0] * x + R[1] * y + R[2] * z + tx;
            double py = R[3] * x + R[4] * y + R[5] * z + ty;
            double pz = R[6] * x + R[7] * y + R[8] * z + tz;

            var key = (
                (int)Math.Floor(px * inv),
                (int)Math.Floor(py * inv),
                (int)Math.Floor(pz * inv)
            );
            if (!voxels.TryGetValue(key, out var v))
                continue;

            double dx = px - v.Mx,
                dy = py - v.My,
                dz = pz - v.Mz;
            double[] m = v.InvCov;
            double q =
                dx * (m[0] * dx + m[1] * dy + m[2] * dz)
                + dy * (m[3] * dx + m[4] * dy + m[5] * dz)
                + dz * (m[6] * dx + m[7] * dy + m[8] * dz);
            score += Math.Exp(-0.5 * q);
        }
        return score;
    }

    private double[] NumericalGradient(
        double[] p,
        double[] h,
        double[] srcX,
        double[] srcY,
        double[] srcZ,
        int[] idx,
        Dictionary<(int, int, int), Voxel> voxels
    )
    {
        double[] g = new double[6];
        for (int k = 0; k < 6; k++)
        {
            double[] pp = (double[])p.Clone();
            double[] pm = (double[])p.Clone();
            pp[k] += h[k];
            pm[k] -= h[k];
            double sp = ScoreFromParams(pp, srcX, srcY, srcZ, idx, voxels);
            double sm = ScoreFromParams(pm, srcX, srcY, srcZ, idx, voxels);
            g[k] = (sp - sm) / (2 * h[k]);
        }
        return g;
    }

    private double[,] NumericalHessian(
        double[] p,
        double[] h,
        double[] srcX,
        double[] srcY,
        double[] srcZ,
        int[] idx,
        Dictionary<(int, int, int), Voxel> voxels
    )
    {
        double[,] H = new double[6, 6];
        double f0 = ScoreFromParams(p, srcX, srcY, srcZ, idx, voxels);

        for (int j = 0; j < 6; j++)
        {
            for (int k = j; k < 6; k++)
            {
                double val;
                if (j == k)
                {
                    double[] pp = (double[])p.Clone();
                    double[] pm = (double[])p.Clone();
                    pp[j] += h[j];
                    pm[j] -= h[j];
                    double sp = ScoreFromParams(pp, srcX, srcY, srcZ, idx, voxels);
                    double sm = ScoreFromParams(pm, srcX, srcY, srcZ, idx, voxels);
                    val = (sp - 2 * f0 + sm) / (h[j] * h[j]);
                }
                else
                {
                    double[] ppp = (double[])p.Clone();
                    double[] ppm = (double[])p.Clone();
                    double[] pmp = (double[])p.Clone();
                    double[] pmm = (double[])p.Clone();
                    ppp[j] += h[j];
                    ppp[k] += h[k];
                    ppm[j] += h[j];
                    ppm[k] -= h[k];
                    pmp[j] -= h[j];
                    pmp[k] += h[k];
                    pmm[j] -= h[j];
                    pmm[k] -= h[k];
                    double s1 = ScoreFromParams(ppp, srcX, srcY, srcZ, idx, voxels);
                    double s2 = ScoreFromParams(ppm, srcX, srcY, srcZ, idx, voxels);
                    double s3 = ScoreFromParams(pmp, srcX, srcY, srcZ, idx, voxels);
                    double s4 = ScoreFromParams(pmm, srcX, srcY, srcZ, idx, voxels);
                    val = (s1 - s2 - s3 + s4) / (4 * h[j] * h[k]);
                }
                H[j, k] = val;
                H[k, j] = val;
            }
        }
        return H;
    }

    /// <summary>
    /// 求阻尼牛顿上升步：对最大化问题，求解 (-H + μI) Δ = g，使 Δ 为上升方向。
    /// 失败返回 null。
    /// </summary>
    private static double[] SolveNewtonStep(double[,] H, double[] g)
    {
        // 最大化 → 期望 H 负定。构造 A = -H + μI（正定），解 A Δ = g。
        double mu = 1e-3;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using Mat A = new Mat(6, 6, MatType.CV_64FC1);
            using Mat b = new Mat(6, 1, MatType.CV_64FC1);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 6; c++)
                    A.Set(r, c, -H[r, c] + (r == c ? mu : 0));
                b.Set(r, 0, g[r]);
            }

            using Mat sol = new Mat();
            if (Cv2.Solve(A, b, sol, DecompTypes.Cholesky))
            {
                double[] delta = new double[6];
                for (int i = 0; i < 6; i++)
                    delta[i] = sol.Get<double>(i, 0);
                // 必须是上升方向（g·Δ > 0）
                double dot = 0;
                for (int i = 0; i < 6; i++)
                    dot += g[i] * delta[i];
                if (dot > 0)
                    return delta;
            }
            mu *= 10;
        }
        return null!;
    }

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

    private static int[] UniformSample(int n, int count)
    {
        if (count >= n)
            return Enumerable.Range(0, n).ToArray();
        int[] result = new int[count];
        double step = (double)n / count;
        for (int i = 0; i < count; i++)
            result[i] = Math.Min(n - 1, (int)(i * step));
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>NDT 配准统计信息。</summary>
    public class NdtStats
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public int VoxelCount { get; set; }
        public int Iterations { get; set; }
        public double FinalScore { get; set; }
        public double Resolution { get; set; }
    }
}
