using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudDistance;

/// <summary>
/// 工作流算子：点云到平面距离计算。
/// <para>
/// 计算点云中每个点到参考平面的欧氏距离，输出距离图像（2D 投影）和统计信息。
/// 这是 3D 缺陷检测的核心算子，距离值反映了点云表面相对于参考平面的高度偏差（凹凸）。
/// </para>
/// <para>
/// 计算原理：对点云中每个点 (x, y, z)，计算到平面 ax + by + cz + d = 0 的有符号距离：
/// <c>distance = |a·x + b·y + c·z + d| / sqrt(a² + b² + c²)</c>。
/// 由于参考平面通常已归一化（a²+b²+c²=1），实际计算简化为 <c>|a·x + b·y + c·z + d|</c>。
/// </para>
/// <para>
/// 输出模式：
/// <list type="bullet">
///   <item>3D 输出：每个点的距离值，保留原始点云结构</item>
///   <item>2D 投影：将距离值投影到 XY 平面，生成距离热力图（适合可视化）</item>
///   <item>统计信息：最大距离、平均距离、标准差、超出阈值的点数</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输入 <c>plane_params</c>（Mat）— 平面参数 [a, b, c, d]，形状 (4, 1)，CV_64FC1</item>
///   <item>输出 <c>distance_mat</c>（Mat）— 每个点的距离值（N×1，CV_64FC1）</item>
///   <item>输出 <c>distance_image</c>（Mat）— 2D 距离热力图（CV_8UC3，伪彩色）</item>
///   <item>输出 <c>stats_json</c>（string）— 统计信息 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0003-4000-8000-000000000010")]
[Category("3D拟合测量")]
[DisplayName("平面距离")]
[Description("量每个点到基准面的距离，测平面度、看凹凸用。")]
public class point_to_plane_distance : IOperator
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
            new MatImg() { ParameterName = "distance_mat", DisplayName = "距离矩阵" },
            new MatImg() { ParameterName = "distance_image", DisplayName = "距离图像" },
            new VisionParameter<string>
            {
                ParameterName = "stats_json",
                DisplayName = "统计信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "flatness",
                DisplayName = "平面度(PV)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "max_absolute_distance",
                DisplayName = "最大绝对偏差",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<bool>
            {
                ParameterName = "is_ok",
                DisplayName = "是否合格",
                ParameterType = typeof(bool),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "缺陷阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
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

    private readonly double _distanceThreshold;
    private readonly int _imageResolution;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化点云平面距离算子。
    /// </summary>
    /// <param name="distanceThreshold">缺陷阈值，距离超过此值视为缺陷点。</param>
    /// <param name="imageResolution">2D 投影热力图分辨率（边长像素数）。</param>
    public point_to_plane_distance(double distanceThreshold = 0.1, int imageResolution = 512)
    {
        _distanceThreshold = distanceThreshold;
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
            throw new InvalidOperationException("输入点云为空，无法计算平面距离。");

        Mat planeParams =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_params' 为空，请确认已连接平面拟合算子。"
            );

        if (planeParams.Empty() || planeParams.Rows < 4)
            throw new InvalidOperationException(
                "平面参数格式无效，期望 [a, b, c, d] 形状 (4, 1) 的 CV_64FC1 矩阵。"
            );

        int pointCount = pointCloud.Rows;
        if (pointCount < 3)
            throw new InvalidOperationException("点云点数不足，无法计算。");

        // 读取平面参数 (a, b, c, d)
        double a = planeParams.Get<double>(0, 0);
        double b = planeParams.Get<double>(1, 0);
        double c = planeParams.Get<double>(2, 0);
        double d = planeParams.Get<double>(3, 0);

        // 法向量归一化（确保鲁棒）
        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-10)
            throw new InvalidOperationException("平面法向量为零向量，无法计算距离。");
        a /= norm;
        b /= norm;
        c /= norm;
        d /= norm;

        // 计算每个点到平面的有符号距离
        Mat distances = new Mat(pointCount, 1, MatType.CV_64FC1);
        double maxDist = 0;
        double minSignedDist = double.MaxValue;
        double maxSignedDist = double.MinValue;
        double sumDist = 0;
        int defectCount = 0;

        for (int i = 0; i < pointCount; i++)
        {
            double x = pointCloud.Get<float>(i, 0);
            double y = pointCloud.Get<float>(i, 1);
            double z = pointCloud.Get<float>(i, 2);

            // 有符号距离：a·x + b·y + c·z + d
            double signedDist = a * x + b * y + c * z + d;
            double absDist = Math.Abs(signedDist);
            minSignedDist = Math.Min(minSignedDist, signedDist);
            maxSignedDist = Math.Max(maxSignedDist, signedDist);

            distances.Set(i, 0, absDist);
            sumDist += absDist;
            if (absDist > maxDist)
                maxDist = absDist;
            if (absDist > _distanceThreshold)
                defectCount++;
        }

        double meanDist = pointCount > 0 ? sumDist / pointCount : 0;

        // 计算标准差
        double sumSq = 0;
        for (int i = 0; i < pointCount; i++)
        {
            double diff = distances.Get<double>(i, 0) - meanDist;
            sumSq += diff * diff;
        }
        double stdDev = pointCount > 1 ? Math.Sqrt(sumSq / pointCount) : 0;

        // 生成 2D 投影热力图
        Mat distanceImage = GenerateDistanceHeatmap(
            pointCloud,
            distances,
            _imageResolution,
            maxDist
        );

        // 统计信息
        double flatness = maxSignedDist - minSignedDist;
        bool isOk = flatness <= _distanceThreshold;
        var stats = new DistanceStats
        {
            PointCount = pointCount,
            MaxDistance = maxDist,
            MeanDistance = meanDist,
            StdDev = stdDev,
            DefectCount = defectCount,
            DefectRatio = pointCount > 0 ? (double)defectCount / pointCount : 0,
            Threshold = _distanceThreshold,
            MinSignedDistance = minSignedDist,
            MaxSignedDistance = maxSignedDist,
            Flatness = flatness,
            IsOk = isOk,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);

        context.Set("distance_mat", distances);
        context.Set("distance_image", distanceImage);
        context.Set("stats_json", statsJson);
        context.Set("flatness", flatness);
        context.Set("max_absolute_distance", maxDist);
        context.Set("is_ok", isOk);
    }

    /// <summary>
    /// 将点云距离值投影到 XY 平面，生成伪彩色热力图。
    /// </summary>
    private static Mat GenerateDistanceHeatmap(
        Mat pointCloud,
        Mat distances,
        int resolution,
        double maxDist
    )
    {
        int pointCount = pointCloud.Rows;

        // 计算 XY 范围
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
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

        // 创建距离累积图像和计数图像
        using Mat accumImage = Mat.Zeros(resolution, resolution, MatType.CV_64FC1);
        using Mat countImage = Mat.Zeros(resolution, resolution, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((pointCloud.Get<float>(i, 0) - minX) / rangeX * (resolution - 1));
            int py = (int)((pointCloud.Get<float>(i, 1) - minY) / rangeY * (resolution - 1));

            // 边界裁剪
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            double dist = distances.Get<double>(i, 0);
            accumImage.Set(py, px, accumImage.Get<double>(py, px) + dist);
            countImage.Set(py, px, countImage.Get<int>(py, px) + 1);
        }

        // 平均化 + 归一化
        using Mat normalizedMat = new Mat(resolution, resolution, MatType.CV_8UC1);
        double effectiveMax = maxDist > 0 ? maxDist : 1.0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int count = countImage.Get<int>(y, x);
                if (count > 0)
                {
                    double avg = accumImage.Get<double>(y, x) / count;
                    byte val = (byte)Math.Clamp(avg / effectiveMax * 255, 0, 255);
                    normalizedMat.Set(y, x, val);
                }
            }
        }

        // 伪彩色映射
        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(normalizedMat, colorMap, ColormapTypes.Jet);

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

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>距离统计信息。</summary>
    public class DistanceStats
    {
        public int PointCount { get; set; }
        public double MaxDistance { get; set; }
        public double MeanDistance { get; set; }
        public double StdDev { get; set; }
        public int DefectCount { get; set; }
        public double DefectRatio { get; set; }
        public double Threshold { get; set; }
        public double MinSignedDistance { get; set; }
        public double MaxSignedDistance { get; set; }
        public double Flatness { get; set; }
        public bool IsOk { get; set; }
    }
}
