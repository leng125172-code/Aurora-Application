using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("a372b4a6-4cdf-4fa2-aa93-64a888e23105")]
[Category("3D拟合测量")]
[DisplayName("安装轴线与平面检测")]
[Description("检测销轴、螺柱、连接器等轴线相对安装基准面的分方向倾角。")]
public sealed class installation_axis_to_plane_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "reference_plane_params", DisplayName = "基准平面参数" },
            new PointCloudData { ParameterName = "reference_points", DisplayName = "基准面ROI点云" },
            new MatImg { ParameterName = "axis_params", DisplayName = "安装轴线参数" },
            new PointCloudData { ParameterName = "axis_points", DisplayName = "轴线ROI内点" },
        ];
    public static List<IVisionParameter>? OutputVisionParameters =>
        InspectionOutputs(
            ("tilt_x", "X方向倾角(度)"),
            ("tilt_y", "Y方向倾角(度)"),
            ("axis_to_normal_angle", "轴线与法向夹角(度)"),
            ("axis_to_plane_angle", "轴线与平面夹角(度)")
        );
    public static List<IConfigParameter>? ConfigParameters =>
        [
            NumberConfig("nominalTiltX", "X标称角度", 0),
            NumberConfig("nominalTiltY", "Y标称角度", 0),
            NumberConfig("maxDeviationX", "X最大绝对偏差", 0.5),
            NumberConfig("maxDeviationY", "Y最大绝对偏差", 0.5),
            NumberConfig("maxTotalDeviation", "最大合成偏差", 0.7),
            NumberConfig("maxFitRmse", "最大拟合RMSE", 0.05),
            IntegerConfig("minPointCount", "最小有效点数", 20),
        ];

    private readonly double _nominalX, _nominalY, _maxX, _maxY, _maxTotal, _maxRmse;
    private readonly int _minPoints;
    private bool _disposed;

    public installation_axis_to_plane_inspection(
        double nominalTiltX = 0, double nominalTiltY = 0,
        double maxDeviationX = 0.5, double maxDeviationY = 0.5,
        double maxTotalDeviation = 0.7, double maxFitRmse = 0.05, int minPointCount = 20)
    {
        if (maxDeviationX < 0 || maxDeviationY < 0 || maxTotalDeviation < 0 || maxFitRmse < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDeviationX));
        if (minPointCount < 2) throw new ArgumentOutOfRangeException(nameof(minPointCount));
        _nominalX = nominalTiltX; _nominalY = nominalTiltY;
        _maxX = maxDeviationX; _maxY = maxDeviationY; _maxTotal = maxTotalDeviation;
        _maxRmse = maxFitRmse; _minPoints = minPointCount;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat plane = InstallationAngleMath.RequireMat(context, "reference_plane_params", "基准平面参数");
        Mat axis = InstallationAngleMath.RequireMat(context, "axis_params", "安装轴线参数");
        Mat planePoints = InstallationAngleMath.RequirePoints(context, "reference_points", "基准面ROI点云");
        Mat axisPoints = InstallationAngleMath.RequirePoints(context, "axis_points", "轴线ROI内点");
        double[] normal = InstallationAngleMath.ReadPlaneNormal(plane, "基准平面");
        double[] direction = InstallationAngleMath.OrientToSameHemisphere(
            InstallationAngleMath.ReadDirection(axis, "安装轴线"), normal);
        (double[] x, double[] y) = InstallationAngleMath.BuildPlaneAxes(normal);
        double nz = Math.Clamp(InstallationAngleMath.Dot(direction, normal), -1, 1);
        double tiltX = InstallationAngleMath.Degrees(
            Math.Atan2(-InstallationAngleMath.Dot(direction, y), nz));
        double tiltY = InstallationAngleMath.Degrees(
            Math.Atan2(InstallationAngleMath.Dot(direction, x), nz));
        double normalAngle = InstallationAngleMath.Degrees(Math.Acos(nz));
        double planeAngle = 90 - normalAngle;
        double dx = tiltX - _nominalX, dy = tiltY - _nominalY;
        double total = Math.Sqrt(dx * dx + dy * dy);
        double planeRmse = InstallationAngleMath.PlaneRmse(plane, planePoints);
        double axisRmse = InstallationAngleMath.AxisFeatureRmse(axis, axisPoints);
        List<string> reasons = [];
        if (planePoints.Rows < _minPoints) reasons.Add("基准面点数不足");
        if (axisPoints.Rows < _minPoints) reasons.Add("轴线点数不足");
        if (planeRmse > _maxRmse) reasons.Add("基准面拟合残差过大");
        if (axisRmse > _maxRmse) reasons.Add("轴线拟合残差过大");
        bool valid = reasons.Count == 0;
        bool ok = valid && Math.Abs(dx) <= _maxX && Math.Abs(dy) <= _maxY && total <= _maxTotal;
        if (valid)
        {
            if (Math.Abs(dx) > _maxX)
                reasons.Add($"X方向偏差 {dx:F3}° 超过允许值 ±{_maxX:F3}°");
            if (Math.Abs(dy) > _maxY)
                reasons.Add($"Y方向偏差 {dy:F3}° 超过允许值 ±{_maxY:F3}°");
            if (total > _maxTotal)
                reasons.Add($"合成偏差 {total:F3}° 超过 {_maxTotal:F3}°");
        }
        string status = !valid ? "UNKNOWN" : ok ? "OK" : "NG";
        SetResult(context, status, valid, ok, new InstallationAxisInspectionDetails
        {
            tiltX = InstallationAngleMath.Round(tiltX), tiltY = InstallationAngleMath.Round(tiltY),
            axisToNormalAngle = InstallationAngleMath.Round(normalAngle),
            axisToPlaneAngle = InstallationAngleMath.Round(planeAngle),
            deviationX = InstallationAngleMath.Round(dx), deviationY = InstallationAngleMath.Round(dy),
            totalDeviation = InstallationAngleMath.Round(total), planeRmse = planeRmse,
            axisRmse = axisRmse, reasons = reasons
        });
    }

    internal static List<IVisionParameter> InspectionOutputs(params (string Name, string Display)[] numbers) =>
        [InspectionResults.Output<InstallationAxisInspectionDetails>("检测结果")];
    internal static ConfigParameter NumberConfig(string name, string display, double value) => new()
    { Name = name, DisplayName = display, ParameterType = typeof(double), DefaultValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture), ControlType = PortControlType.Input };
    internal static ConfigParameter IntegerConfig(string name, string display, int value) => new()
    { Name = name, DisplayName = display, ParameterType = typeof(int), DefaultValue = value.ToString(), ControlType = PortControlType.Input };
    internal static void SetResult(IWorkflowContext context, string status, bool valid, bool ok, InstallationAxisInspectionDetails detail)
        => context.Set("result", InspectionResults.CreateTyped(valid, ok, detail, status));
    public void Dispose() { if (_disposed) return; _disposed = true; GC.SuppressFinalize(this); }
}
