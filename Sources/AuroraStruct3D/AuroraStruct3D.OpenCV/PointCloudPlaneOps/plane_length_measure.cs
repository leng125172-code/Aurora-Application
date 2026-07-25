using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d3f12345-6789-0123-4567-890123456710")]
[Category("3D平面处理")]
[DisplayName("平面长度测量")]
[Description("测量平面点云中两点之间的距离（平面内长度）。")]
public class plane_length_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "plane_points", DisplayName = "平面点云" },
            new VisionParameter<string>
            {
                ParameterName = "points_json",
                DisplayName = "测量点对",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "标注点云" },
            new VisionParameter<string>
            {
                ParameterName = "measurement_json",
                DisplayName = "测量结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "length",
                DisplayName = "长度",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("plane_points")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_points' 为空，请确认输入绑定已正确设置。"
            );

        string pointsJson =
            context.Get<string>("points_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'points_json' 为空，请确认输入绑定已正确设置。"
            );

        var points = JsonSerializer.Deserialize<List<Point3D>>(pointsJson, JsonOptions)
            ?? throw new InvalidOperationException("测量点JSON解析失败。");

        if (points.Count < 2)
            throw new InvalidOperationException("至少需要两个点才能测量长度。");

        Point3D p1 = points[0];
        Point3D p2 = points[1];

        double length = Distance3D(p1, p2);

        var measurement = new MeasurementInfo
        {
            Point1 = p1,
            Point2 = p2,
            Length = length,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_point_cloud", input);
        context.Set("measurement_json", json);
        context.Set("length", length);
    }

    private double Distance3D(Point3D p1, Point3D p2)
    {
        double dx = p1.X - p2.X;
        double dy = p1.Y - p2.Y;
        double dz = p1.Z - p2.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class MeasurementInfo
    {
        public Point3D Point1 { get; set; } = new();
        public Point3D Point2 { get; set; } = new();
        public double Length { get; set; }
    }

    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}