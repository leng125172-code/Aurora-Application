namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 双边滤波。
/// <para>
/// 对点云进行双边滤波平滑，在去除噪声的同时保留边缘特征。
/// 不同于传统的高斯滤波，双边滤波同时考虑空间距离和灰度（强度）距离，
/// 因此在边缘处权重会迅速衰减，从而保留边缘的锐利度。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>对每个点，搜索其邻域范围内的所有点</item>
///   <item>计算空间权重：基于欧氏距离的高斯函数</item>
///   <item>计算强度权重：基于 Z 坐标差异的高斯函数</item>
///   <item>组合权重 = 空间权重 × 强度权重</item>
///   <item>用加权平均更新点的位置</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>filtered_cloud</c>（PointCloudData）— 滤波后的点云</item>
///   <item>输出 <c>filter_info</c>（string）— 滤波统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0004-4000-8000-000000000015")]
[Category("3D点云预处理")]
[DisplayName("3D双边滤波")]
[Description("点云保边降噪，平滑表面的同时不糊掉棱角。")]
public class bilateral_filter_3d : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "filtered_cloud", DisplayName = "滤波点云" },
            new VisionParameter<string>
            {
                ParameterName = "filter_info",
                DisplayName = "滤波信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "spatialRadius",
                DisplayName = "空间半径",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "sigmaSpatial",
                DisplayName = "空间标准差",
                ParameterType = typeof(double),
                DefaultValue = "0.03",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "sigmaRange",
                DisplayName = "强度标准差",
                ParameterType = typeof(double),
                DefaultValue = "0.02",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxNeighbors",
                DisplayName = "最大邻域点数",
                ParameterType = typeof(int),
                DefaultValue = "50",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _spatialRadius;
    private readonly double _sigmaSpatial;
    private readonly double _sigmaRange;
    private readonly int _maxNeighbors;
    private bool _disposed;

    /// <summary>
    /// 初始化 3D 双边滤波算子。
    /// </summary>
    /// <param name="spatialRadius">邻域搜索半径。</param>
    /// <param name="sigmaSpatial">空间高斯标准差。</param>
    /// <param name="sigmaRange">强度高斯标准差。</param>
    /// <param name="maxNeighbors">每个点的最大邻域点数。</param>
    public bilateral_filter_3d(
        double spatialRadius = 0.05,
        double sigmaSpatial = 0.03,
        double sigmaRange = 0.02,
        int maxNeighbors = 50
    )
    {
        _spatialRadius = spatialRadius;
        _sigmaSpatial = sigmaSpatial;
        _sigmaRange = sigmaRange;
        _maxNeighbors = maxNeighbors;
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
            throw new InvalidOperationException("输入点云为空，无法进行双边滤波。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数不足，无法进行滤波。");

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
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, _spatialRadius);

        // 计算滤波后的坐标
        Mat filteredCloud = new Mat(pointCount, pointCloud.Cols, pointCloud.Type());
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
            if (neighbors.Count < 3)
            {
                for (int c = 0; c < pointCloud.Cols; c++)
                    filteredCloud.Set(i, c, pointCloud.Get<float>(i, c));
                continue;
            }

            // 双边滤波加权平均
            double sumW = 0;
            double sumX = 0,
                sumY = 0,
                sumZ = 0;
            double sumNx = 0,
                sumNy = 0,
                sumNz = 0;

            foreach (int j in neighbors)
            {
                if (j == i)
                    continue;

                float dx = xs[j] - xi;
                float dy = ys[j] - yi;
                float dz = zs[j] - zi;
                double spatialDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (spatialDist > _spatialRadius)
                    continue;

                // 空间权重：高斯函数
                double spatialWeight = Math.Exp(
                    -(spatialDist * spatialDist) / (2 * _sigmaSpatial * _sigmaSpatial)
                );

                // 强度权重：基于 Z 坐标差异
                double rangeDiff = Math.Abs(zs[j] - zi);
                double rangeWeight = Math.Exp(
                    -(rangeDiff * rangeDiff) / (2 * _sigmaRange * _sigmaRange)
                );

                // 组合权重
                double weight = spatialWeight * rangeWeight;

                sumW += weight;
                sumX += weight * xs[j];
                sumY += weight * ys[j];
                sumZ += weight * zs[j];

                // 如果有点云法向量，也进行滤波
                if (pointCloud.Cols >= 6)
                {
                    sumNx += weight * pointCloud.Get<float>(j, 3);
                    sumNy += weight * pointCloud.Get<float>(j, 4);
                    sumNz += weight * pointCloud.Get<float>(j, 5);
                }
            }

            // 更新坐标
            float newX,
                newY,
                newZ;
            if (sumW > 0)
            {
                newX = (float)(sumX / sumW);
                newY = (float)(sumY / sumW);
                newZ = (float)(sumZ / sumW);
            }
            else
            {
                newX = xi;
                newY = yi;
                newZ = zi;
            }

            // 计算位移
            double disp = Math.Sqrt(
                (newX - xi) * (newX - xi) + (newY - yi) * (newY - yi) + (newZ - zi) * (newZ - zi)
            );
            totalDisplacement += disp;
            updatedPoints++;

            // 设置滤波后的坐标
            filteredCloud.Set(i, 0, newX);
            filteredCloud.Set(i, 1, newY);
            filteredCloud.Set(i, 2, newZ);

            // 设置滤波后的法向量
            if (pointCloud.Cols >= 6 && sumW > 0)
            {
                float nx = (float)(sumNx / sumW);
                float ny = (float)(sumNy / sumW);
                float nz = (float)(sumNz / sumW);

                // 归一化法向量
                float nNorm = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nNorm > 1e-10f)
                {
                    nx /= nNorm;
                    ny /= nNorm;
                    nz /= nNorm;
                }

                filteredCloud.Set(i, 3, nx);
                filteredCloud.Set(i, 4, ny);
                filteredCloud.Set(i, 5, nz);
            }
        }

        // 创建输出点云数据
        PointCloudData output = new PointCloudData();
        output.Value = filteredCloud;
        if (input.HasColors)
            output.SetColors(input.Colors?.Clone());

        // 统计信息
        double avgDisplacement = updatedPoints > 0 ? totalDisplacement / updatedPoints : 0;
        string filterInfo = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                OriginalPointCount = pointCount,
                UpdatedPointCount = updatedPoints,
                SpatialRadius = _spatialRadius,
                SigmaSpatial = _sigmaSpatial,
                SigmaRange = _sigmaRange,
                AverageDisplacement = avgDisplacement,
                MaxNeighbors = _maxNeighbors,
            }
        );

        context.Set("filtered_cloud", output);
        context.Set("filter_info", filterInfo);
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
