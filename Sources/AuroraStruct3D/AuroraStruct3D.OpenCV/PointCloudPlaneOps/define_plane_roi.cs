using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d420473b-76f1-455a-83e4-492809c23102")]
[Category("3D平面处理")]
[DisplayName("定义平面区域")]
[Description("在选中平面的局部 UV 坐标系中定义矩形或多边形 ROI。")]
public sealed class define_plane_roi : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "plane_params", DisplayName = "选中平面参数" },
            new PointCloudData { ParameterName = "plane_points", DisplayName = "选中平面内点" },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            new VisionParameter<string>
            {
                ParameterName = "plane_roi_json",
                DisplayName = "平面ROI",
                ParameterType = typeof(string),
            },
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "roiJson",
                DisplayName = "平面区域",
                ParameterType = typeof(string),
                DefaultValue = "{\"points\":[]}",
                Required = false,
                ControlType = PortControlType.PlaneRoiEditor,
            },
        ];

    private readonly string _roiJson;
    private bool _disposed;

    public define_plane_roi(string roiJson = "{\"points\":[]}") => _roiJson = roiJson;

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat plane =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException("选中平面参数为空。");
        PointCloudData cloud =
            context.Get<PointCloudData>("plane_points")
            ?? throw new InvalidOperationException("选中平面内点为空。");
        Mat points = cloud.PointCloud ?? throw new InvalidOperationException("选中平面内点为空。");
        PlaneGeometry geometry = PlaneGeometry.Create(plane, points);

        RoiInput? input = JsonSerializer.Deserialize<RoiInput>(
            _roiJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        List<UvPoint> polygon = input?.Points ?? [];
        if (polygon.Count == 0)
            polygon = geometry.ComputeBoundsPolygon(points);
        if (polygon.Count < 3)
            throw new InvalidOperationException("平面 ROI 至少需要 3 个点。");

        context.Set(
            "plane_roi_json",
            JsonSerializer.Serialize(
                new
                {
                    origin = geometry.Origin,
                    axisU = geometry.AxisU,
                    axisV = geometry.AxisV,
                    normal = geometry.Normal,
                    points = polygon,
                },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            )
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class RoiInput
    {
        public List<UvPoint> Points { get; set; } = [];
    }

    public sealed class UvPoint
    {
        public double U { get; set; }
        public double V { get; set; }
    }

    internal sealed class PlaneGeometry
    {
        public double[] Origin { get; init; } = [];
        public double[] AxisU { get; init; } = [];
        public double[] AxisV { get; init; } = [];
        public double[] Normal { get; init; } = [];

        public static PlaneGeometry Create(Mat plane, Mat points)
        {
            if (plane.Empty() || plane.Total() < 4)
                throw new InvalidOperationException("选中平面参数格式无效。");
            if (points.Empty() || points.Cols < 3 || points.Rows < 3)
                throw new InvalidOperationException("选中平面至少需要 3 个有效内点。");
            double a = plane.Get<double>(0, 0);
            double b = plane.Get<double>(1, 0);
            double c = plane.Get<double>(2, 0);
            double d = plane.Get<double>(3, 0);
            double n = Math.Sqrt(a * a + b * b + c * c);
            if (n < 1e-12)
                throw new InvalidOperationException("平面法向量无效。");
            a /= n; b /= n; c /= n; d /= n;
            double[] u = Math.Abs(c) < 0.9 ? Normalize([-b, a, 0]) : Normalize([0, -c, b]);
            double[] v = [b * u[2] - c * u[1], c * u[0] - a * u[2], a * u[1] - b * u[0]];
            double cx = 0, cy = 0, cz = 0;
            int rows = points.Rows;
            for (int i = 0; i < rows; i++)
            {
                cx += points.Get<float>(i, 0); cy += points.Get<float>(i, 1); cz += points.Get<float>(i, 2);
            }
            cx /= rows; cy /= rows; cz /= rows;
            double dist = a * cx + b * cy + c * cz + d;
            return new PlaneGeometry
            {
                Origin = [cx - dist * a, cy - dist * b, cz - dist * c],
                AxisU = u, AxisV = v, Normal = [a, b, c],
            };
        }

        public List<UvPoint> ComputeBoundsPolygon(Mat points)
        {
            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            int rows = points.Rows;
            for (int i = 0; i < rows; i++)
            {
                double dx = points.Get<float>(i, 0) - Origin[0];
                double dy = points.Get<float>(i, 1) - Origin[1];
                double dz = points.Get<float>(i, 2) - Origin[2];
                double u = dx * AxisU[0] + dy * AxisU[1] + dz * AxisU[2];
                double v = dx * AxisV[0] + dy * AxisV[1] + dz * AxisV[2];
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
            return [new(){U=minU,V=minV},new(){U=maxU,V=minV},new(){U=maxU,V=maxV},new(){U=minU,V=maxV}];
        }

        private static double[] Normalize(double[] p)
        {
            double n = Math.Sqrt(p.Sum(x => x * x));
            return p.Select(x => x / n).ToArray();
        }
    }
}
