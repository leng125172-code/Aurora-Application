namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：点云投影到平面。
/// <para>
/// 将点云中每个点沿法向量方向投影到指定的参考平面上，输出投影后的点云。
/// 如果未连接平面参数，则默认投影到 Z=0 平面（XY 平面），常用于将曲面点云展开到参考面，
/// 或生成 2D 深度图用于缺陷可视化。
/// </para>
/// <para>
/// 投影公式：p' = p - dist × n，其中 dist = a·x + b·y + c·z + d，n = (a, b, c) 为单位法向量。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云（必选）</item>
///   <item>输入 <c>plane_params</c>（Mat）— 平面参数 [a,b,c,d]，形状 (4,1)，CV_64FC1（可选）</item>
///   <item>输出 <c>projected_cloud</c>（PointCloudData）— 投影到平面上的点云</item>
///   <item>输出 <c>projection_image</c>（Mat）— 2D 投影深度图（CV_8UC3，伪彩色）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000c-4000-8000-000000000019")]
[Category("3D重建分割")]
[DisplayName("投影到平面")]
[Description("把点云压到一个平面上，降维做分析。")]
public class project_to_plane : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg() { ParameterName = "plane_params", DisplayName = "平面参数" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "projected_cloud", DisplayName = "投影点云" },
            new MatImg() { ParameterName = "projection_image", DisplayName = "投影图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "投影分辨率",
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
    /// 初始化点云投影到平面算子。
    /// </summary>
    /// <param name="imageResolution">2D 深度图分辨率（边长像素数）。</param>
    public project_to_plane(int imageResolution = 512)
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

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行投影。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;

        // 读取平面参数，默认投影到 Z=0 平面（法向量 (0,0,1)，d=0）
        double a = 0,
            b = 0,
            c = 1,
            d = 0;
        Mat? planeParams = context.Get<Mat>("plane_params");
        if (planeParams != null && !planeParams.Empty() && planeParams.Rows >= 4)
        {
            a = planeParams.Get<double>(0, 0);
            b = planeParams.Get<double>(1, 0);
            c = planeParams.Get<double>(2, 0);
            d = planeParams.Get<double>(3, 0);

            // 法向量归一化
            double norm = Math.Sqrt(a * a + b * b + c * c);
            if (norm > 1e-10)
            {
                a /= norm;
                b /= norm;
                c /= norm;
                d /= norm;
            }
        }

        // 投影每个点到平面
        Mat projectedCloud = new Mat(pointCount, colCount, MatType.CV_32FC1);
        double[] distances = new double[pointCount];
        double maxDist = 0;

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            // 有符号距离
            double dist = a * x + b * y + c * z + d;
            double absDist = Math.Abs(dist);
            distances[i] = dist;
            if (absDist > maxDist)
                maxDist = absDist;

            // 投影：p' = p - dist × n
            projectedCloud.Set(i, 0, (float)(x - dist * a));
            projectedCloud.Set(i, 1, (float)(y - dist * b));
            projectedCloud.Set(i, 2, (float)(z - dist * c));

            // 复制其余列（颜色等）
            for (int col = 3; col < colCount; col++)
                projectedCloud.Set(i, col, pointCloud.Get<float>(i, col));
        }

        // 生成 2D 深度图
        Mat projectionImage = GenerateDepthImage(
            projectedCloud,
            distances,
            pointCount,
            maxDist,
            _imageResolution
        );

        var outputCloud = new PointCloudData();
        outputCloud.Value = projectedCloud;
        if (input.HasColors && input.Colors is not null && !input.Colors.Empty())
        {
            outputCloud.SetColors(input.Colors.Clone());
        }

        context.Set("projected_cloud", outputCloud);
        context.Set("projection_image", projectionImage);
    }

    /// <summary>
    /// 将投影后的点云生成 2D 深度图，用距离值着色。
    /// </summary>
    private static Mat GenerateDepthImage(
        Mat projectedCloud,
        double[] distances,
        int pointCount,
        double maxDist,
        int resolution
    )
    {
        // 计算 XY 范围
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            float x = projectedCloud.Get<float>(i, 0);
            float y = projectedCloud.Get<float>(i, 1);
            if (x < minX)
                minX = x;
            if (x > maxX)
                maxX = x;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-6f)
            rangeX = 1;
        if (rangeY < 1e-6f)
            rangeY = 1;

        double effectiveMax = maxDist > 0 ? maxDist : 1.0;

        using Mat accum = Mat.Zeros(resolution, resolution, MatType.CV_64FC1);
        using Mat count = Mat.Zeros(resolution, resolution, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((projectedCloud.Get<float>(i, 0) - minX) / rangeX * (resolution - 1));
            int py = (int)((projectedCloud.Get<float>(i, 1) - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            accum.Set(py, px, accum.Get<double>(py, px) + Math.Abs(distances[i]));
            count.Set(py, px, count.Get<int>(py, px) + 1);
        }

        using Mat normalized = new Mat(resolution, resolution, MatType.CV_8UC1);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int cnt = count.Get<int>(y, x);
                if (cnt > 0)
                {
                    double avg = accum.Get<double>(y, x) / cnt;
                    byte val = (byte)Math.Clamp(avg / effectiveMax * 255, 0, 255);
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
