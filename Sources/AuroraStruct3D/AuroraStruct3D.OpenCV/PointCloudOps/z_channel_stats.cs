using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：点云高度统计。
/// <para>
/// 计算点云的高度统计特征，用于 3D 缺陷检测中的高度分析：
/// <list type="bullet">
///   <item><b>平均高度</b> — 均值，反映区域整体高度水平</item>
///   <item><b>最大高度</b> — 最大值，反映突起/凸点高度</item>
///   <item><b>最小高度</b> — 最小值，反映凹陷深度</item>
///   <item><b>高度标准差</b> — 离散程度，反映表面平整度</item>
/// </list>
/// 支持两种模式：
/// <list type="bullet">
///   <item><b>未连接参考平面</b>：直接统计 Z 坐标（适用于产品水平放置）</item>
///   <item><b>连接参考平面</b>：计算点到平面的有符号距离（适用于产品倾斜放置）</item>
/// </list>
/// 可与 <see cref="point_cloud_crop"/> 配合，先用 2D ROI 掩膜裁剪点云区域，
/// 再统计该区域的高度特征，实现区域高度分析。
/// 与 <see cref="ransac_plane_fit"/> 配合可正确处理产品倾斜场景。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输入 <c>plane_params</c>（Mat）— 参考平面参数 [a,b,c,d]，4x1 CV_64FC1（可选）</item>
///   <item>输出 <c>z_stats_json</c>（string）— 高度统计 JSON</item>
///   <item>输出 <c>avg_height</c>（double）— 平均高度，方便直接连线到 OK/NG 判定</item>
///   <item>输出 <c>max_height</c>（double）— 最大高度（突起高度）</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1c2d3e4-0002-4000-8000-000000000102")]
[Category("3D拟合测量")]
[DisplayName("高度统计")]
[Description("统计 Z 方向高度分布，看高低范围和平均高度。")]
public class z_channel_stats : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg() { ParameterName = "plane_params", DisplayName = "参考平面" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<string>
            {
                ParameterName = "z_stats_json",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
                DisplayName = "Z统计JSON",
            },
            new VisionParameter<double>
            {
                ParameterName = "avg_height",
                ParameterType = typeof(double),
                DisplayName = "平均高度",
            },
            new VisionParameter<double>
            {
                ParameterName = "centroid_z",
                ParameterType = typeof(double),
                DisplayName = "质心Z高度",
            },
            new VisionParameter<double>
            {
                ParameterName = "max_height",
                ParameterType = typeof(double),
                DisplayName = "最大高度",
            },
            new VisionParameter<double>
            {
                ParameterName = "min_height",
                ParameterType = typeof(double),
                DisplayName = "最小高度",
            },
            new VisionParameter<double>
            {
                ParameterName = "std_height",
                ParameterType = typeof(double),
                DisplayName = "高度标准差",
            },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    public z_channel_stats() { }

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
            throw new InvalidOperationException("输入点云为空，无法计算高度统计。");

        int pointCount = pointCloud.Rows;

        // 读取可选的参考平面参数
        double a = 0,
            b = 0,
            c = 1,
            d = 0;
        bool usePlane = false;
        Mat? planeParams = context.Get<Mat>("plane_params");
        if (planeParams != null && !planeParams.Empty() && planeParams.Rows >= 4)
        {
            a = planeParams.Get<double>(0, 0);
            b = planeParams.Get<double>(1, 0);
            c = planeParams.Get<double>(2, 0);
            d = planeParams.Get<double>(3, 0);

            // 法向量归一化（确保有符号距离正确）
            double norm = Math.Sqrt(a * a + b * b + c * c);
            if (norm > 1e-10)
            {
                a /= norm;
                b /= norm;
                c /= norm;
                d /= norm;
            }
            usePlane = true;
        }

        // 计算每个点的高度值（有参考平面时用有符号距离，否则用 Z 坐标）
        double sumH = 0;
        double sumZ = 0;
        double maxH = double.MinValue;
        double minH = double.MaxValue;
        double[] heights = new double[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            sumZ += z;

            double h = usePlane
                ? a * x + b * y + c * z + d // 有符号距离：正值=法向量方向，负值=反向
                : z; // 直接使用 Z 坐标

            heights[i] = h;
            sumH += h;
            if (h > maxH)
                maxH = h;
            if (h < minH)
                minH = h;
        }

        double avgH = sumH / pointCount;
        double centroidZ = Math.Round(sumZ / pointCount, 4);

        // 计算标准差
        double sumSquaredDiff = 0;
        for (int i = 0; i < pointCount; i++)
        {
            double diff = heights[i] - avgH;
            sumSquaredDiff += diff * diff;
        }
        double stdH = Math.Sqrt(sumSquaredDiff / pointCount);

        // 构建统计结果 JSON
        var stats = new ZChannelStats
        {
            AvgHeight = Math.Round(avgH, 4),
            MaxHeight = Math.Round(maxH, 4),
            MinHeight = Math.Round(minH, 4),
            StdHeight = Math.Round(stdH, 4),
            HeightRange = Math.Round(maxH - minH, 4),
            PointCount = pointCount,
            UseReferencePlane = usePlane,
        };

        string statsJson = JsonSerializer.Serialize(
            stats,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            }
        );

        context.Set("z_stats_json", statsJson);
        context.Set("avg_height", stats.AvgHeight);
        context.Set("centroid_z", centroidZ);
        context.Set("max_height", stats.MaxHeight);
        context.Set("min_height", stats.MinHeight);
        context.Set("std_height", stats.StdHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>高度统计结果 DTO。</summary>
public class ZChannelStats
{
    /// <summary>平均高度（距离均值）。</summary>
    public double AvgHeight { get; set; }

    /// <summary>最大高度（突起最高点）。</summary>
    public double MaxHeight { get; set; }

    /// <summary>最小高度（凹陷最低点）。</summary>
    public double MinHeight { get; set; }

    /// <summary>高度标准差（反映表面平整度）。</summary>
    public double StdHeight { get; set; }

    /// <summary>高度范围（Max - Min）。</summary>
    public double HeightRange { get; set; }

    /// <summary>点云点数。</summary>
    public int PointCount { get; set; }

    /// <summary>是否使用了参考平面校正（true=有符号距离，false=原始 Z 坐标）。</summary>
    public bool UseReferencePlane { get; set; }
}
