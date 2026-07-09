namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：点云投影到 2D 图像。
/// <para>
/// 将 3D 点云投影到 XY 平面，生成 2D 图像。支持两种投影模式：
/// <list type="bullet">
///   <item><b>RGB 模式</b>：将点云自带的颜色信息渲染到 2D 图像上，生成彩色图</item>
///   <item><b>深度模式</b>：将点云的 Z 坐标（高度）渲染为伪彩色深度图</item>
/// </list>
/// 生成 2D 图像后可直接串联 2D 算子链（如边缘检测、阈值分割等），实现 3D→2D 的混合处理管线。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>output_image</c>（Mat）— 投影后的 2D 图像（CV_8UC3）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000d-4000-8000-000000000020")]
[Category("3D重建分割")]
[DisplayName("投影到图像")]
[Description("把点云拍扁成一张深度/高度图，转回 2D 来处理。")]
public class project_to_image : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_image", DisplayName = "输出图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "projectionMode",
                DisplayName = "投影模式",
                ParameterType = typeof(string),
                DefaultValue = "depth",
                ValueLimit = new[] { "depth", "rgb" },
                Required = false,
                ControlType = PortControlType.Select,
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
        };

    private readonly string _projectionMode;
    private readonly int _imageResolution;
    private bool _disposed;

    /// <summary>
    /// 初始化点云投影到图像算子。
    /// </summary>
    /// <param name="projectionMode">投影模式：depth（深度图）或 rgb（彩色图）。</param>
    /// <param name="imageResolution">输出图像分辨率（边长像素数）。</param>
    public project_to_image(string projectionMode = "depth", int imageResolution = 512)
    {
        _projectionMode = projectionMode;
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

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行投影。");

        int pointCount = pointCloud.Rows;

        // 计算 XY 范围
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        float minZ = float.MaxValue,
            maxZ = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);
            if (x < minX)
                minX = x;
            if (x > maxX)
                maxX = x;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
            if (z < minZ)
                minZ = z;
            if (z > maxZ)
                maxZ = z;
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        float rangeZ = maxZ - minZ;
        if (rangeX < 1e-6f)
            rangeX = 1;
        if (rangeY < 1e-6f)
            rangeY = 1;
        if (rangeZ < 1e-6f)
            rangeZ = 1;

        int res = _imageResolution;

        if (_projectionMode == "rgb")
        {
            // RGB 模式：优先使用 PointCloudData 自带颜色信息，兼容旧格式回退。
            Mat rgbImage = Common.PointCloudProjectionRenderer.RenderColorImage(input, res);
            context.Set("output_image", rgbImage);
        }
        else
        {
            // 深度模式：使用 Z 坐标为深度值
            Mat depthImage = RenderDepthImage(
                pointCloud,
                pointCount,
                minX,
                minY,
                minZ,
                rangeX,
                rangeY,
                rangeZ,
                res
            );
            context.Set("output_image", depthImage);
        }
    }

    /// <summary>
    /// 渲染深度伪彩色图像。
    /// </summary>
    private static Mat RenderDepthImage(
        Mat pointCloud,
        int pointCount,
        float minX,
        float minY,
        float minZ,
        float rangeX,
        float rangeY,
        float rangeZ,
        int res
    )
    {
        using Mat accum = Mat.Zeros(res, res, MatType.CV_64FC1);
        using Mat count = Mat.Zeros(res, res, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((pointCloud.Get<float>(i, 0) - minX) / rangeX * (res - 1));
            int py = (int)((pointCloud.Get<float>(i, 1) - minY) / rangeY * (res - 1));
            px = Math.Clamp(px, 0, res - 1);
            py = Math.Clamp(py, 0, res - 1);

            float z = pointCloud.Get<float>(i, 2);
            accum.Set(py, px, accum.Get<double>(py, px) + z);
            count.Set(py, px, count.Get<int>(py, px) + 1);
        }

        using Mat normalized = new Mat(res, res, MatType.CV_8UC1);
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int cnt = count.Get<int>(y, x);
                if (cnt > 0)
                {
                    double avg = accum.Get<double>(y, x) / cnt;
                    byte val = (byte)Math.Clamp((avg - minZ) / rangeZ * 255, 0, 255);
                    normalized.Set(y, x, val);
                }
            }
        }

        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(normalized, colorMap, ColormapTypes.Jet);
        return colorMap;
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
