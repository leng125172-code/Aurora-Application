namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：贪婪三角化投影。
/// <para>
/// 基于法向量的贪婪投影三角化算法，从有序或无序点云中重建三角网格。
/// 该算法从边界边开始，沿着法向量方向扩展三角形，逐步填充整个点云表面。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>构建点云的 KD-Tree 索引加速最近邻搜索</item>
///   <item>初始化边界边列表（从边界点开始）</item>
///   <item>对每条边界边，搜索符合条件的第三个点形成三角形</item>
///   <item>检查三角形是否满足角度约束和法向量一致性</item>
///   <item>将新边加入边界列表，继续扩展直到边界列表为空</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云（需包含法向量）</item>
///   <item>输出 <c>mesh_indices</c>（Mat）— 三角形索引矩阵（N×3，CV_32SC1）</item>
///   <item>输出 <c>mesh_info</c>（string）— 网格统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000d-4000-8000-000000000017")]
[Category("3D重建分割")]
[DisplayName("贪婪三角化")]
[Description("把点云连成三角网格面，快速建个面看看形状。")]
public class greedy_projection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "mesh_indices", DisplayName = "网格索引" },
            new VisionParameter<string>
            {
                ParameterName = "mesh_info",
                DisplayName = "网格信息",
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
                Name = "maxNeighbors",
                DisplayName = "最大邻域点数",
                ParameterType = typeof(int),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "angleThreshold",
                DisplayName = "角度阈值(度)",
                ParameterType = typeof(double),
                DefaultValue = "15",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maximumSurfaceAngle",
                DisplayName = "最大表面角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "45",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _searchRadius;
    private readonly int _maxNeighbors;
    private readonly double _angleThreshold;
    private readonly double _maximumSurfaceAngle;
    private bool _disposed;

    /// <summary>
    /// 初始化贪婪三角化投影算子。
    /// </summary>
    /// <param name="searchRadius">邻域搜索半径。</param>
    /// <param name="maxNeighbors">每个点的最大邻域点数。</param>
    /// <param name="angleThreshold">三角化角度阈值（度）。</param>
    /// <param name="maximumSurfaceAngle">最大表面角度（度）。</param>
    public greedy_projection(
        double searchRadius = 0.1,
        int maxNeighbors = 100,
        double angleThreshold = 15,
        double maximumSurfaceAngle = 45
    )
    {
        _searchRadius = searchRadius;
        _maxNeighbors = maxNeighbors;
        _angleThreshold = angleThreshold;
        _maximumSurfaceAngle = maximumSurfaceAngle;
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
            throw new InvalidOperationException("输入点云为空，无法进行三角化。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数不足，无法进行三角化。");

        // 检查是否包含法向量
        bool hasNormals = pointCloud.Cols >= 6;
        if (!hasNormals)
            throw new InvalidOperationException(
                "输入点云不包含法向量信息，请先执行法向量计算算子。"
            );

        // 提取点云坐标和法向量
        float[] xs = new float[pointCount];
        float[] ys = new float[pointCount];
        float[] zs = new float[pointCount];
        float[] nx = new float[pointCount];
        float[] ny = new float[pointCount];
        float[] nz = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
            nx[i] = pointCloud.Get<float>(i, 3);
            ny[i] = pointCloud.Get<float>(i, 4);
            nz[i] = pointCloud.Get<float>(i, 5);
        }

        // 构建空间网格加速邻域搜索
        SpatialGrid grid = new SpatialGrid(xs, ys, zs, pointCount, _searchRadius);

        // 三角化算法
        List<int[]> triangles = new List<int[]>();
        HashSet<long> edges = new HashSet<long>();
        Queue<long> boundaryEdges = new Queue<long>();

        // 初始化：找到边界点并创建初始边
        FindInitialEdges(xs, ys, zs, nx, ny, nz, pointCount, grid, edges, boundaryEdges);

        double angleThresholdRad = _angleThreshold * Math.PI / 180;
        double maxSurfaceAngleRad = _maximumSurfaceAngle * Math.PI / 180;

        // 贪婪三角化主循环
        while (boundaryEdges.Count > 0)
        {
            long edge = boundaryEdges.Dequeue();
            if (!edges.Contains(edge))
                continue;

            int p1 = (int)(edge >> 32);
            int p2 = (int)(edge & 0xFFFFFFFF);

            // 搜索候选第三个点
            int? p3 = FindThirdPoint(
                p1,
                p2,
                xs,
                ys,
                zs,
                nx,
                ny,
                nz,
                pointCount,
                grid,
                triangles,
                angleThresholdRad,
                maxSurfaceAngleRad
            );

            if (p3.HasValue)
            {
                // 添加三角形
                int[] triangle = new[] { p1, p2, p3.Value };
                triangles.Add(triangle);

                // 添加新边到边界列表
                long edge13 = MakeEdge(p1, p3.Value);
                long edge23 = MakeEdge(p2, p3.Value);

                if (!edges.Contains(edge13))
                {
                    edges.Add(edge13);
                    boundaryEdges.Enqueue(edge13);
                }

                if (!edges.Contains(edge23))
                {
                    edges.Add(edge23);
                    boundaryEdges.Enqueue(edge23);
                }
            }

            // 移除已处理的边
            edges.Remove(edge);
        }

        // 创建三角形索引矩阵
        Mat meshIndices = new Mat(triangles.Count, 3, MatType.CV_32SC1);
        for (int i = 0; i < triangles.Count; i++)
        {
            meshIndices.Set(i, 0, triangles[i][0]);
            meshIndices.Set(i, 1, triangles[i][1]);
            meshIndices.Set(i, 2, triangles[i][2]);
        }

        // 统计信息
        string meshInfo = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                PointCount = pointCount,
                TriangleCount = triangles.Count,
                EdgeCount = edges.Count,
                SearchRadius = _searchRadius,
                MaxNeighbors = _maxNeighbors,
                AngleThreshold = _angleThreshold,
                MaximumSurfaceAngle = _maximumSurfaceAngle,
                HasNormals = hasNormals,
            }
        );

        context.Set("mesh_indices", meshIndices);
        context.Set("mesh_info", meshInfo);
    }

    /// <summary>
    /// 找到初始边界边。
    /// </summary>
    private void FindInitialEdges(
        float[] xs,
        float[] ys,
        float[] zs,
        float[] nx,
        float[] ny,
        float[] nz,
        int pointCount,
        SpatialGrid grid,
        HashSet<long> edges,
        Queue<long> boundaryEdges
    )
    {
        for (int i = 0; i < pointCount; i++)
        {
            List<int> neighbors = grid.FindNeighbors(xs[i], ys[i], zs[i], xs, ys, zs);

            if (neighbors.Count < 3)
            {
                // 边界点，与最近邻点创建边
                if (neighbors.Count > 0)
                {
                    int j = neighbors[0];
                    long edge = MakeEdge(i, j);
                    if (!edges.Contains(edge))
                    {
                        edges.Add(edge);
                        boundaryEdges.Enqueue(edge);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 找到构成三角形的第三个点。
    /// </summary>
    private int? FindThirdPoint(
        int p1,
        int p2,
        float[] xs,
        float[] ys,
        float[] zs,
        float[] nx,
        float[] ny,
        float[] nz,
        int pointCount,
        SpatialGrid grid,
        List<int[]> triangles,
        double angleThresholdRad,
        double maxSurfaceAngleRad
    )
    {
        float x1 = xs[p1],
            y1 = ys[p1],
            z1 = zs[p1];
        float x2 = xs[p2],
            y2 = ys[p2],
            z2 = zs[p2];

        // 计算边方向向量
        float ex = x2 - x1;
        float ey = y2 - y1;
        float ez = z2 - z1;
        float edgeLen = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);

        if (edgeLen < 1e-10)
            return null;

        // 计算边中点和垂直平面
        float mx = (x1 + x2) / 2;
        float my = (y1 + y2) / 2;
        float mz = (z1 + z2) / 2;

        // 搜索中点邻域内的候选点
        List<int> candidates = grid.FindNeighbors(mx, my, mz, xs, ys, zs);

        int? bestPoint = null;
        double bestScore = double.MinValue;

        foreach (int p3 in candidates)
        {
            if (p3 == p1 || p3 == p2)
                continue;

            float x3 = xs[p3],
                y3 = ys[p3],
                z3 = zs[p3];

            // 检查法向量一致性
            float dot1 = nx[p1] * nx[p3] + ny[p1] * ny[p3] + nz[p1] * nz[p3];
            float dot2 = nx[p2] * nx[p3] + ny[p2] * ny[p3] + nz[p2] * nz[p3];

            if (dot1 < Math.Cos(maxSurfaceAngleRad) || dot2 < Math.Cos(maxSurfaceAngleRad))
                continue;

            // 计算三角形法向量
            float v1x = x2 - x1,
                v1y = y2 - y1,
                v1z = z2 - z1;
            float v2x = x3 - x1,
                v2y = y3 - y1,
                v2z = z3 - z1;

            float tx = v1y * v2z - v1z * v2y;
            float ty = v1z * v2x - v1x * v2z;
            float tz = v1x * v2y - v1y * v2x;

            float tLen = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (tLen < 1e-10)
                continue;

            // 检查三角形法向量与点法向量的一致性
            float dotT = tx * nx[p1] + ty * ny[p1] + tz * nz[p1];
            if (dotT < 0)
                continue;

            // 检查角度约束
            float len1 = (float)Math.Sqrt(v1x * v1x + v1y * v1y + v1z * v1z);
            float len2 = (float)Math.Sqrt(v2x * v2x + v2y * v2y + v2z * v2z);
            float cosAngle = Math.Abs((v1x * v2x + v1y * v2y + v1z * v2z) / (len1 * len2));

            if (Math.Acos(cosAngle) > angleThresholdRad)
                continue;

            // 检查是否与已有三角形重叠
            bool overlap = false;
            foreach (int[] tri in triangles)
            {
                if (
                    (tri[0] == p1 && tri[1] == p3)
                    || (tri[0] == p3 && tri[1] == p1)
                    || (tri[0] == p2 && tri[1] == p3)
                    || (tri[0] == p3 && tri[1] == p2)
                )
                {
                    overlap = true;
                    break;
                }
            }

            if (overlap)
                continue;

            // 选择距离边中点最近的点
            float distToMid = (float)
                Math.Sqrt((x3 - mx) * (x3 - mx) + (y3 - my) * (y3 - my) + (z3 - mz) * (z3 - mz));

            double score = 1.0 / (distToMid + 1e-10);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = p3;
            }
        }

        return bestPoint;
    }

    /// <summary>
    /// 创建边的唯一标识符。
    /// </summary>
    private static long MakeEdge(int p1, int p2)
    {
        if (p1 < p2)
            return ((long)p1 << 32) | (uint)p2;
        return ((long)p2 << 32) | (uint)p1;
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
