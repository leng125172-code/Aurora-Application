namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 半径滤波降噪。
/// <para>
/// 对点云中每个点，统计其半径范围内的邻域点数量。
/// 移除邻域点数少于阈值的离群点。
/// 适用于去除孤立噪点。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 滤波后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("9b809123-4567-8901-2345-678901234508")]
[Category("3D点云预处理")]
[DisplayName("半径滤波")]
[Description("半径范围内邻居太少的点当噪点删掉，清飞点。")]
public class radius_outlier_removal : IOperator
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
                Name = "radius",
                DisplayName = "搜索半径",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minNeighbors",
                DisplayName = "最小邻域点数",
                ParameterType = typeof(int),
                DefaultValue = "2",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _radius;
    private readonly int _minNeighbors;
    private bool _disposed;

    public radius_outlier_removal(double radius = 0.1, int minNeighbors = 2)
    {
        _radius = radius;
        _minNeighbors = minNeighbors;
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
            throw new InvalidOperationException("输入点云为空，无法执行半径滤波。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;
        double radiusSq = _radius * _radius;

        // 筛选内点
        var inliers = new List<float[]>();
        var inlierColors = new List<byte[]>();
        for (int i = 0; i < pointCount; i++)
        {
            int neighborCount = 0;
            float xi = pointCloud.Get<float>(i, 0);
            float yi = pointCloud.Get<float>(i, 1);
            float zi = pointCloud.Get<float>(i, 2);

            for (int j = 0; j < pointCount; j++)
            {
                if (i == j)
                    continue;
                double dx = xi - pointCloud.Get<float>(j, 0);
                double dy = yi - pointCloud.Get<float>(j, 1);
                double dz = zi - pointCloud.Get<float>(j, 2);
                if (dx * dx + dy * dy + dz * dz <= radiusSq)
                {
                    neighborCount++;
                    if (neighborCount >= _minNeighbors)
                        break;
                }
            }

            if (neighborCount >= _minNeighbors)
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
            throw new InvalidOperationException(
                "半径滤波后点云为空，请增大搜索半径或减小最小邻域点数。"
            );

        Mat result = new Mat(inliers.Count, colCount, MatType.CV_32FC1);
        for (int i = 0; i < inliers.Count; i++)
        for (int c = 0; c < colCount; c++)
            result.Set(i, c, inliers[i][c]);

        var output = new PointCloudData();
        output.Value = result;

        if (inlierColors.Count > 0)
        {
            Mat colors = new Mat(inlierColors.Count, 3, MatType.CV_8UC1);
            for (int i = 0; i < inlierColors.Count; i++)
            for (int c = 0; c < 3; c++)
                colors.Set<byte>(i, c, inlierColors[i][c]);
            output.SetColors(colors);
        }

        context.Set("output_point_cloud", output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
