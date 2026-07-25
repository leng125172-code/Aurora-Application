namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

internal static class InstallationAngleMath
{
    public static double[] ReadPlaneNormal(Mat plane, string displayName)
    {
        RequireMatrix(plane, 4, displayName);
        return Normalize([plane.Get<double>(0, 0), plane.Get<double>(1, 0), plane.Get<double>(2, 0)]);
    }

    public static double[] ReadDirection(Mat parameters, string displayName)
    {
        RequireMatrix(parameters, 6, displayName);
        return Normalize(
            [parameters.Get<double>(3, 0), parameters.Get<double>(4, 0), parameters.Get<double>(5, 0)]
        );
    }

    public static double[] OrientToSameHemisphere(double[] direction, double[] reference) =>
        Dot(direction, reference) < 0 ? Scale(direction, -1) : direction;

    public static (double[] X, double[] Y) BuildPlaneAxes(double[] normal)
    {
        double[] candidate = Math.Abs(normal[0]) <= 0.95 ? [1, 0, 0] : [0, 1, 0];
        double[] x = Normalize(Subtract(candidate, Scale(normal, Dot(candidate, normal))));
        return (x, Normalize(Cross(normal, x)));
    }

    public static double SignedAngle(double[] from, double[] to, double[] normal)
    {
        double[] a = Normalize(Subtract(from, Scale(normal, Dot(from, normal))));
        double[] b = Normalize(Subtract(to, Scale(normal, Dot(to, normal))));
        return Degrees(Math.Atan2(Dot(normal, Cross(a, b)), Math.Clamp(Dot(a, b), -1, 1)));
    }

    public static double AcuteAxisAngle(double[] a, double[] b) =>
        Degrees(Math.Acos(Math.Clamp(Math.Abs(Dot(a, b)), -1, 1)));

    public static double PlaneRmse(Mat plane, Mat points)
    {
        double a = plane.Get<double>(0, 0), b = plane.Get<double>(1, 0);
        double c = plane.Get<double>(2, 0), d = plane.Get<double>(3, 0);
        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-12 || points.Rows == 0)
            return double.PositiveInfinity;
        double sum = 0;
        int rows = points.Rows;
        for (int i = 0; i < rows; i++)
        {
            double distance = (
                a * points.Get<float>(i, 0)
                + b * points.Get<float>(i, 1)
                + c * points.Get<float>(i, 2)
                + d
            ) / norm;
            sum += distance * distance;
        }
        return Math.Sqrt(sum / rows);
    }

    public static double LineRmse(Mat line, Mat points)
    {
        double[] origin = [line.Get<double>(0, 0), line.Get<double>(1, 0), line.Get<double>(2, 0)];
        double[] direction = ReadDirection(line, "轴线");
        if (points.Rows == 0)
            return double.PositiveInfinity;
        double sum = 0;
        int rows = points.Rows;
        for (int i = 0; i < rows; i++)
        {
            double[] offset =
            [
                points.Get<float>(i, 0) - origin[0],
                points.Get<float>(i, 1) - origin[1],
                points.Get<float>(i, 2) - origin[2],
            ];
            double distance = Length(Cross(offset, direction));
            sum += distance * distance;
        }
        return Math.Sqrt(sum / rows);
    }

    public static double AxisFeatureRmse(Mat parameters, Mat points)
    {
        if (parameters.Total() < 7)
            return LineRmse(parameters, points);

        RequireMatrix(parameters, 7, "圆柱");
        double[] origin =
        [
            parameters.Get<double>(0, 0),
            parameters.Get<double>(1, 0),
            parameters.Get<double>(2, 0),
        ];
        double[] direction = ReadDirection(parameters, "圆柱轴线");
        double radius = parameters.Get<double>(6, 0);
        if (!double.IsFinite(radius) || radius <= 0 || points.Rows == 0)
            return double.PositiveInfinity;
        double sum = 0;
        int rows = points.Rows;
        for (int i = 0; i < rows; i++)
        {
            double[] offset =
            [
                points.Get<float>(i, 0) - origin[0],
                points.Get<float>(i, 1) - origin[1],
                points.Get<float>(i, 2) - origin[2],
            ];
            double radialResidual = Length(Cross(offset, direction)) - radius;
            sum += radialResidual * radialResidual;
        }
        return Math.Sqrt(sum / rows);
    }

    public static Mat RequireMat(IWorkflowContext context, string name, string displayName)
    {
        Mat value = context.Get<Mat>(name)
            ?? throw new InvalidOperationException($"{displayName}未连接。");
        return value;
    }

    public static Mat RequirePoints(IWorkflowContext context, string name, string displayName)
    {
        PointCloudData cloud = context.Get<PointCloudData>(name)
            ?? throw new InvalidOperationException($"{displayName}未连接。");
        Mat points = cloud.PointCloud ?? throw new InvalidOperationException($"{displayName}为空。");
        if (points.Empty() || points.Cols < 3)
            throw new InvalidOperationException($"{displayName}格式无效。");
        return points;
    }

    public static void RequireMatrix(Mat matrix, long minimumTotal, string displayName)
    {
        if (matrix.Empty() || matrix.Total() < minimumTotal || matrix.Type() != MatType.CV_64FC1)
            throw new InvalidOperationException($"{displayName}必须是 CV_64FC1 参数矩阵。");
    }

    public static double Dot(double[] a, double[] b) =>
        a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    public static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    public static double[] Scale(double[] value, double factor) =>
        [value[0] * factor, value[1] * factor, value[2] * factor];
    public static double[] Subtract(double[] a, double[] b) =>
        [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    public static double Length(double[] value) => Math.Sqrt(Dot(value, value));
    public static double[] Normalize(double[] value)
    {
        double length = Length(value);
        if (length < 1e-12)
            throw new InvalidOperationException("方向向量无效。");
        return Scale(value, 1 / length);
    }
    public static double Degrees(double radians) => radians * 180 / Math.PI;
    public static double Round(double value) => Math.Round(value, 6);
}
