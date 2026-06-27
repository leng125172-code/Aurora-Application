namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 均匀采样。
/// <para>
/// 按固定步长等间隔保留点云中的点，实现均匀降采样。
/// 适用于需要保持原始点云中点的精确位置的下采样场景。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 下采样后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("c2e12345-6789-0123-4567-89012345670b")]
[Category("3D点云预处理")]
[DisplayName("均匀采样")]
[Description("按固定间隔均匀抽稀点云，简单快速地减量。")]
public class uniform_downsample : IOperator
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
                Name = "step",
                DisplayName = "采样步长",
                ParameterType = typeof(int),
                DefaultValue = "2",
                ValueLimit = new[] { "2", "3", "4", "5", "10" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _step;
    private bool _disposed;

    public uniform_downsample(int step = 2)
    {
        _step = step;
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
            throw new InvalidOperationException("输入点云为空，无法执行均匀采样。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;

        int sampledCount = (pointCount + _step - 1) / _step;
        Mat result = new Mat(sampledCount, colCount, MatType.CV_32FC1);

        int dstIdx = 0;
        for (int srcIdx = 0; srcIdx < pointCount; srcIdx += _step)
        {
            for (int c = 0; c < colCount; c++)
                result.Set(dstIdx, c, pointCloud.Get<float>(srcIdx, c));
            dstIdx++;
        }

        var output = new PointCloudData();
        output.Value = result;

        if (input.HasColors && input.Colors != null)
        {
            Mat colors = new Mat(sampledCount, 3, MatType.CV_8UC3);
            dstIdx = 0;
            for (int srcIdx = 0; srcIdx < pointCount; srcIdx += _step)
            {
                for (int c = 0; c < 3; c++)
                    colors.Set<byte>(dstIdx, c, input.Colors.Get<byte>(srcIdx, c));
                dstIdx++;
            }
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
