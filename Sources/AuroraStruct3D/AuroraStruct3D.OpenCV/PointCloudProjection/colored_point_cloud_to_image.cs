namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：将带颜色的点云投影到图像。
/// </summary>
[Guid("1dd66555-a882-436b-bf30-dab8c680a201")]
[Category("3D重建分割")]
[DisplayName("彩色点云转图像")]
[Description("把带颜色的点云拍成 2D 彩色图，方便接 2D ROI 和标注算子。")]
public class colored_point_cloud_to_image : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_image", DisplayName = "输出图像" },
            new VisionParameter<string>
            {
                ParameterName = "projection_mapping",
                ParameterType = typeof(string),
                DisplayName = "投影映射",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "autoBounds",
                DisplayName = "自动边界",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "图像分辨率",
                ParameterType = typeof(int),
                DefaultValue = "512",
                ValueLimit = new[] { "256", "512", "1024", "2048" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "minX",
                DisplayName = "最小X",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxX",
                DisplayName = "最大X",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minY",
                DisplayName = "最小Y",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxY",
                DisplayName = "最大Y",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly bool _autoBounds;
    private readonly int _imageResolution;
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _minY;
    private readonly double _maxY;
    private bool _disposed;

    /// <summary>
    /// 初始化彩色点云转图像算子。
    /// </summary>
    public colored_point_cloud_to_image(
        bool autoBounds = true,
        int imageResolution = 512,
        double minX = 0,
        double maxX = 100,
        double minY = 0,
        double maxY = 100
    )
    {
        _autoBounds = autoBounds;
        _imageResolution = imageResolution;
        _minX = minX;
        _maxX = maxX;
        _minY = minY;
        _maxY = maxY;
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

        double minX;
        double maxX;
        double minY;
        double maxY;
        Mat output;
        if (_autoBounds)
        {
            output = Common.PointCloudProjectionRenderer.RenderColorImage(
                input,
                _imageResolution,
                out minX,
                out maxX,
                out minY,
                out maxY
            );
        }
        else
        {
            minX = _minX;
            maxX = _maxX;
            minY = _minY;
            maxY = _maxY;
            output = Common.PointCloudProjectionRenderer.RenderColorImage(
                input,
                _imageResolution,
                minX,
                maxX,
                minY,
                maxY
            );
        }

        context.Set("output_image", output);

        // 同步产出投影映射，作为 ROI 底图像素坐标 → 模型世界坐标映射的权威来源，
        // 从源头消除 ROI 底图尺寸/边界与实际投影不一致的问题。
        var projectionMapping = new RoiOps.RoiProjectionMapping
        {
            ViewLabel = "XY",
            WorldMinX = minX,
            WorldMaxX = maxX,
            WorldMinY = minY,
            WorldMaxY = maxY,
            ImageWidth = output.Width,
            ImageHeight = output.Height,
        };
        context.Set(
            "projection_mapping",
            System.Text.Json.JsonSerializer.Serialize(projectionMapping)
        );
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
