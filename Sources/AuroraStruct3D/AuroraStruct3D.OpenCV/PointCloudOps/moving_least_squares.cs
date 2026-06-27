namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：移动最小二乘平滑。
/// <para>
/// 对点云进行移动最小二乘（MLS）平滑处理，通过在每个点的邻域内拟合二次多项式，
/// 在去除噪声的同时保留几何特征和边缘。MLS 是一种局部多项式拟合方法，
/// 比传统滤波方法能更好地保留细节特征。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>对每个点，搜索其邻域范围内的所有点</item>
///   <item>构建加权局部坐标系，以查询点为原点</item>
///   <item>用二次多项式拟合邻域点，权重随距离衰减</item>
///   <item>将拟合多项式在查询点处的值作为新位置</item>
///   <item>同时计算新的法向量（多项式梯度）</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>smoothed_cloud</c>（PointCloudData）— 平滑后的点云</item>
///   <item>输出 <c>mls_info</c>（string）— 平滑统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0004-4000-8000-000000000016")]
[Category("3D点云预处理")]
[DisplayName("MLS平滑")]
[Description("用移动最小二乘把点云表面磨光滑，去抖动毛刺。")]
public class moving_least_squares : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "smoothed_cloud", DisplayName = "平滑点云" },
            new VisionParameter<string>
            {
                ParameterName = "mls_info",
                DisplayName = "平滑信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "searchRadius",
                DisplayName = "搜索半径",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "polynomialOrder",
                DisplayName = "多项式阶数",
                ParameterType = typeof(int),
                DefaultValue = "2",
                ValueLimit = new[] { "1", "2", "3" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "sigma",
                DisplayName = "权重标准差",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "computeNormals",
                DisplayName = "计算法向量",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Switch,
            },
        };

    private readonly double _searchRadius;
    private readonly int _polynomialOrder;
    private readonly double _sigma;
    private readonly bool _computeNormals;
    private bool _disposed;

    /// <summary>
    /// 初始化移动最小二乘平滑算子。
    /// </summary>
    /// <param name="searchRadius">邻域搜索半径。</param>
    /// <param name="polynomialOrder">拟合多项式阶数（1-3）。</param>
    /// <param name="sigma">权重函数标准差。</param>
    /// <param name="computeNormals">是否计算平滑后的法向量。</param>
    public moving_least_squares(
        double searchRadius = 0.1,
        int polynomialOrder = 2,
        double sigma = 0.05,
        bool computeNormals = true
    )
    {
        _searchRadius = searchRadius;
        _polynomialOrder = Math.Clamp(polynomialOrder, 1, 3);
        _sigma = sigma;
        _computeNormals = computeNormals;
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
            throw new InvalidOperationException("输入点云为空，无法进行移动最小二乘平滑。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数不足，无法进行平滑。");

        // 提取点云坐标
        float[] xs = new float[pointCount];
        float[] ys = new float[pointCount];
        float[] zs = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
        }

        // 构建空间网格加速邻域搜索
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, _searchRadius);

        int cols = _computeNormals ? 6 : pointCloud.Cols;
        if (cols > pointCloud.Cols)
            cols = 6;

        Mat smoothedCloud = new Mat(pointCount, cols, pointCloud.Type());
        double totalDisplacement = 0;
        int updatedPoints = 0;

        for (int i = 0; i < pointCount; i++)
        {
            float xi = xs[i];
            float yi = ys[i];
            float zi = zs[i];

            // 搜索邻域点
            List<int> neighbors = grid.FindNeighbors(xi, yi, zi, xs, ys, zs);

            // 如果邻域点太少，保持原位置
            if (neighbors.Count < (_polynomialOrder + 1) * (_polynomialOrder + 2) / 2)
            {
                for (int c = 0; c < pointCloud.Cols && c < cols; c++)
                    smoothedCloud.Set(i, c, pointCloud.Get<float>(i, c));
                continue;
            }

            // 构建加权最小二乘系统
            int paramCount = (_polynomialOrder + 1) * (_polynomialOrder + 2) / 2;
            double[,] A = new double[neighbors.Count, paramCount];
            double[] b = new double[neighbors.Count];
            double[] weights = new double[neighbors.Count];

            int idx = 0;
            foreach (int j in neighbors)
            {
                if (j == i)
                    continue;

                float dx = xs[j] - xi;
                float dy = ys[j] - yi;

                double dist = Math.Sqrt(dx * dx + dy * dy + (zs[j] - zi) * (zs[j] - zi));
                if (dist > _searchRadius)
                    continue;

                // 高斯权重
                weights[idx] = Math.Exp(-(dist * dist) / (2 * _sigma * _sigma));

                // 构建多项式基函数
                int pIdx = 0;
                for (int order = 0; order <= _polynomialOrder; order++)
                {
                    for (int k = 0; k <= order; k++)
                    {
                        A[idx, pIdx++] = Math.Pow(dx, k) * Math.Pow(dy, order - k);
                    }
                }

                b[idx] = zs[j] - zi;
                idx++;
            }

            int validCount = idx;
            if (validCount < paramCount)
            {
                for (int c = 0; c < pointCloud.Cols && c < cols; c++)
                    smoothedCloud.Set(i, c, pointCloud.Get<float>(i, c));
                continue;
            }

            // 求解加权最小二乘：(A^T W A) x = A^T W b
            double[,] AtWA = new double[paramCount, paramCount];
            double[] AtWb = new double[paramCount];

            for (int r = 0; r < validCount; r++)
            {
                double w = weights[r];
                for (int c = 0; c < paramCount; c++)
                {
                    double ac = A[r, c] * w;
                    for (int cc = 0; cc < paramCount; cc++)
                        AtWA[c, cc] += ac * A[r, cc];
                    AtWb[c] += ac * b[r];
                }
            }

            // 使用高斯消元求解
            double[] x = SolveLinearSystem(AtWA, AtWb, paramCount);

            // 计算平滑后的 Z 值（在局部坐标系原点处）
            double smoothedZ = x[0]; // 常数项

            // 计算法向量（梯度）
            float nx = 0,
                ny = 0,
                nz = 1;
            if (_computeNormals && paramCount > 2)
            {
                // dz/dx = x[1] (一阶项系数)
                // dz/dy = x[2] (一阶项系数)
                nx = (float)-x[1];
                ny = (float)-x[2];
                nz = 1.0f;

                float nNorm = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nNorm > 1e-10f)
                {
                    nx /= nNorm;
                    ny /= nNorm;
                    nz /= nNorm;
                }
            }

            // 设置平滑后的坐标
            smoothedCloud.Set(i, 0, xi);
            smoothedCloud.Set(i, 1, yi);
            smoothedCloud.Set(i, 2, (float)(zi + smoothedZ));

            if (_computeNormals)
            {
                smoothedCloud.Set(i, 3, nx);
                smoothedCloud.Set(i, 4, ny);
                smoothedCloud.Set(i, 5, nz);
            }

            // 计算位移
            double disp = Math.Abs(smoothedZ);
            totalDisplacement += disp;
            updatedPoints++;
        }

        // 创建输出点云数据
        PointCloudData output = new PointCloudData();
        output.Value = smoothedCloud;
        if (input.HasColors)
            output.SetColors(input.Colors?.Clone());

        // 统计信息
        double avgDisplacement = updatedPoints > 0 ? totalDisplacement / updatedPoints : 0;
        string mlsInfo = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                OriginalPointCount = pointCount,
                UpdatedPointCount = updatedPoints,
                SearchRadius = _searchRadius,
                PolynomialOrder = _polynomialOrder,
                Sigma = _sigma,
                ComputeNormals = _computeNormals,
                AverageDisplacement = avgDisplacement,
            }
        );

        context.Set("smoothed_cloud", output);
        context.Set("mls_info", mlsInfo);
    }

    /// <summary>
    /// 使用高斯消元求解线性方程组。
    /// </summary>
    private static double[] SolveLinearSystem(double[,] A, double[] b, int n)
    {
        double[] x = new double[n];

        // 前向消元
        for (int k = 0; k < n; k++)
        {
            // 寻找主元
            int maxRow = k;
            for (int i = k + 1; i < n; i++)
                if (Math.Abs(A[i, k]) > Math.Abs(A[maxRow, k]))
                    maxRow = i;

            // 交换行
            if (maxRow != k)
            {
                for (int j = k; j < n; j++)
                {
                    double temp = A[k, j];
                    A[k, j] = A[maxRow, j];
                    A[maxRow, j] = temp;
                }
                double tempB = b[k];
                b[k] = b[maxRow];
                b[maxRow] = tempB;
            }

            // 消元
            for (int i = k + 1; i < n; i++)
            {
                double factor = A[i, k] / A[k, k];
                for (int j = k; j < n; j++)
                    A[i, j] -= factor * A[k, j];
                b[i] -= factor * b[k];
            }
        }

        // 回代求解
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
                sum += A[i, j] * x[j];
            x[i] = (b[i] - sum) / A[i, i];
        }

        return x;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 空间哈希网格 ────────────────────────────────────────────────────────────

    private class SpatialGrid
    {
        private readonly float _invCellSize;
        private readonly float _minX,
            _minY,
            _minZ;
        private readonly int _gridSizeX,
            _gridSizeY,
            _gridSizeZ;
        private readonly Dictionary<int, List<int>> _cells;

        public SpatialGrid(float[] xs, float[] ys, float[] zs, int pointCount, double cellSize)
        {
            _invCellSize = 1.0f / (float)cellSize;
            _minX = xs.Min();
            _minY = ys.Min();
            _minZ = zs.Min();
            float maxX = xs.Max();
            float maxY = ys.Max();
            float maxZ = zs.Max();

            _gridSizeX = Math.Max(1, (int)((maxX - _minX) * _invCellSize) + 1);
            _gridSizeY = Math.Max(1, (int)((maxY - _minY) * _invCellSize) + 1);
            _gridSizeZ = Math.Max(1, (int)((maxZ - _minZ) * _invCellSize) + 1);

            _cells = new Dictionary<int, List<int>>();

            for (int i = 0; i < pointCount; i++)
            {
                int key = GetCellKey(xs[i], ys[i], zs[i]);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _cells[key] = list;
                }
                list.Add(i);
            }
        }

        public List<int> FindNeighbors(
            float qx,
            float qy,
            float qz,
            float[] xs,
            float[] ys,
            float[] zs
        )
        {
            List<int> neighbors = new List<int>();
            int cx = (int)((qx - _minX) * _invCellSize);
            int cy = (int)((qy - _minY) * _invCellSize);
            int cz = (int)((qz - _minZ) * _invCellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        int nz = cz + dz;

                        if (
                            nx < 0
                            || nx >= _gridSizeX
                            || ny < 0
                            || ny >= _gridSizeY
                            || nz < 0
                            || nz >= _gridSizeZ
                        )
                            continue;

                        int key = nz * _gridSizeY * _gridSizeX + ny * _gridSizeX + nx;
                        if (!_cells.TryGetValue(key, out var cell))
                            continue;

                        neighbors.AddRange(cell);
                    }
                }
            }

            return neighbors;
        }

        private int GetCellKey(float x, float y, float z)
        {
            int cx = (int)((x - _minX) * _invCellSize);
            int cy = (int)((y - _minY) * _invCellSize);
            int cz = (int)((z - _minZ) * _invCellSize);
            cx = Math.Clamp(cx, 0, _gridSizeX - 1);
            cy = Math.Clamp(cy, 0, _gridSizeY - 1);
            cz = Math.Clamp(cz, 0, _gridSizeZ - 1);
            return cz * _gridSizeY * _gridSizeX + cy * _gridSizeX + cx;
        }
    }
}
