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
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
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
        };

    private readonly int _imageResolution;
    private bool _disposed;

    /// <summary>
    /// 初始化彩色点云转图像算子。
    /// </summary>
    public colored_point_cloud_to_image(int imageResolution = 512)
    {
        _imageResolution = imageResolution;
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

        Mat output = Common.PointCloudProjectionRenderer.RenderColorImage(input, _imageResolution);
        context.Set("output_image", output);
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