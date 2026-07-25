namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 体素下采样。
/// <para>
/// 将点云空间划分为等大的体素网格，每个体素以质心点替代内部所有点。
/// 能有效减少点云密度，同时保持点云的整体形状特征。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 下采样后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1d01234-5678-9012-4567-89012345670a")]
[Category("3D点云预处理")]
[DisplayName("体素下采样")]
[Description("用体素格子抽稀点云，点太多先减量提速，密度也更均匀。")]
public class voxel_downsample : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "voxelSize",
                DisplayName = "体素尺寸",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _voxelSize;
    private bool _disposed;

    public voxel_downsample(double voxelSize = 0.05)
    {
        if (!double.IsFinite(voxelSize) || voxelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(voxelSize),
                voxelSize,
                "体素尺寸必须是大于 0 的有限数值。"
            );
        }
        _voxelSize = voxelSize;
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
            throw new InvalidOperationException("输入点云为空，无法执行体素下采样。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;

        // 体素网格：key = 体素索引，value = (点列表, 颜色列表)
        var voxelMap =
            new Dictionary<(int, int, int), (List<float[]> points, List<byte[]> colors)>();

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            int vx = (int)Math.Floor(x / _voxelSize);
            int vy = (int)Math.Floor(y / _voxelSize);
            int vz = (int)Math.Floor(z / _voxelSize);
            var key = (vx, vy, vz);

            if (!voxelMap.TryGetValue(key, out var entry))
            {
                entry = (new List<float[]>(), new List<byte[]>());
                voxelMap[key] = entry;
            }

            var point = new float[colCount];
            for (int c = 0; c < colCount; c++)
                point[c] = pointCloud.Get<float>(i, c);
            entry.points.Add(point);

            if (input.HasColors && input.Colors != null)
            {
                entry.colors.Add(
                    new[]
                    {
                        input.Colors.Get<byte>(i, 0),
                        input.Colors.Get<byte>(i, 1),
                        input.Colors.Get<byte>(i, 2),
                    }
                );
            }
        }

        // 计算每个体素的质心
        var sampledPoints = new List<float[]>();
        var sampledColors = new List<byte[]>();
        foreach (var kv in voxelMap)
        {
            var (pts, cols) = kv.Value;
            var centroid = new float[colCount];
            for (int c = 0; c < colCount; c++)
            {
                centroid[c] = (float)pts.Average(p => p[c]);
            }
            sampledPoints.Add(centroid);

            if (cols.Count > 0)
            {
                byte avgR = (byte)cols.Average(c => c[2]);
                byte avgG = (byte)cols.Average(c => c[1]);
                byte avgB = (byte)cols.Average(c => c[0]);
                sampledColors.Add(new[] { avgB, avgG, avgR });
            }
        }

        Mat result = new Mat(sampledPoints.Count, colCount, MatType.CV_32FC1);
        for (int i = 0; i < sampledPoints.Count; i++)
        for (int c = 0; c < colCount; c++)
            result.Set(i, c, sampledPoints[i][c]);

        var output = new PointCloudData();
        output.Value = result;

        if (sampledColors.Count > 0)
        {
            // 颜色统一采用 N×3、CV_8UC1；PointCloudData 及投影算子均按
            // Colors.Get<byte>(row, channel) 读取。CV_8UC3 会变成每个单元三通道，
            // 与该矩阵布局约定冲突并造成颜色错位。
            Mat colors = new Mat(sampledColors.Count, 3, MatType.CV_8UC1);
            for (int i = 0; i < sampledColors.Count; i++)
            for (int c = 0; c < 3; c++)
                colors.Set<byte>(i, c, sampledColors[i][c]);
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
