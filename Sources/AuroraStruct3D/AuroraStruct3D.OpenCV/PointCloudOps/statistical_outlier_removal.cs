namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 统计滤波降噪。
/// <para>
/// 对点云中每个点，计算其到 K 个最近邻点的平均距离。
/// 移除那些平均距离超出全局均值 + 标准差倍数阈值的离群点。
/// 适用于去除稀疏离群噪声点。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 滤波后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("8a708912-3456-7890-1234-567890123407")]
[Category("3D点云预处理")]
[DisplayName("统计滤波")]
[Description("按邻域距离统计踢掉离群噪点，给点云去毛刺。")]
public class statistical_outlier_removal : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "输出点云" } };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "k",
                DisplayName = "邻域点数 K",
                ParameterType = typeof(int),
                DefaultValue = "6",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "stddevMultiplier",
                DisplayName = "标准差倍数",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _k;
    private readonly double _stddevMultiplier;
    private bool _disposed;

    public statistical_outlier_removal(int k = 6, double stddevMultiplier = 1.0)
    {
        _k = k;
        _stddevMultiplier = stddevMultiplier;
    }

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
            throw new InvalidOperationException("输入点云为空，无法执行统计滤波。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;

        if (pointCount <= _k)
            throw new InvalidOperationException($"点云点数({pointCount})少于邻域点数({_k})，无法执行统计滤波。");

        double[] distances = ComputeKNearestDistances(pointCloud, pointCount, _k);

        double mean = distances.Average();
        double variance = distances.Select(d => (d - mean) * (d - mean)).Average();
        double stddev = Math.Sqrt(variance);
        double threshold = mean + _stddevMultiplier * stddev;

        Mat result = new Mat(pointCount, colCount, MatType.CV_32FC1);
        Mat? colors = input.HasColors && input.Colors != null
            ? new Mat(pointCount, 3, MatType.CV_8UC1)
            : null;

        int outCount = 0;
        for (int i = 0; i < pointCount; i++)
        {
            if (distances[i] <= threshold)
            {
                for (int c = 0; c < colCount; c++)
                    result.Set(outCount, c, pointCloud.Get<float>(i, c));

                if (colors != null)
                    for (int c = 0; c < 3; c++)
                        colors.Set<byte>(outCount, c, input.Colors!.Get<byte>(i, c));

                outCount++;
            }
        }

        if (outCount == 0)
            throw new InvalidOperationException("统计滤波后点云为空，请调整参数。");

        result = result.RowRange(0, outCount);
        if (colors != null)
            colors = colors.RowRange(0, outCount);

        var output = new PointCloudData();
        output.Value = result;
        if (colors != null)
            output.SetColors(colors);

        context.Set("output_point_cloud", output);
    }

    private static double[] ComputeKNearestDistances(Mat pointCloud, int pointCount, int k)
    {
        double[] distances = new double[pointCount];

        double cellSize = EstimateCellSize(pointCloud, pointCount, k);

        Dictionary<(int, int, int), List<int>> voxelGrid = BuildVoxelGrid(pointCloud, pointCount, cellSize);

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            int cx = (int)Math.Floor(x / cellSize);
            int cy = (int)Math.Floor(y / cellSize);
            int cz = (int)Math.Floor(z / cellSize);

            var neighbors = new List<double>();

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (voxelGrid.TryGetValue((cx + dx, cy + dy, cz + dz), out var cellPoints))
                        {
                            foreach (int j in cellPoints)
                            {
                                if (i == j)
                                    continue;

                                double dx2 = pointCloud.Get<float>(i, 0) - pointCloud.Get<float>(j, 0);
                                double dy2 = pointCloud.Get<float>(i, 1) - pointCloud.Get<float>(j, 1);
                                double dz2 = pointCloud.Get<float>(i, 2) - pointCloud.Get<float>(j, 2);
                                double dist = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;

                                InsertSortedSquared(neighbors, dist, k);
                            }
                        }
                    }
                }
            }

            distances[i] = neighbors.Count > 0
                ? Math.Sqrt(neighbors.Average())
                : double.MaxValue;
        }

        return distances;
    }

    private static double EstimateCellSize(Mat pointCloud, int pointCount, int k)
    {
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;

        for (int i = 0; i < Math.Min(1000, pointCount); i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }

        double volume = (maxX - minX) * (maxY - minY) * (maxZ - minZ);
        double density = pointCount / volume;
        double cellVolume = k / density;
        return Math.Pow(cellVolume, 1.0 / 3.0) * 1.5;
    }

    private static Dictionary<(int, int, int), List<int>> BuildVoxelGrid(Mat pointCloud, int pointCount, double cellSize)
    {
        var grid = new Dictionary<(int, int, int), List<int>>();

        for (int i = 0; i < pointCount; i++)
        {
            int cx = (int)Math.Floor(pointCloud.Get<float>(i, 0) / cellSize);
            int cy = (int)Math.Floor(pointCloud.Get<float>(i, 1) / cellSize);
            int cz = (int)Math.Floor(pointCloud.Get<float>(i, 2) / cellSize);

            var key = (cx, cy, cz);
            if (!grid.ContainsKey(key))
                grid[key] = new List<int>();
            grid[key].Add(i);
        }

        return grid;
    }

    private static void InsertSortedSquared(List<double> list, double value, int maxCount)
    {
        int idx = list.BinarySearch(value);
        if (idx < 0)
            idx = ~idx;
        list.Insert(idx, value);
        if (list.Count > maxCount)
            list.RemoveAt(list.Count - 1);
    }

    private static void InsertSorted(List<double> list, double value, int maxCount)
    {
        int idx = list.BinarySearch(value);
        if (idx < 0)
            idx = ~idx;
        list.Insert(idx, value);
        if (list.Count > maxCount)
            list.RemoveAt(list.Count - 1);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
