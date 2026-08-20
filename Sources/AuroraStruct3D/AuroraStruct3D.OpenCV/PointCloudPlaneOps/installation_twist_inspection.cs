namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("c594d6c8-6ef2-41c4-8cb5-86caaae23107")]
[Category("3D拟合测量")]
[DisplayName("安装平面内旋转检测")]
[Description("将基准和安装方向投影到基准平面，检测绕基准法向的有符号 twistZ 旋转。")]
public sealed class installation_twist_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "reference_plane_params", DisplayName = "基准平面参数" },
            new MatImg { ParameterName = "reference_direction_params", DisplayName = "基准方向参数" },
            new PointCloudData { ParameterName = "reference_direction_points", DisplayName = "基准方向内点" },
            new MatImg { ParameterName = "measured_direction_params", DisplayName = "安装方向参数" },
            new PointCloudData { ParameterName = "measured_direction_points", DisplayName = "安装方向内点" },
        ];
    public static List<IVisionParameter>? OutputVisionParameters =>
        installation_axis_to_plane_inspection.InspectionOutputs(
            ("twist_z", "平面内旋转角(度)"), ("twist_deviation", "旋转角偏差(度)"));
    public static List<IConfigParameter>? ConfigParameters =>
        [
            installation_axis_to_plane_inspection.NumberConfig("nominalTwist", "标称旋转角", 0),
            installation_axis_to_plane_inspection.NumberConfig("maxTwistDeviation", "最大旋转角偏差", 0.5),
            installation_axis_to_plane_inspection.NumberConfig("maxFitRmse", "最大拟合RMSE", 0.05),
            installation_axis_to_plane_inspection.IntegerConfig("minPointCount", "最小有效点数", 20),
        ];
    private readonly double _nominal, _maxDeviation, _maxRmse;
    private readonly int _minPoints;
    private bool _disposed;
    public installation_twist_inspection(
        double nominalTwist = 0, double maxTwistDeviation = 0.5,
        double maxFitRmse = 0.05, int minPointCount = 20)
    {
        if (!double.IsFinite(nominalTwist) || maxTwistDeviation < 0 || maxFitRmse < 0)
            throw new ArgumentOutOfRangeException(nameof(nominalTwist));
        if (minPointCount < 2) throw new ArgumentOutOfRangeException(nameof(minPointCount));
        _nominal = nominalTwist; _maxDeviation = maxTwistDeviation;
        _maxRmse = maxFitRmse; _minPoints = minPointCount;
    }
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat plane = InstallationAngleMath.RequireMat(context, "reference_plane_params", "基准平面");
        Mat reference = InstallationAngleMath.RequireMat(context, "reference_direction_params", "基准方向");
        Mat measured = InstallationAngleMath.RequireMat(context, "measured_direction_params", "安装方向");
        Mat referencePoints = InstallationAngleMath.RequirePoints(context, "reference_direction_points", "基准方向内点");
        Mat measuredPoints = InstallationAngleMath.RequirePoints(context, "measured_direction_points", "安装方向内点");
        double[] normal = InstallationAngleMath.ReadPlaneNormal(plane, "基准平面");
        double[] referenceDirection = InstallationAngleMath.ReadDirection(reference, "基准方向");
        double[] measuredDirection = InstallationAngleMath.ReadDirection(measured, "安装方向");
        if (InstallationAngleMath.Dot(referenceDirection, measuredDirection) < 0)
            measuredDirection = InstallationAngleMath.Scale(measuredDirection, -1);
        double twist = InstallationAngleMath.SignedAngle(referenceDirection, measuredDirection, normal);
        double deviation = NormalizeAngle(twist - _nominal);
        double referenceRmse = InstallationAngleMath.LineRmse(reference, referencePoints);
        double measuredRmse = InstallationAngleMath.LineRmse(measured, measuredPoints);
        List<string> reasons = [];
        if (referencePoints.Rows < _minPoints) reasons.Add("基准方向点数不足");
        if (measuredPoints.Rows < _minPoints) reasons.Add("安装方向点数不足");
        if (referenceRmse > _maxRmse) reasons.Add("基准方向拟合残差过大");
        if (measuredRmse > _maxRmse) reasons.Add("安装方向拟合残差过大");
        bool valid = reasons.Count == 0;
        bool ok = valid && Math.Abs(deviation) <= _maxDeviation;
        string status = !valid ? "UNKNOWN" : ok ? "OK" : "NG";
        installation_axis_to_plane_inspection.SetResult(context, status, valid, ok,
            new InstallationAxisInspectionDetails { twistZ = InstallationAngleMath.Round(twist), twistDeviation = InstallationAngleMath.Round(deviation), referenceRmse = referenceRmse, measuredRmse = measuredRmse, reasons = reasons });
    }
    private static double NormalizeAngle(double value)
    {
        while (value > 180) value -= 360;
        while (value <= -180) value += 360;
        return value;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; GC.SuppressFinalize(this); }
}
