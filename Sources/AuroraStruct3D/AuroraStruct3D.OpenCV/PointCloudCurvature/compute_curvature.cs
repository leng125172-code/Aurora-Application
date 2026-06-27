using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudCurvature;

/// <summary>
/// 工作流算子：点云曲率计算。
/// <para>
/// 对点云中每个点，基于局部二次曲面拟合计算高斯曲率和平均曲率。
/// 可接收 <c>compute_normals</c> 输出的法向量以加速计算，也可自行计算法向量。
/// 曲率是 3D 缺陷检测的关键特征——缺陷区域（凹坑、凸起）通常表现为曲率异常。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>对每个点搜索 k 近邻，建立局部坐标系（法向量为 Z 轴）</item>
///   <item>将邻域点变换到局部坐标系，最小二乘拟合二次曲面 z = ax² + bxy + cy² + dx + ey</item>
///   <item>从二次曲面系数计算 Hessian 矩阵，提取主曲率 k₁、k₂</item>
///   <item>高斯曲率 K = k₁·k₂，平均曲率 H = (k₁ + k₂) / 2</item>
///   <item>生成曲率热力图和统计信息</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云（必选）</item>
///   <item>输入 <c>normal_cloud</c>（PointCloudData）— 带法向量的点云，来自 compute_normals（可选）</item>
///   <item>输出 <c>gaussian_curvature_mat</c>（Mat）— 高斯曲率（N×1，CV_64FC1）</item>
///   <item>输出 <c>mean_curvature_mat</c>（Mat）— 平均曲率（N×1，CV_64FC1）</item>
///   <item>输出 <c>curvature_image</c>（Mat）— 曲率可视化热力图（CV_8UC3，伪彩色）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0005-4000-8000-000000000012")]
[Category("3D拟合测量")]
[DisplayName("曲率计算")]
[Description("算每个点弯不弯，找边角、凸起、缺陷用。")]
public class compute_curvature : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new PointCloudData() { ParameterName = "normal_cloud", DisplayName = "法向量点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "gaussian_curvature_mat", DisplayName = "高斯曲率" },
            new MatImg() { ParameterName = "mean_curvature_mat", DisplayName = "平均曲率" },
            new MatImg() { ParameterName = "curvature_image", DisplayName = "曲率图像" },
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
                Name = "knnCount",
                DisplayName = "K 近邻数",
                ParameterType = typeof(int),
                DefaultValue = "20",
                ValueLimit = new[] { "10", "15", "20", "30", "50" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "投影分辨率",
                ParameterType = typeof(int),
                DefaultValue = "512",
                ValueLimit = new[] { "256", "512", "1024", "2048" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _knnCount;
    private readonly int _imageResolution;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化点云曲率计算算子。
    /// </summary>
    /// <param name="knnCount">K 近邻搜索的点数，越大越平滑但细节越少。</param>
    /// <param name="imageResolution">曲率热力图分辨率（边长像素数）。</param>
    public compute_curvature(int knnCount = 20, int imageResolution = 512)
    {
        _knnCount = knnCount;
        _imageResolution = imageResolution;
    }

    /// <inheritdoc/>
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
            throw new InvalidOperationException("输入点云为空，无法计算曲率。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数少于 3，无法计算曲率。");

        int knn = Math.Min(_knnCount, pointCount - 1);
        if (knn < 5)
            throw new InvalidOperationException(
                $"K 近邻数 {knn} 不足，至少需要 5 个邻域点才能拟合二次曲面。"
            );

        // 提取 XYZ 坐标
        float[] xs = new float[pointCount];
        float[] ys = new float[pointCount];
        float[] zs = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
        }

        // 尝试读取已提供的法向量
        float[]? normalsX = null;
        float[]? normalsY = null;
        float[]? normalsZ = null;
        bool hasProvidedNormals = false;

        PointCloudData? normalInput = context.Get<PointCloudData>("normal_cloud");
        if (
            normalInput != null
            && normalInput.PointCloud != null
            && !normalInput.PointCloud.Empty()
        )
        {
            Mat normalCloud = normalInput.PointCloud;
            if (normalCloud.Rows == pointCount && normalCloud.Cols >= 6)
            {
                normalsX = new float[pointCount];
                normalsY = new float[pointCount];
                normalsZ = new float[pointCount];
                for (int i = 0; i < pointCount; i++)
                {
                    normalsX[i] = normalCloud.Get<float>(i, 3);
                    normalsY[i] = normalCloud.Get<float>(i, 4);
                    normalsZ[i] = normalCloud.Get<float>(i, 5);
                }
                hasProvidedNormals = true;
            }
        }

        // 如果没有提供法向量，自行计算
        if (!hasProvidedNormals)
        {
            normalsX = new float[pointCount];
            normalsY = new float[pointCount];
            normalsZ = new float[pointCount];
            ComputeNormalsByPCA(xs, ys, zs, pointCount, knn, normalsX, normalsY, normalsZ);
        }

        // 计算曲率
        double[] gaussianCurvatures = new double[pointCount];
        double[] meanCurvatures = new double[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            // 搜索 k 近邻
            int[] neighbors = FindKNearestNeighbors(xs, ys, zs, i, knn, pointCount);

            // 基于局部二次曲面拟合计算曲率
            double nx = normalsX![i];
            double ny = normalsY![i];
            double nz = normalsZ![i];

            (double gaussian, double mean) = ComputeCurvatureByQuadricFit(
                xs,
                ys,
                zs,
                i,
                neighbors,
                knn,
                nx,
                ny,
                nz
            );

            gaussianCurvatures[i] = gaussian;
            meanCurvatures[i] = mean;
        }

        // 构建输出矩阵
        Mat gaussianMat = new Mat(pointCount, 1, MatType.CV_64FC1);
        Mat meanMat = new Mat(pointCount, 1, MatType.CV_64FC1);
        for (int i = 0; i < pointCount; i++)
        {
            gaussianMat.Set(i, 0, gaussianCurvatures[i]);
            meanMat.Set(i, 0, meanCurvatures[i]);
        }

        // 生成曲率热力图（基于平均曲率）
        Mat curvatureImage = GenerateCurvatureHeatmap(
            xs,
            ys,
            meanCurvatures,
            pointCount,
            _imageResolution
        );

        // 统计信息
        double gaussMax = gaussianCurvatures.Max();
        double gaussMean = gaussianCurvatures.Average();
        double meanMax = meanCurvatures.Max();
        double meanMean = meanCurvatures.Average();

        double meanStd = Math.Sqrt(meanCurvatures.Average(c => (c - meanMean) * (c - meanMean)));

        var stats = new CurvatureStats
        {
            PointCount = pointCount,
            KnnCount = knn,
            GaussianMax = gaussMax,
            GaussianMean = gaussMean,
            MeanCurvatureMax = meanMax,
            MeanCurvatureMean = meanMean,
            MeanCurvatureStd = meanStd,
            HasProvidedNormals = hasProvidedNormals,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("gaussian_curvature_mat", gaussianMat);
        context.Set("mean_curvature_mat", meanMat);
        context.Set("curvature_image", curvatureImage);
        context.Set("stats_json", statsJson);
    }

    // ===== 法向量计算（PCA） =====

    /// <summary>
    /// 使用 PCA 计算每个点的法向量（最小特征值对应的特征向量）。
    /// </summary>
    private static void ComputeNormalsByPCA(
        float[] xs,
        float[] ys,
        float[] zs,
        int pointCount,
        int knn,
        float[] outNx,
        float[] outNy,
        float[] outNz
    )
    {
        for (int i = 0; i < pointCount; i++)
        {
            int[] neighbors = FindKNearestNeighbors(xs, ys, zs, i, knn, pointCount);

            // 计算邻域质心
            double cx = 0,
                cy = 0,
                cz = 0;
            for (int j = 0; j < knn; j++)
            {
                int idx = neighbors[j];
                cx += xs[idx];
                cy += ys[idx];
                cz += zs[idx];
            }
            cx /= knn;
            cy /= knn;
            cz /= knn;

            // 构建协方差矩阵
            double c00 = 0,
                c01 = 0,
                c02 = 0;
            double c11 = 0,
                c12 = 0;
            double c22 = 0;

            for (int j = 0; j < knn; j++)
            {
                int idx = neighbors[j];
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

            (double[] ev, double[,] eVec) = JacobiEigenDecomposition(
                c00 / knn,
                c01 / knn,
                c02 / knn,
                c11 / knn,
                c12 / knn,
                c22 / knn
            );

            int minIdx = 0;
            if (ev[1] < ev[minIdx])
                minIdx = 1;
            if (ev[2] < ev[minIdx])
                minIdx = 2;

            outNx[i] = (float)eVec[0, minIdx];
            outNy[i] = (float)eVec[1, minIdx];
            outNz[i] = (float)eVec[2, minIdx];
        }
    }

    // ===== 局部二次曲面拟合曲率计算 =====

    /// <summary>
    /// 在局部坐标系中拟合二次曲面，计算高斯曲率和平均曲率。
    /// </summary>
    /// <remarks>
    /// 步骤：
    /// 1. 以查询点 p 为原点，法向量 n 为 Z 轴，建立局部坐标系
    /// 2. 将 k 近邻变换到局部坐标系
    /// 3. 最小二乘拟合 z = ax² + bxy + cy² + dx + ey
    /// 4. 计算 Hessian，提取主曲率 → 高斯曲率、平均曲率
    /// </remarks>
    private static (double gaussian, double mean) ComputeCurvatureByQuadricFit(
        float[] xs,
        float[] ys,
        float[] zs,
        int queryIdx,
        int[] neighbors,
        int knn,
        double nx,
        double ny,
        double nz
    )
    {
        // ① 建立局部坐标系：Z 轴 = 法向量，X/Y 轴 = 任意正交基
        double nNorm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nNorm < 1e-12)
            return (0, 0);

        double uzx = nx / nNorm;
        double uzy = ny / nNorm;
        double uzz = nz / nNorm;

        // 选择与法向量不平行的向量来构造 X 轴
        double uxx,
            uxy,
            uxz;
        if (Math.Abs(uzx) < 0.9)
        {
            // 法向量不平行于 X 轴，用 (1,0,0) × n
            uxx = 0;
            uxy = uzz;
            uxz = -uzy;
        }
        else
        {
            // 法向量接近 X 轴，用 (0,1,0) × n
            uxx = -uzz;
            uxy = 0;
            uxz = uzx;
        }
        double uxNorm = Math.Sqrt(uxx * uxx + uxy * uxy + uxz * uxz);
        uxx /= uxNorm;
        uxy /= uxNorm;
        uxz /= uxNorm;

        // Y 轴 = Z × X
        double uyx = uzy * uxz - uzz * uxy;
        double uyy = uzz * uxx - uzx * uxz;
        double uyz = uzx * uxy - uzy * uxx;

        // ② 将邻域点变换到局部坐标系
        double px = xs[queryIdx];
        double py = ys[queryIdx];
        double pz = zs[queryIdx];

        // 构建最小二乘矩阵 A (knn × 5) 和右侧向量 b (knn × 1)
        // 拟合：z_local = a*x² + b*x*y + c*y² + d*x + e*y
        double[,] A = new double[knn, 5];
        double[] b = new double[knn];

        for (int i = 0; i < knn; i++)
        {
            int idx = neighbors[i];
            double dx = xs[idx] - px;
            double dy = ys[idx] - py;
            double dz = zs[idx] - pz;

            // 变换到局部坐标系
            double lx = uxx * dx + uxy * dy + uxz * dz;
            double ly = uyx * dx + uyy * dy + uyz * dz;
            double lz = uzx * dx + uzy * dy + uzz * dz;

            A[i, 0] = lx * lx; // x²
            A[i, 1] = lx * ly; // xy
            A[i, 2] = ly * ly; // y²
            A[i, 3] = lx; // x
            A[i, 4] = ly; // y
            b[i] = lz;
        }

        // ③ 解正规方程 (AᵀA) x = Aᵀb
        double[] coeffs = SolveNormalEquations(A, b, knn, 5);

        double a = coeffs[0]; // x² 系数
        double bCoef = coeffs[1]; // xy 系数
        double c = coeffs[2]; // y² 系数
        double d = coeffs[3]; // x 系数
        double e = coeffs[4]; // y 系数

        // ④ 计算曲率
        // 二次曲面 z = ax² + bxy + cy² + dx + ey 在原点处：
        // 第一基本形式系数：E = 1+d², F = de, G = 1+e²
        // 第二基本形式系数：L = 2a, M = b, N = 2c
        // 高斯曲率 K = (LN - M²) / (EG - F²)
        // 平均曲率 H = (EN - 2FM + GL) / (2(EG - F²))

        double E = 1 + d * d;
        double F = d * e;
        double G = 1 + e * e;
        double L = 2 * a;
        double M = bCoef;
        double N = 2 * c;

        double denom = E * G - F * F;
        if (Math.Abs(denom) < 1e-15)
            return (0, 0);

        double gaussian = (L * N - M * M) / denom;
        double mean = (E * N - 2 * F * M + G * L) / (2 * denom);

        return (gaussian, mean);
    }

    // ===== 最小二乘正规方程求解 =====

    /// <summary>
    /// 求解正规方程 (AᵀA) x = Aᵀb，使用 Cholesky 分解。
    /// </summary>
    private static double[] SolveNormalEquations(double[,] A, double[] b, int rows, int cols)
    {
        // 构建 AᵀA (cols × cols) 和 Aᵀb (cols × 1)
        double[,] ata = new double[cols, cols];
        double[] atb = new double[cols];

        for (int i = 0; i < rows; i++)
        {
            for (int c1 = 0; c1 < cols; c1++)
            {
                double aVal = A[i, c1];
                atb[c1] += aVal * b[i];
                for (int c2 = 0; c2 < cols; c2++)
                {
                    ata[c1, c2] += aVal * A[i, c2];
                }
            }
        }

        // Cholesky 分解：AᵀA = L·Lᵀ
        double[,] L = new double[cols, cols];
        for (int i = 0; i < cols; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < j; k++)
                    sum += L[i, k] * L[j, k];

                if (i == j)
                {
                    double diag = ata[i, i] - sum;
                    L[i, j] = diag > 1e-15 ? Math.Sqrt(diag) : 0;
                }
                else
                {
                    L[i, j] = (ata[i, j] - sum) / (L[j, j] > 1e-15 ? L[j, j] : 1);
                }
            }
        }

        // 前向代入 Ly = Aᵀb
        double[] y = new double[cols];
        for (int i = 0; i < cols; i++)
        {
            double sum = 0;
            for (int j = 0; j < i; j++)
                sum += L[i, j] * y[j];
            y[i] = (atb[i] - sum) / (L[i, i] > 1e-15 ? L[i, i] : 1);
        }

        // 回代 Lᵀx = y
        double[] x = new double[cols];
        for (int i = cols - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < cols; j++)
                sum += L[j, i] * x[j];
            x[i] = (y[i] - sum) / (L[i, i] > 1e-15 ? L[i, i] : 1);
        }

        return x;
    }

    // ===== K 近邻搜索（暴力搜索） =====

    /// <summary>
    /// 暴力搜索 k 个最近邻，排除自身。
    /// </summary>
    private static int[] FindKNearestNeighbors(
        float[] xs,
        float[] ys,
        float[] zs,
        int queryIndex,
        int k,
        int pointCount
    )
    {
        float qx = xs[queryIndex];
        float qy = ys[queryIndex];
        float qz = zs[queryIndex];

        var heap = new SortedList<double, int>(new DuplicateKeyComparer());

        for (int i = 0; i < pointCount; i++)
        {
            if (i == queryIndex)
                continue;

            float dx = xs[i] - qx;
            float dy = ys[i] - qy;
            float dz = zs[i] - qz;
            double distSq = dx * dx + dy * dy + dz * dz;

            if (heap.Count < k)
            {
                heap.Add(distSq, i);
            }
            else if (distSq < heap.Keys[heap.Count - 1])
            {
                heap.RemoveAt(heap.Count - 1);
                heap.Add(distSq, i);
            }
        }

        int[] result = new int[k];
        for (int i = 0; i < k && i < heap.Count; i++)
            result[i] = heap.Values[i];

        return result;
    }

    // ===== Jacobi 特征值分解（3×3 对称矩阵） =====

    /// <summary>
    /// 对 3×3 对称矩阵执行 Jacobi 特征值分解。
    /// </summary>
    private static (double[] eigenvalues, double[,] eigenvectors) JacobiEigenDecomposition(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22
    )
    {
        double[,] M = new double[3, 3]
        {
            { s00, s01, s02 },
            { s01, s11, s12 },
            { s02, s12, s22 },
        };

        double[,] V = new double[3, 3];
        for (int i = 0; i < 3; i++)
            V[i, i] = 1.0;

        for (int iter = 0; iter < 100; iter++)
        {
            int p = 0,
                q = 1;
            double maxOff = Math.Abs(M[0, 1]);
            if (Math.Abs(M[0, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[0, 2]);
                p = 0;
                q = 2;
            }
            if (Math.Abs(M[1, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[1, 2]);
                p = 1;
                q = 2;
            }

            if (maxOff < 1e-15)
                break;

            double theta = (M[q, q] - M[p, p]) / (2 * M[p, q]);
            double t =
                theta >= 0
                    ? 1.0 / (theta + Math.Sqrt(theta * theta + 1))
                    : 1.0 / (theta - Math.Sqrt(theta * theta + 1));
            double cosA = 1.0 / Math.Sqrt(t * t + 1);
            double sinA = t * cosA;

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

            for (int r = 0; r < 3; r++)
            {
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = cosA * vrp - sinA * vrq;
                V[r, q] = sinA * vrp + cosA * vrq;
            }
        }

        double[] eigenvalues = new double[] { M[0, 0], M[1, 1], M[2, 2] };
        return (eigenvalues, V);
    }

    // ===== 曲率热力图生成 =====

    /// <summary>
    /// 将曲率值投影到 XY 平面，生成伪彩色热力图。
    /// </summary>
    private static Mat GenerateCurvatureHeatmap(
        float[] xs,
        float[] ys,
        double[] curvatures,
        int pointCount,
        int resolution
    )
    {
        // 计算 XY 范围
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            if (xs[i] < minX)
                minX = xs[i];
            if (xs[i] > maxX)
                maxX = xs[i];
            if (ys[i] < minY)
                minY = ys[i];
            if (ys[i] > maxY)
                maxY = ys[i];
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-6f)
            rangeX = 1;
        if (rangeY < 1e-6f)
            rangeY = 1;

        // 确定曲率映射范围（使用对称范围，正=凸，负=凹）
        double absMax = 0;
        for (int i = 0; i < pointCount; i++)
        {
            double absVal = Math.Abs(curvatures[i]);
            if (absVal > absMax)
                absMax = absVal;
        }
        if (absMax < 1e-12)
            absMax = 1;

        // 累积和计数
        using Mat accumImage = Mat.Zeros(resolution, resolution, MatType.CV_64FC1);
        using Mat countImage = Mat.Zeros(resolution, resolution, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((xs[i] - minX) / rangeX * (resolution - 1));
            int py = (int)((ys[i] - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            accumImage.Set(py, px, accumImage.Get<double>(py, px) + curvatures[i]);
            countImage.Set(py, px, countImage.Get<int>(py, px) + 1);
        }

        // 归一化到 0-255
        using Mat normalizedMat = new Mat(resolution, resolution, MatType.CV_8UC1);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int count = countImage.Get<int>(y, x);
                if (count > 0)
                {
                    double avg = accumImage.Get<double>(y, x) / count;
                    // 映射到 0-255：负值（凹）→ 0-127，正值（凸）→ 128-255
                    double mapped = (avg / absMax + 1) * 0.5;
                    byte val = (byte)Math.Clamp(mapped * 255, 0, 255);
                    normalizedMat.Set(y, x, val);
                }
            }
        }

        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(normalizedMat, colorMap, ColormapTypes.Jet);

        return colorMap;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 辅助类型 ────────────────────────────────────────────────────────────

    /// <summary>支持重复键的 SortedList 比较器。</summary>
    private class DuplicateKeyComparer : IComparer<double>
    {
        public int Compare(double x, double y)
        {
            int result = x.CompareTo(y);
            return result == 0 ? 1 : result;
        }
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>曲率计算统计信息。</summary>
    public class CurvatureStats
    {
        public int PointCount { get; set; }
        public int KnnCount { get; set; }
        public double GaussianMax { get; set; }
        public double GaussianMean { get; set; }
        public double MeanCurvatureMax { get; set; }
        public double MeanCurvatureMean { get; set; }
        public double MeanCurvatureStd { get; set; }
        public bool HasProvidedNormals { get; set; }
    }
}
