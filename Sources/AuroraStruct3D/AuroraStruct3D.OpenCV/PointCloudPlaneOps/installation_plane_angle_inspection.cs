using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("f261a395-3bcf-4e91-9a82-53f777e23104")]
[Category("3D拟合测量")]
[DisplayName("安装平面角度检测")]
[Description("根据基准面和安装面两个 ROI 的局部拟合结果，检测分方向安装倾角并输出 OK、NG 或 UNKNOWN。")]
public sealed class installation_plane_angle_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "reference_plane_params", DisplayName = "基准平面参数" },
            new PointCloudData { ParameterName = "reference_points", DisplayName = "基准面ROI点云" },
            new MatImg { ParameterName = "measured_plane_params", DisplayName = "安装平面参数" },
            new PointCloudData { ParameterName = "measured_points", DisplayName = "安装面ROI点云" },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [InspectionResults.Output<InstallationPlaneInspectionDetails>("安装角度结果")];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            NumberConfig("nominalTiltX", "X标称角度", 0),
            NumberConfig("nominalTiltY", "Y标称角度", 0),
            NumberConfig("minDeviationX", "X最小偏差", -0.5),
            NumberConfig("maxDeviationX", "X最大偏差", 0.5),
            NumberConfig("minDeviationY", "Y最小偏差", -0.5),
            NumberConfig("maxDeviationY", "Y最大偏差", 0.5),
            NumberConfig("maxTotalDeviation", "最大合成偏差", 0.7),
            NumberConfig("maxFitRmse", "最大拟合RMSE", 0.05),
            new ConfigParameter
            {
                Name = "minPointCount",
                DisplayName = "最小有效点数",
                ParameterType = typeof(int),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
        ];

    private readonly double _nominalTiltX;
    private readonly double _nominalTiltY;
    private readonly double _minDeviationX;
    private readonly double _maxDeviationX;
    private readonly double _minDeviationY;
    private readonly double _maxDeviationY;
    private readonly double _maxTotalDeviation;
    private readonly double _maxFitRmse;
    private readonly int _minPointCount;
    private bool _disposed;

    public installation_plane_angle_inspection(
        double nominalTiltX = 0,
        double nominalTiltY = 0,
        double minDeviationX = -0.5,
        double maxDeviationX = 0.5,
        double minDeviationY = -0.5,
        double maxDeviationY = 0.5,
        double maxTotalDeviation = 0.7,
        double maxFitRmse = 0.05,
        int minPointCount = 30)
    {
        ValidateFinite(nominalTiltX, nameof(nominalTiltX));
        ValidateFinite(nominalTiltY, nameof(nominalTiltY));
        ValidateRange(minDeviationX, maxDeviationX, "X方向偏差");
        ValidateRange(minDeviationY, maxDeviationY, "Y方向偏差");
        if (!double.IsFinite(maxTotalDeviation) || maxTotalDeviation < 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalDeviation));
        if (!double.IsFinite(maxFitRmse) || maxFitRmse < 0)
            throw new ArgumentOutOfRangeException(nameof(maxFitRmse));
        if (minPointCount < 3)
            throw new ArgumentOutOfRangeException(nameof(minPointCount));

        _nominalTiltX = nominalTiltX;
        _nominalTiltY = nominalTiltY;
        _minDeviationX = minDeviationX;
        _maxDeviationX = maxDeviationX;
        _minDeviationY = minDeviationY;
        _maxDeviationY = maxDeviationY;
        _maxTotalDeviation = maxTotalDeviation;
        _maxFitRmse = maxFitRmse;
        _minPointCount = minPointCount;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat referencePlane = RequirePlane(context, "reference_plane_params", "基准平面");
        Mat measuredPlane = RequirePlane(context, "measured_plane_params", "安装平面");
        Mat referencePoints = RequirePoints(context, "reference_points", "基准面 ROI");
        Mat measuredPoints = RequirePoints(context, "measured_points", "安装面 ROI");

        double[] referenceNormal = ReadNormalizedPlane(referencePlane);
        double[] measuredNormal = ReadNormalizedPlane(measuredPlane);
        if (Dot(referenceNormal, measuredNormal) < 0)
            measuredNormal = Scale(measuredNormal, -1);

        double[] axisX = BuildReferenceAxisX(referenceNormal);
        double[] axisY = Normalize(Cross(referenceNormal, axisX));
        double normalX = Dot(measuredNormal, axisX);
        double normalY = Dot(measuredNormal, axisY);
        double normalZ = Math.Clamp(Dot(measuredNormal, referenceNormal), -1, 1);

        double tiltX = RadiansToDegrees(Math.Atan2(-normalY, normalZ));
        double tiltY = RadiansToDegrees(Math.Atan2(normalX, normalZ));
        double totalTilt = RadiansToDegrees(Math.Acos(normalZ));
        double deviationX = tiltX - _nominalTiltX;
        double deviationY = tiltY - _nominalTiltY;
        double totalDeviation = Math.Sqrt(deviationX * deviationX + deviationY * deviationY);
        double referenceRmse = ComputeRmse(referencePlane, referencePoints);
        double measuredRmse = ComputeRmse(measuredPlane, measuredPoints);

        List<string> qualityReasons = [];
        if (referencePoints.Rows < _minPointCount)
            qualityReasons.Add($"基准面点数 {referencePoints.Rows} 小于 {_minPointCount}");
        if (measuredPoints.Rows < _minPointCount)
            qualityReasons.Add($"安装面点数 {measuredPoints.Rows} 小于 {_minPointCount}");
        if (referenceRmse > _maxFitRmse)
            qualityReasons.Add($"基准面 RMSE {referenceRmse:F6} 超过 {_maxFitRmse:F6}");
        if (measuredRmse > _maxFitRmse)
            qualityReasons.Add($"安装面 RMSE {measuredRmse:F6} 超过 {_maxFitRmse:F6}");

        bool isValid = qualityReasons.Count == 0;
        bool isOk = isValid
            && deviationX >= _minDeviationX
            && deviationX <= _maxDeviationX
            && deviationY >= _minDeviationY
            && deviationY <= _maxDeviationY
            && totalDeviation <= _maxTotalDeviation;
        string status = !isValid ? "UNKNOWN" : isOk ? "OK" : "NG";

        context.Set("result", InspectionResults.CreateTyped(isValid, isOk,
                new InstallationPlaneInspectionDetails(
                    status,
                    isValid,
                    isOk,
                    Round(tiltX), Round(tiltY), Round(totalTilt),
                    _nominalTiltX, _nominalTiltY,
                    Round(deviationX), Round(deviationY), Round(totalDeviation),
                    Round(referenceRmse), Round(measuredRmse),
                    referencePoints.Rows, measuredPoints.Rows,
                    qualityReasons,
                    new InstallationPlaneToleranceDetails(_minDeviationX, _maxDeviationX,
                        _minDeviationY, _maxDeviationY, _maxTotalDeviation, _maxFitRmse,
                        _minPointCount)), status));
    }

    private static VisionParameter<double> NumberOutput(string name, string displayName) =>
        new() { ParameterName = name, DisplayName = displayName, ParameterType = typeof(double) };

    private static ConfigParameter NumberConfig(string name, string displayName, double value) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = typeof(double),
            DefaultValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Required = false,
            ControlType = PortControlType.Input,
        };

    private static Mat RequirePlane(IWorkflowContext context, string name, string displayName)
    {
        Mat plane = context.Get<Mat>(name)
            ?? throw new InvalidOperationException($"{displayName}参数未连接。");
        if (plane.Empty() || plane.Total() < 4 || plane.Type() != MatType.CV_64FC1)
            throw new InvalidOperationException($"{displayName}参数必须为包含 a、b、c、d 的 CV_64FC1 矩阵。");
        return plane;
    }

    private static Mat RequirePoints(IWorkflowContext context, string name, string displayName)
    {
        PointCloudData cloud = context.Get<PointCloudData>(name)
            ?? throw new InvalidOperationException($"{displayName}点云未连接。");
        Mat points = cloud.PointCloud
            ?? throw new InvalidOperationException($"{displayName}点云为空。");
        if (points.Empty() || points.Cols < 3)
            throw new InvalidOperationException($"{displayName}点云格式无效。");
        return points;
    }

    private static double[] ReadNormalizedPlane(Mat plane)
    {
        double[] normal = [plane.Get<double>(0, 0), plane.Get<double>(1, 0), plane.Get<double>(2, 0)];
        return Normalize(normal);
    }

    private static double ComputeRmse(Mat plane, Mat points)
    {
        double a = plane.Get<double>(0, 0);
        double b = plane.Get<double>(1, 0);
        double c = plane.Get<double>(2, 0);
        double d = plane.Get<double>(3, 0);
        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-12)
            return double.PositiveInfinity;
        double sumSquares = 0;
        int rows = points.Rows;
        for (int i = 0; i < rows; i++)
        {
            double distance = (
                a * points.Get<float>(i, 0)
                + b * points.Get<float>(i, 1)
                + c * points.Get<float>(i, 2)
                + d
            ) / norm;
            sumSquares += distance * distance;
        }
        return Math.Sqrt(sumSquares / rows);
    }

    private static double[] BuildReferenceAxisX(double[] normal)
    {
        double[] candidate = [1, 0, 0];
        if (Math.Abs(Dot(candidate, normal)) > 0.95)
            candidate = [0, 1, 0];
        return Normalize(Subtract(candidate, Scale(normal, Dot(candidate, normal))));
    }

    private static double[] Normalize(double[] vector)
    {
        double norm = Math.Sqrt(Dot(vector, vector));
        if (norm < 1e-12)
            throw new InvalidOperationException("平面法向量或参考坐标轴无效。");
        return Scale(vector, 1 / norm);
    }

    private static double Dot(double[] a, double[] b) =>
        a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double[] Scale(double[] vector, double scale) =>
        [vector[0] * scale, vector[1] * scale, vector[2] * scale];

    private static double[] Subtract(double[] a, double[] b) =>
        [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;
    private static double Round(double value) => Math.Round(value, 6);
    private static void SetNumber(IWorkflowContext context, string name, double value) =>
        context.Set(name, Round(value));

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateRange(double minimum, double maximum, string displayName)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentException($"{displayName}范围无效。");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
