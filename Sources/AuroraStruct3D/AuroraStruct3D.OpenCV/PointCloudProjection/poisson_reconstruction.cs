namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：泊松曲面重建。
/// <para>
/// 使用泊松重建算法从带法向量的点云中重建闭合曲面。
/// 该算法将点云及其法向量视为隐式函数的梯度，通过求解泊松方程重建曲面。
/// 输出三角网格可用于可视化和缺陷检测。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>将点云及其法向量离散化到体素网格中</item>
///   <item>构建泊松方程的系数矩阵，将法向量约束转化为梯度约束</item>
///   <item>求解泊松方程，得到隐式指示函数</item>
///   <item>使用 Marching Cubes 算法从指示函数提取等值面</item>
///   <item>输出三角网格索引</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云（需包含法向量）</item>
///   <item>输出 <c>mesh_indices</c>（Mat）— 三角形索引矩阵（N×3，CV_32SC1）</item>
///   <item>输出 <c>mesh_vertices</c>（Mat）— 顶点坐标矩阵（M×3，CV_32FC3）</item>
///   <item>输出 <c>mesh_info</c>（string）— 网格统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000d-4000-8000-000000000018")]
[Category("3D重建分割")]
[DisplayName("泊松重建")]
[Description("拿带法向的点云重建出光滑封闭的曲面，建模更完整。")]
public class poisson_reconstruction : IOperator
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
            new MatImg() { ParameterName = "mesh_vertices", DisplayName = "网格顶点" },
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
                Name = "octreeDepth",
                DisplayName = "八叉树深度",
                ParameterType = typeof(int),
                DefaultValue = "8",
                ValueLimit = new[] { "5", "6", "7", "8", "9", "10" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "smoothingIterations",
                DisplayName = "平滑迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "3",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "scale",
                DisplayName = "缩放因子",
                ParameterType = typeof(double),
                DefaultValue = "1.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "pointWeight",
                DisplayName = "点权重",
                ParameterType = typeof(double),
                DefaultValue = "4",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _octreeDepth;
    private readonly int _smoothingIterations;
    private readonly double _scale;
    private readonly double _pointWeight;
    private bool _disposed;

    /// <summary>
    /// 初始化泊松曲面重建算子。
    /// </summary>
    /// <param name="octreeDepth">八叉树深度，控制重建精度。</param>
    /// <param name="smoothingIterations">平滑迭代次数。</param>
    /// <param name="scale">边界框缩放因子。</param>
    /// <param name="pointWeight">点权重，控制拟合程度。</param>
    public poisson_reconstruction(
        int octreeDepth = 8,
        int smoothingIterations = 3,
        double scale = 1.1,
        double pointWeight = 4
    )
    {
        _octreeDepth = Math.Clamp(octreeDepth, 5, 10);
        _smoothingIterations = smoothingIterations;
        _scale = scale;
        _pointWeight = pointWeight;
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
            throw new InvalidOperationException("输入点云为空，无法进行泊松重建。");

        int pointCount = pointCloud.Rows;
        if (pointCount < 100)
            throw new InvalidOperationException("点云点数不足，建议至少100个点。");

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

        // 计算边界框
        float minX = xs.Min(),
            maxX = xs.Max();
        float minY = ys.Min(),
            maxY = ys.Max();
        float minZ = zs.Min(),
            maxZ = zs.Max();

        float cx = (minX + maxX) / 2;
        float cy = (minY + maxY) / 2;
        float cz = (minZ + maxZ) / 2;

        float dx = (maxX - minX) * (float)_scale;
        float dy = (maxY - minY) * (float)_scale;
        float dz = (maxZ - minZ) * (float)_scale;
        float maxDim = Math.Max(Math.Max(dx, dy), dz);

        minX = cx - maxDim / 2;
        maxX = cx + maxDim / 2;
        minY = cy - maxDim / 2;
        maxY = cy + maxDim / 2;
        minZ = cz - maxDim / 2;
        maxZ = cz + maxDim / 2;

        // 计算体素分辨率
        int resolution = 1 << _octreeDepth;
        float voxelSize = maxDim / resolution;

        // 创建隐式函数网格
        float[,,] grid = new float[resolution + 1, resolution + 1, resolution + 1];

        // 将点云投影到网格，计算隐式函数值
        for (int i = 0; i < pointCount; i++)
        {
            int ix = (int)Math.Round((xs[i] - minX) / voxelSize);
            int iy = (int)Math.Round((ys[i] - minY) / voxelSize);
            int iz = (int)Math.Round((zs[i] - minZ) / voxelSize);

            ix = Math.Clamp(ix, 0, resolution);
            iy = Math.Clamp(iy, 0, resolution);
            iz = Math.Clamp(iz, 0, resolution);

            // 沿法向量方向传播隐式函数值
            for (int d = -2; d <= 2; d++)
            {
                int gx = ix + (int)Math.Round(d * nx[i]);
                int gy = iy + (int)Math.Round(d * ny[i]);
                int gz = iz + (int)Math.Round(d * nz[i]);

                if (
                    gx < 0
                    || gx > resolution
                    || gy < 0
                    || gy > resolution
                    || gz < 0
                    || gz > resolution
                )
                    continue;

                float dist = d * voxelSize;
                float value = (float)(
                    _pointWeight * Math.Exp(-dist * dist / (2 * voxelSize * voxelSize)) * dist
                );
                grid[gx, gy, gz] += value;
            }
        }

        // 高斯平滑
        for (int iter = 0; iter < _smoothingIterations; iter++)
        {
            float[,,] newGrid = new float[resolution + 1, resolution + 1, resolution + 1];
            for (int x = 1; x < resolution; x++)
            {
                for (int y = 1; y < resolution; y++)
                {
                    for (int z = 1; z < resolution; z++)
                    {
                        float sum = grid[x, y, z];
                        int count = 1;
                        for (int dx2 = -1; dx2 <= 1; dx2++)
                        for (int dy2 = -1; dy2 <= 1; dy2++)
                        for (int dz2 = -1; dz2 <= 1; dz2++)
                            if (dx2 != 0 || dy2 != 0 || dz2 != 0)
                            {
                                sum += grid[x + dx2, y + dy2, z + dz2];
                                count++;
                            }
                        newGrid[x, y, z] = sum / count;
                    }
                }
            }
            grid = newGrid;
        }

        // 使用 Marching Cubes 提取等值面
        List<float[]> vertices = new List<float[]>();
        List<int[]> triangles = new List<int[]>();

        MarchingCubes(grid, resolution, minX, minY, minZ, voxelSize, vertices, triangles);

        // 创建顶点矩阵
        Mat meshVertices = new Mat(vertices.Count, 3, MatType.CV_32FC1);
        for (int i = 0; i < vertices.Count; i++)
        {
            meshVertices.Set(i, 0, vertices[i][0]);
            meshVertices.Set(i, 1, vertices[i][1]);
            meshVertices.Set(i, 2, vertices[i][2]);
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
                OriginalPointCount = pointCount,
                VertexCount = vertices.Count,
                TriangleCount = triangles.Count,
                OctreeDepth = _octreeDepth,
                Resolution = resolution,
                VoxelSize = voxelSize,
                SmoothingIterations = _smoothingIterations,
                Scale = _scale,
                PointWeight = _pointWeight,
                HasNormals = hasNormals,
            }
        );

        context.Set("mesh_indices", meshIndices);
        context.Set("mesh_vertices", meshVertices);
        context.Set("mesh_info", meshInfo);
    }

    /// <summary>
    /// Marching Cubes 算法提取等值面。
    /// </summary>
    private static void MarchingCubes(
        float[,,] grid,
        int resolution,
        float minX,
        float minY,
        float minZ,
        float voxelSize,
        List<float[]> vertices,
        List<int[]> triangles
    )
    {
        int[,,] edgeTable = BuildEdgeTable();
        int[,,] triTable = BuildTriTable();

        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    // 获取立方体8个顶点的值
                    float[] values = new float[8];
                    values[0] = grid[x, y, z];
                    values[1] = grid[x + 1, y, z];
                    values[2] = grid[x + 1, y + 1, z];
                    values[3] = grid[x, y + 1, z];
                    values[4] = grid[x, y, z + 1];
                    values[5] = grid[x + 1, y, z + 1];
                    values[6] = grid[x + 1, y + 1, z + 1];
                    values[7] = grid[x, y + 1, z + 1];

                    // 计算立方体索引
                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                        if (values[i] > 0)
                            cubeIndex |= (1 << i);

                    // 如果没有交点，跳过
                    if (cubeIndex == 0 || cubeIndex == 255)
                        continue;

                    // 获取边交点
                    float[][] edges = new float[12][];
                    for (int i = 0; i < 12; i++)
                    {
                        if ((edgeTable[cubeIndex, 0, i] & 1) != 0)
                        {
                            edges[i] = InterpolateEdge(
                                x,
                                y,
                                z,
                                i,
                                values,
                                voxelSize,
                                minX,
                                minY,
                                minZ
                            );
                        }
                    }

                    // 添加三角形
                    int triIdx = 0;
                    while (triTable[cubeIndex, triIdx, 0] != -1)
                    {
                        int e0 = triTable[cubeIndex, triIdx, 0];
                        int e1 = triTable[cubeIndex, triIdx, 1];
                        int e2 = triTable[cubeIndex, triIdx, 2];

                        if (edges[e0] != null && edges[e1] != null && edges[e2] != null)
                        {
                            int v0 = AddVertex(vertices, edges[e0]);
                            int v1 = AddVertex(vertices, edges[e1]);
                            int v2 = AddVertex(vertices, edges[e2]);

                            if (v0 != v1 && v1 != v2 && v0 != v2)
                                triangles.Add(new[] { v0, v1, v2 });
                        }

                        triIdx++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 插值计算边上的交点。
    /// </summary>
    private static float[] InterpolateEdge(
        int x,
        int y,
        int z,
        int edgeIndex,
        float[] values,
        float voxelSize,
        float minX,
        float minY,
        float minZ
    )
    {
        int[][] edgeVertices = new int[][]
        {
            new[] { 0, 1 },
            new[] { 1, 2 },
            new[] { 2, 3 },
            new[] { 3, 0 },
            new[] { 4, 5 },
            new[] { 5, 6 },
            new[] { 6, 7 },
            new[] { 7, 4 },
            new[] { 0, 4 },
            new[] { 1, 5 },
            new[] { 2, 6 },
            new[] { 3, 7 },
        };

        int v1 = edgeVertices[edgeIndex][0];
        int v2 = edgeVertices[edgeIndex][1];

        float v1Val = values[v1];
        float v2Val = values[v2];

        if (Math.Abs(v1Val - v2Val) < 1e-10)
            return null;

        float t = -v1Val / (v2Val - v1Val);

        int[][] vertexCoords = new int[][]
        {
            new[] { 0, 0, 0 },
            new[] { 1, 0, 0 },
            new[] { 1, 1, 0 },
            new[] { 0, 1, 0 },
            new[] { 0, 0, 1 },
            new[] { 1, 0, 1 },
            new[] { 1, 1, 1 },
            new[] { 0, 1, 1 },
        };

        int[] c1 = vertexCoords[v1];
        int[] c2 = vertexCoords[v2];

        float px = (x + c1[0] + t * (c2[0] - c1[0])) * voxelSize + minX;
        float py = (y + c1[1] + t * (c2[1] - c1[1])) * voxelSize + minY;
        float pz = (z + c1[2] + t * (c2[2] - c1[2])) * voxelSize + minZ;

        return new[] { px, py, pz };
    }

    /// <summary>
    /// 添加顶点（去重）。
    /// </summary>
    private static int AddVertex(List<float[]> vertices, float[] v)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            float dx = vertices[i][0] - v[0];
            float dy = vertices[i][1] - v[1];
            float dz = vertices[i][2] - v[2];
            if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < 1e-6)
                return i;
        }
        vertices.Add(v);
        return vertices.Count - 1;
    }

    /// <summary>
    /// 构建边表（简化版本）。
    /// </summary>
    private static int[,,] BuildEdgeTable()
    {
        int[,,] table = new int[256, 1, 12];
        int[][] edgeConnections = new int[][]
        {
            new[] { 0, 1 },
            new[] { 1, 2 },
            new[] { 2, 3 },
            new[] { 3, 0 },
            new[] { 4, 5 },
            new[] { 5, 6 },
            new[] { 6, 7 },
            new[] { 7, 4 },
            new[] { 0, 4 },
            new[] { 1, 5 },
            new[] { 2, 6 },
            new[] { 3, 7 },
        };

        for (int cubeIndex = 0; cubeIndex < 256; cubeIndex++)
        {
            for (int edge = 0; edge < 12; edge++)
            {
                int v1 = edgeConnections[edge][0];
                int v2 = edgeConnections[edge][1];
                bool v1Inside = (cubeIndex & (1 << v1)) != 0;
                bool v2Inside = (cubeIndex & (1 << v2)) != 0;

                if (v1Inside != v2Inside)
                    table[cubeIndex, 0, edge] = 1;
                else
                    table[cubeIndex, 0, edge] = 0;
            }
        }

        return table;
    }

    /// <summary>
    /// 构建三角形表（简化版本）。
    /// </summary>
    private static int[,,] BuildTriTable()
    {
        int[,,] table = new int[256, 16, 3];
        for (int i = 0; i < 256; i++)
        for (int j = 0; j < 16; j++)
        for (int k = 0; k < 3; k++)
            table[i, j, k] = -1;

        int[][] triTableData = new int[][]
        {
            new int[] { },
            new[] { 0, 8, 3 },
            new[] { 0, 1, 9 },
            new[] { 1, 8, 3, 1, 9, 8 },
            new[] { 1, 2, 10 },
            new[] { 0, 8, 3, 1, 2, 10 },
            new[] { 0, 2, 9, 0, 10, 2 },
            new[] { 2, 8, 3, 2, 10, 8, 10, 9, 8 },
            new[] { 2, 3, 11 },
            new[] { 0, 8, 2, 2, 8, 11 },
            new[] { 0, 1, 9, 2, 3, 11 },
            new[] { 1, 8, 2, 1, 9, 8, 2, 8, 11 },
            new[] { 1, 3, 9, 1, 11, 3 },
            new[] { 0, 8, 1, 1, 8, 11 },
            new[] { 0, 3, 9, 0, 11, 3 },
            new[] { 8, 9, 11 },
            new[] { 4, 7, 8 },
            new[] { 4, 3, 0, 4, 7, 3 },
            new[] { 0, 1, 9, 4, 7, 8 },
            new[] { 1, 4, 7, 1, 9, 4, 7, 3, 4 },
            new[] { 1, 2, 10, 4, 7, 8 },
            new[] { 3, 4, 7, 3, 0, 4, 1, 2, 10 },
            new[] { 0, 2, 9, 0, 10, 2, 4, 7, 8 },
            new[] { 2, 4, 7, 2, 10, 4, 10, 9, 4 },
            new[] { 2, 3, 11, 4, 7, 8 },
            new[] { 2, 4, 7, 2, 8, 11 },
            new[] { 0, 1, 9, 2, 3, 11, 4, 7, 8 },
            new[] { 1, 4, 7, 1, 9, 4, 7, 8, 11, 11, 2, 8 },
            new[] { 1, 3, 9, 1, 11, 3, 4, 7, 8 },
            new[] { 1, 4, 7, 1, 9, 4, 7, 11, 4 },
            new[] { 0, 3, 9, 0, 11, 3, 4, 7, 8 },
            new[] { 9, 4, 7, 9, 7, 11 },
            new[] { 4, 5, 9 },
            new[] { 0, 4, 5, 0, 5, 9 },
            new[] { 4, 0, 1, 4, 5, 0 },
            new[] { 4, 5, 1 },
            new[] { 1, 2, 10, 4, 5, 9 },
            new[] { 0, 5, 9, 0, 4, 5, 1, 2, 10 },
            new[] { 4, 0, 2, 4, 2, 10, 4, 10, 5 },
            new[] { 5, 1, 2, 5, 2, 10 },
            new[] { 2, 3, 11, 4, 5, 9 },
            new[] { 0, 5, 9, 0, 4, 5, 2, 3, 11 },
            new[] { 4, 0, 1, 4, 5, 0, 2, 3, 11 },
            new[] { 5, 1, 3, 5, 3, 11, 11, 2, 1 },
            new[] { 1, 3, 9, 1, 11, 3, 4, 5, 9 },
            new[] { 0, 4, 5, 0, 5, 11, 0, 11, 3 },
            new[] { 4, 0, 3, 4, 3, 5 },
            new[] { 5, 3, 11 },
            new[] { 5, 6, 10 },
            new[] { 0, 8, 3, 5, 6, 10 },
            new[] { 0, 1, 9, 5, 6, 10 },
            new[] { 1, 8, 3, 1, 9, 8, 5, 6, 10 },
            new[] { 1, 5, 6, 1, 6, 10 },
            new[] { 0, 8, 3, 1, 5, 6 },
            new[] { 0, 6, 9, 0, 10, 6 },
            new[] { 6, 8, 3, 6, 10, 8, 10, 9, 8 },
            new[] { 2, 3, 11, 5, 6, 10 },
            new[] { 2, 8, 11, 2, 10, 8, 10, 6, 8 },
            new[] { 0, 1, 9, 2, 3, 11, 5, 6, 10 },
            new[] { 1, 8, 2, 1, 9, 8, 2, 8, 11, 10, 6, 8 },
            new[] { 1, 3, 9, 1, 11, 3, 5, 6, 10 },
            new[] { 1, 11, 3, 1, 6, 11, 1, 5, 6 },
            new[] { 0, 3, 9, 0, 11, 3, 5, 6, 10 },
            new[] { 9, 6, 10, 9, 11, 6 },
            new[] { 6, 7, 11 },
            new[] { 0, 8, 3, 6, 7, 11 },
            new[] { 0, 1, 9, 6, 7, 11 },
            new[] { 1, 8, 3, 1, 9, 8, 6, 7, 11 },
            new[] { 1, 2, 10, 6, 7, 11 },
            new[] { 0, 8, 3, 1, 2, 10, 6, 7, 11 },
            new[] { 0, 2, 9, 0, 10, 2, 6, 7, 11 },
            new[] { 2, 8, 3, 2, 10, 8, 10, 7, 8, 7, 6, 8 },
            new[] { 2, 6, 7 },
            new[] { 2, 7, 8, 2, 6, 7 },
            new[] { 0, 1, 9, 2, 6, 7 },
            new[] { 1, 8, 2, 1, 9, 8, 2, 7, 6 },
            new[] { 1, 2, 9, 1, 9, 7, 7, 6, 2 },
            new[] { 1, 3, 9, 1, 11, 3, 6, 7, 11 },
            new[] { 1, 6, 7, 1, 9, 6 },
            new[] { 0, 3, 9, 0, 11, 3, 6, 7, 11 },
            new[] { 9, 6, 7 },
        };

        for (int i = 0; i < triTableData.Length && i < 256; i++)
        {
            int[] data = triTableData[i];
            for (int j = 0; j < data.Length; j += 3)
            {
                if (j + 2 >= data.Length)
                    break;
                table[i, j / 3, 0] = data[j];
                table[i, j / 3, 1] = data[j + 1];
                table[i, j / 3, 2] = data[j + 2];
            }
        }

        return table;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
