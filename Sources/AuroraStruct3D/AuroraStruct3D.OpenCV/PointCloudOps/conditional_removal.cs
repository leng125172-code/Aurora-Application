namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：3D 条件去噪。
/// <para>
/// 根据用户指定的轴方向和阈值范围，移除点云中不符合条件的点。
/// 支持按 X、Y、Z 轴的上下限进行裁剪，适用于去除背景噪点或 ROI 裁剪。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 去噪后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("a0c91234-5678-9012-3456-789012345609")]
[Category("3D点云预处理")]
[DisplayName("条件去噪")]
[Description("按坐标或数值条件筛点，只留你要的那段范围。")]
public class conditional_removal : IOperator
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
                Name = "axis",
                DisplayName = "过滤轴",
                ParameterType = typeof(string),
                DefaultValue = "Z",
                ValueLimit = new[] { "X", "Y", "Z" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "minValue",
                DisplayName = "最小值",
                ParameterType = typeof(double),
                DefaultValue = "-1000",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxValue",
                DisplayName = "最大值",
                ParameterType = typeof(double),
                DefaultValue = "1000",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _axis;
    private readonly double _minValue;
    private readonly double _maxValue;
    private bool _disposed;

    public conditional_removal(string axis = "Z", double minValue = -1000, double maxValue = 1000)
    {
        _axis = axis;
        _minValue = minValue;
        _maxValue = maxValue;
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
            throw new InvalidOperationException("输入点云为空，无法执行条件去噪。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;
        int axisIndex = _axis switch
        {
            "X" => 0,
            "Y" => 1,
            _ => 2,
        };

        var inliers = new List<float[]>();
        var inlierColors = new List<byte[]>();
        for (int i = 0; i < pointCount; i++)
        {
            float value = pointCloud.Get<float>(i, axisIndex);
            if (value >= _minValue && value <= _maxValue)
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
            throw new InvalidOperationException("条件去噪后点云为空，请调整阈值范围。");

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
