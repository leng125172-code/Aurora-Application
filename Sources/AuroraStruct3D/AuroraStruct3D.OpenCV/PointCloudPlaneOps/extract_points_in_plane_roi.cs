using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("e15fb284-2abe-477c-bd47-3cfde9c23103")]
[Category("3D平面处理")]
[DisplayName("提取平面区域点云")]
[Description("按平面厚度和局部 UV 区域从原始高密度点云提取全部点。")]
public sealed class extract_points_in_plane_roi : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "原始点云" },
            new MatImg { ParameterName = "plane_params", DisplayName = "选中平面参数" },
            new VisionParameter<string>
            {
                ParameterName = "plane_roi_json",
                DisplayName = "平面ROI",
                ParameterType = typeof(string),
            },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            new PointCloudData { ParameterName = "selected_points", DisplayName = "区域内全部点" },
            new PointCloudData { ParameterName = "remaining_points", DisplayName = "区域外点" },
            new VisionParameter<int>
            {
                ParameterName = "selected_count",
                DisplayName = "选中点数",
                ParameterType = typeof(int),
            },
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "planeDistanceThreshold",
                DisplayName = "平面厚度阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
        ];

    private readonly double _threshold;
    private bool _disposed;

    public extract_points_in_plane_roi(double planeDistanceThreshold = 0.01)
    {
        if (!double.IsFinite(planeDistanceThreshold) || planeDistanceThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(planeDistanceThreshold));
        _threshold = planeDistanceThreshold;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PointCloudData input = context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException("原始点云为空。");
        Mat cloud = input.PointCloud ?? throw new InvalidOperationException("原始点云为空。");
        Mat plane = context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException("选中平面参数为空。");
        string roiJson = context.Get<string>("plane_roi_json")
            ?? throw new InvalidOperationException("平面 ROI 为空。");
        PlaneRoi roi = JsonSerializer.Deserialize<PlaneRoi>(
            roiJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? throw new InvalidOperationException("平面 ROI 格式无效。");
        if (roi.Points.Count < 3)
            throw new InvalidOperationException("平面 ROI 至少需要 3 个点。");
        if (roi.Origin.Length != 3 || roi.AxisU.Length != 3 || roi.AxisV.Length != 3)
            throw new InvalidOperationException("平面 ROI 坐标系格式无效。");
        if (plane.Empty() || plane.Total() < 4)
            throw new InvalidOperationException("选中平面参数格式无效。");

        double a = plane.Get<double>(0, 0), b = plane.Get<double>(1, 0);
        double c = plane.Get<double>(2, 0), d = plane.Get<double>(3, 0);
        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-12)
            throw new InvalidOperationException("选中平面法向量无效。");
        a /= norm; b /= norm; c /= norm; d /= norm;
        List<int> selected = [], remaining = [];
        int rows = cloud.Rows;
        for (int i = 0; i < rows; i++)
        {
            double x = cloud.Get<float>(i, 0), y = cloud.Get<float>(i, 1), z = cloud.Get<float>(i, 2);
            double distance = Math.Abs(a * x + b * y + c * z + d);
            double dx = x - roi.Origin[0], dy = y - roi.Origin[1], dz = z - roi.Origin[2];
            double u = dx * roi.AxisU[0] + dy * roi.AxisU[1] + dz * roi.AxisU[2];
            double v = dx * roi.AxisV[0] + dy * roi.AxisV[1] + dz * roi.AxisV[2];
            (distance <= _threshold && Contains(roi.Points, u, v) ? selected : remaining).Add(i);
        }

        (Mat selectedMat, Mat? selectedColors) = PointCloudUtils.ExtractSubset(input, selected, cloud);
        (Mat remainingMat, Mat? remainingColors) = PointCloudUtils.ExtractSubset(input, remaining, cloud);
        context.Set("selected_points", PointCloudUtils.BuildCloud(selectedMat, selectedColors));
        context.Set("remaining_points", PointCloudUtils.BuildCloud(remainingMat, remainingColors));
        context.Set("selected_count", selected.Count);
    }

    private static bool Contains(List<UvPoint> polygon, double u, double v)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            UvPoint pi = polygon[i], pj = polygon[j];
            if ((pi.V > v) != (pj.V > v)
                && u < (pj.U - pi.U) * (v - pi.V) / (pj.V - pi.V) + pi.U)
                inside = !inside;
        }
        return inside;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class PlaneRoi
    {
        public double[] Origin { get; set; } = [];
        public double[] AxisU { get; set; } = [];
        public double[] AxisV { get; set; } = [];
        public List<UvPoint> Points { get; set; } = [];
    }
    private sealed class UvPoint
    {
        public double U { get; set; }
        public double V { get; set; }
    }
}
