namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("b483c5b7-5de1-40b3-bba4-75b999e23106")]
[Category("3D拟合测量")]
[DisplayName("安装轴线夹角检测")]
[Description("检测两根轴、管件或连接器轴线之间的最小夹角及公差。")]
public sealed class installation_axis_to_axis_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "reference_axis_params", DisplayName = "基准轴线参数" },
            new PointCloudData { ParameterName = "reference_axis_points", DisplayName = "基准轴线内点" },
            new MatImg { ParameterName = "measured_axis_params", DisplayName = "安装轴线参数" },
            new PointCloudData { ParameterName = "measured_axis_points", DisplayName = "安装轴线内点" },
        ];
    public static List<IVisionParameter>? OutputVisionParameters =>
        installation_axis_to_plane_inspection.InspectionOutputs(
            ("axis_angle", "轴线最小夹角(度)"), ("angle_deviation", "角度偏差(度)"));
    public static List<IConfigParameter>? ConfigParameters =>
        [
            installation_axis_to_plane_inspection.NumberConfig("nominalAngle", "标称夹角", 0),
            installation_axis_to_plane_inspection.NumberConfig("maxAngleDeviation", "最大角度偏差", 0.5),
            installation_axis_to_plane_inspection.NumberConfig("maxFitRmse", "最大拟合RMSE", 0.05),
            installation_axis_to_plane_inspection.IntegerConfig("minPointCount", "最小有效点数", 20),
        ];
    private readonly double _nominal, _maxDeviation, _maxRmse;
    private readonly int _minPoints;
    private bool _disposed;
    public installation_axis_to_axis_inspection(
        double nominalAngle = 0, double maxAngleDeviation = 0.5,
        double maxFitRmse = 0.05, int minPointCount = 20)
    {
        if (nominalAngle < 0 || nominalAngle > 90 || maxAngleDeviation < 0 || maxFitRmse < 0)
            throw new ArgumentOutOfRangeException(nameof(nominalAngle));
        if (minPointCount < 2) throw new ArgumentOutOfRangeException(nameof(minPointCount));
        _nominal = nominalAngle; _maxDeviation = maxAngleDeviation;
        _maxRmse = maxFitRmse; _minPoints = minPointCount;
    }
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat reference = InstallationAngleMath.RequireMat(context, "reference_axis_params", "基准轴线");
        Mat measured = InstallationAngleMath.RequireMat(context, "measured_axis_params", "安装轴线");
        Mat referencePoints = InstallationAngleMath.RequirePoints(context, "reference_axis_points", "基准轴线内点");
        Mat measuredPoints = InstallationAngleMath.RequirePoints(context, "measured_axis_points", "安装轴线内点");
        double angle = InstallationAngleMath.AcuteAxisAngle(
            InstallationAngleMath.ReadDirection(reference, "基准轴线"),
            InstallationAngleMath.ReadDirection(measured, "安装轴线"));
        double deviation = angle - _nominal;
        double referenceRmse = InstallationAngleMath.AxisFeatureRmse(reference, referencePoints);
        double measuredRmse = InstallationAngleMath.AxisFeatureRmse(measured, measuredPoints);
        List<string> reasons = [];
        if (referencePoints.Rows < _minPoints) reasons.Add("基准轴线点数不足");
        if (measuredPoints.Rows < _minPoints) reasons.Add("安装轴线点数不足");
        if (referenceRmse > _maxRmse) reasons.Add("基准轴线拟合残差过大");
        if (measuredRmse > _maxRmse) reasons.Add("安装轴线拟合残差过大");
        bool valid = reasons.Count == 0;
        bool ok = valid && Math.Abs(deviation) <= _maxDeviation;
        string status = !valid ? "UNKNOWN" : ok ? "OK" : "NG";
        installation_axis_to_plane_inspection.Set(context, "axis_angle", angle);
        installation_axis_to_plane_inspection.Set(context, "angle_deviation", deviation);
        installation_axis_to_plane_inspection.SetResult(context, status, valid, ok,
            new { axisAngle = InstallationAngleMath.Round(angle), angleDeviation = InstallationAngleMath.Round(deviation), referenceRmse, measuredRmse, reasons });
    }
    public void Dispose() { if (_disposed) return; _disposed = true; GC.SuppressFinalize(this); }
}
