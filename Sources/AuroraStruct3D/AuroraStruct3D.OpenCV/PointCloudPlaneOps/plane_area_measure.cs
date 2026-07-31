using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d3f12345-6789-0123-4567-890123456711")]
[Category("3D平面处理")]
[DisplayName("平面面积测量")]
[Description("测量平面点云中多边形或轮廓的面积。")]
public class plane_area_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "plane_points", DisplayName = "平面点云" },
            new MatImg() { ParameterName = "plane_params", DisplayName = "平面参数" },
            new VisionParameter<string>
            {
                ParameterName = "contour_json",
                DisplayName = "轮廓点",
                ParameterType = typeof(string),
                ControlType = PortControlType.Input,
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
                ParameterName = "area",
                DisplayName = "面积",
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

        Mat planeParams =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_params' 为空，请确认输入绑定已正确设置。"
            );

        string contourJson =
            context.Get<string>("contour_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'contour_json' 为空，请确认输入绑定已正确设置。"
            );

        var contourPoints = JsonSerializer.Deserialize<List<Point3D>>(contourJson, JsonOptions)
            ?? throw new InvalidOperationException("轮廓点JSON解析失败。");

        if (contourPoints.Count < 3)
            throw new InvalidOperationException("至少需要三个点才能计算面积。");

        double a = planeParams.Get<double>(0, 0);
        double b = planeParams.Get<double>(1, 0);
        double c = planeParams.Get<double>(2, 0);

        double area = ComputePolygonArea3D(contourPoints, a, b, c);

        (double cx, double cy, double cz) = Centroid3D(contourPoints);

        var measurement = new MeasurementInfo
        {
            Area = area,
            Centroid = new Point3D { X = cx, Y = cy, Z = cz },
            PointCount = contourPoints.Count,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_point_cloud", input);
        context.Set("measurement_json", json);
        context.Set("area", area);
    }

    private static double ComputePolygonArea3D(
        List<Point3D> points,
        double nx,
        double ny,
        double nz
    )
    {
        if (points.Count < 3)
            return 0;

        double normalNorm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (!double.IsFinite(normalNorm) || normalNorm < 1e-12)
            throw new InvalidOperationException("平面法向量无效，无法计算面积。");
        nx /= normalNorm;
        ny /= normalNorm;
        nz /= normalNorm;

        double vectorAreaX = 0;
        double vectorAreaY = 0;
        double vectorAreaZ = 0;
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Point3D p = points[i];
            Point3D q = points[j];
            if (
                !double.IsFinite(p.X)
                || !double.IsFinite(p.Y)
                || !double.IsFinite(p.Z)
                || !double.IsFinite(q.X)
                || !double.IsFinite(q.Y)
                || !double.IsFinite(q.Z)
            )
                throw new InvalidOperationException("轮廓包含非有限坐标，无法计算面积。");

            vectorAreaX += p.Y * q.Z - p.Z * q.Y;
            vectorAreaY += p.Z * q.X - p.X * q.Z;
            vectorAreaZ += p.X * q.Y - p.Y * q.X;
        }

        return 0.5
            * Math.Abs(vectorAreaX * nx + vectorAreaY * ny + vectorAreaZ * nz);
    }

    private (double x, double y, double z) Centroid3D(List<Point3D> points)
    {
        double cx = 0, cy = 0, cz = 0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
            cz += p.Z;
        }
        int n = points.Count;
        return (cx / n, cy / n, cz / n);
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
        public double Area { get; set; }
        public Point3D Centroid { get; set; } = new();
        public int PointCount { get; set; }
    }

    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
