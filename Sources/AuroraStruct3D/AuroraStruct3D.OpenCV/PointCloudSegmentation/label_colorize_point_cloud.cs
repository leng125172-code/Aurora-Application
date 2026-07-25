using AuroraStruct3D.OpenCV.VisionParameters;

namespace AuroraStruct3D.OpenCV.PointCloudSegmentation;

/// <summary>
/// 将带标签点云按标签渲染成彩色点云。
/// </summary>
[Guid("6f8be0a9-9951-4d58-b4ec-1aab2baf5101")]
[Category("3D重建分割")]
[DisplayName("标签着色点云")]
[Description("根据点云中的分割标签给不同区域着不同颜色，方便导出和前端查看。")]
public class label_colorize_point_cloud : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "noiseAsBlack",
                DisplayName = "噪声设为黑色",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly bool _noiseAsBlack;
    private bool _disposed;

    public label_colorize_point_cloud(bool noiseAsBlack = true)
    {
        _noiseAsBlack = noiseAsBlack;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud =
            input.PointCloud
            ?? throw new InvalidOperationException("输入点云为空，无法执行标签着色。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行标签着色。");
        }

        if (pointCloud.Cols < 4)
        {
            throw new InvalidOperationException("输入点云缺少标签列，期望第 4 列为分割标签。");
        }

        int pointCount = pointCloud.Rows;
        Mat colors = new(pointCount, 3, MatType.CV_8UC1);
        for (int i = 0; i < pointCount; i++)
        {
            int label = (int)Math.Round(pointCloud.Get<float>(i, 3));
            Vec3b color = ResolveColor(label);
            colors.Set(i, 0, color.Item0);
            colors.Set(i, 1, color.Item1);
            colors.Set(i, 2, color.Item2);
        }

        var output = new PointCloudData();
        output.Value = pointCloud.Clone();
        output.SetColors(colors);
        context.Set("output_point_cloud", output);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private Vec3b ResolveColor(int label)
    {
        if (label < 0 && _noiseAsBlack)
        {
            return new Vec3b(0, 0, 0);
        }

        int index = Math.Abs(label) % 10;
        return index switch
        {
            0 => new Vec3b(239, 83, 80),
            1 => new Vec3b(255, 167, 38),
            2 => new Vec3b(255, 238, 88),
            3 => new Vec3b(102, 187, 106),
            4 => new Vec3b(38, 198, 218),
            5 => new Vec3b(66, 165, 245),
            6 => new Vec3b(126, 87, 194),
            7 => new Vec3b(236, 64, 122),
            8 => new Vec3b(141, 110, 99),
            _ => new Vec3b(189, 189, 189),
        };
    }
}
