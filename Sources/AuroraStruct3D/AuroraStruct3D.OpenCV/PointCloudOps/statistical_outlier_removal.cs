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
[Category("3D降噪滤波")]
[DisplayName("3D 统计滤波")]
[Description("基于邻域距离统计去除离群噪点，适合去除稀疏噪声。")]
public class statistical_outlier_removal : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new PointCloudData() { ParameterName = "input_point_cloud" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new PointCloudData() { ParameterName = "output_point_cloud" } };

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

        // 计算每个点到 K 近邻的平均距离
        double[] distances = new double[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            var neighbors = new List<double>();
            for (int j = 0; j < pointCount; j++)
            {
                if (i == j)
                    continue;
                double dx = pointCloud.Get<float>(i, 0) - pointCloud.Get<float>(j, 0);
                double dy = pointCloud.Get<float>(i, 1) - pointCloud.Get<float>(j, 1);
                double dz = pointCloud.Get<float>(i, 2) - pointCloud.Get<float>(j, 2);
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                InsertSorted(neighbors, dist, _k);
            }
            distances[i] = neighbors.Average();
        }

        double mean = distances.Average();
        double stddev = Math.Sqrt(distances.Select(d => (d - mean) * (d - mean)).Average());
        double threshold = mean + _stddevMultiplier * stddev;

        // 筛选内点
        var inliers = new List<float[]>();
        var inlierColors = new List<byte[]>();
        for (int i = 0; i < pointCount; i++)
        {
            if (distances[i] <= threshold)
            {
                var point = new float[colCount];
                for (int c = 0; c < colCount; c++)
                    point[c] = pointCloud.Get<float>(i, c);
                inliers.Add(point);

                if (input.HasColors && input.Colors != null)
                {
                    inlierColors.Add(
                        new[]
                        {
                            input.Colors.Get<byte>(i, 0),
                            input.Colors.Get<byte>(i, 1),
                            input.Colors.Get<byte>(i, 2),
                        }
                    );
                }
            }
        }

        if (inliers.Count == 0)
            throw new InvalidOperationException("统计滤波后点云为空，请调整参数。");

        Mat result = new Mat(inliers.Count, colCount, MatType.CV_32FC1);
        for (int i = 0; i < inliers.Count; i++)
        for (int c = 0; c < colCount; c++)
            result.Set(i, c, inliers[i][c]);

        var output = new PointCloudData();
        output.Value = result;

        if (inlierColors.Count > 0)
        {
            Mat colors = new Mat(inlierColors.Count, 3, MatType.CV_8UC3);
            for (int i = 0; i < inlierColors.Count; i++)
            for (int c = 0; c < 3; c++)
                colors.Set<byte>(i, c, inlierColors[i][c]);
            output.SetColors(colors);
        }

        context.Set("output_point_cloud", output);
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
