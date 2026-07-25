using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d3f12345-6789-0123-4567-89012345670d")]
[Category("3D平面处理")]
[DisplayName("平面点云提取")]
[Description("根据平面参数从点云中提取平面内的点，支持距离阈值过滤。")]
public class extract_plane_points : IOperator
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
            new PointCloudData() { ParameterName = "plane_points", DisplayName = "平面点云" },
            new PointCloudData() { ParameterName = "non_plane_points", DisplayName = "非平面点云" },
            new VisionParameter<double>
            {
                ParameterName = "plane_normal_x",
                DisplayName = "法向量X",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "plane_normal_y",
                DisplayName = "法向量Y",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "plane_normal_z",
                DisplayName = "法向量Z",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "plane_d",
                DisplayName = "平面D值",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "plane_point_count",
                DisplayName = "平面点数",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "距离阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _distanceThreshold;
    private bool _disposed;

    public extract_plane_points(double distanceThreshold = 0.01)
    {
        _distanceThreshold = distanceThreshold;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat planeParams =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_params' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行平面点提取。");

        if (planeParams.Rows != 4 || planeParams.Cols != 1)
            throw new InvalidOperationException("平面参数格式错误，应为 (4, 1) 的 Mat。");

        double a = planeParams.Get<double>(0, 0);
        double b = planeParams.Get<double>(1, 0);
        double c = planeParams.Get<double>(2, 0);
        double d = planeParams.Get<double>(3, 0);

        double normalNorm = Math.Sqrt(a * a + b * b + c * c);
        if (normalNorm < 1e-10)
            throw new InvalidOperationException("平面法向量长度接近零，无法提取平面点。");

        a /= normalNorm;
        b /= normalNorm;
        c /= normalNorm;
        d /= normalNorm;

        List<int> inlierIndices = new();
        List<int> outlierIndices = new();

        int pointCount = pointCloud.Rows;
        for (int i = 0; i < pointCount; i++)
        {
            double x = pointCloud.Get<float>(i, 0);
            double y = pointCloud.Get<float>(i, 1);
            double z = pointCloud.Get<float>(i, 2);
            double dist = Math.Abs(a * x + b * y + c * z + d);

            if (dist <= _distanceThreshold)
                inlierIndices.Add(i);
            else
                outlierIndices.Add(i);
        }

        (var planePoints, var planeColors) = PointCloudUtils.ExtractSubset(
            input,
            inlierIndices,
            pointCloud
        );
        (var nonPlanePoints, var nonPlaneColors) = PointCloudUtils.ExtractSubset(
            input,
            outlierIndices,
            pointCloud
        );

        var planeOutput = PointCloudUtils.BuildCloud(planePoints, planeColors);
        var nonPlaneOutput = PointCloudUtils.BuildCloud(nonPlanePoints, nonPlaneColors);

        context.Set("plane_points", planeOutput);
        context.Set("non_plane_points", nonPlaneOutput);
        context.Set("plane_normal_x", a);
        context.Set("plane_normal_y", b);
        context.Set("plane_normal_z", c);
        context.Set("plane_d", d);
        context.Set("plane_point_count", inlierIndices.Count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}