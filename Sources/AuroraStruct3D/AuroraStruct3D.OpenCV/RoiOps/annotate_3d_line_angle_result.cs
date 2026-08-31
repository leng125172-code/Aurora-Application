using System.Globalization;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow;

namespace AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 在点云 XY 投影图上绘制两条 3D 拟合线段、延长线、交点和空间夹角。
/// </summary>
[Guid("6ea79db8-4b77-4d63-9c1c-12fc5c608a01")]
[Category("3D拟合测量")]
[DisplayName("3D双直线夹角标注")]
[Description("将两条3D拟合线投影到结果图，绘制线段、延长线、交点和空间夹角。")]
public sealed class annotate_3d_line_angle_result : IOperator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "input_mat", DisplayName = "点云投影图" },
            new VisionParameter<string>
            {
                ParameterName = "projection_mapping",
                ParameterType = typeof(string),
                DisplayName = "投影映射",
                ControlType = PortControlType.Variable,
            },
            new MatImg { ParameterName = "line1_params", DisplayName = "直线1参数" },
            new PointCloudData { ParameterName = "line1_points", DisplayName = "直线1内点" },
            new MatImg { ParameterName = "line2_params", DisplayName = "直线2参数" },
            new PointCloudData { ParameterName = "line2_points", DisplayName = "直线2内点" },
            new VisionParameter<InspectionResult<InstallationAxisInspectionDetails>>
            {
                ParameterName = "inspection_result",
                ParameterType = typeof(InspectionResult<InstallationAxisInspectionDetails>),
                JsonSchema = WorkflowJsonSchema.For<InspectionResult<InstallationAxisInspectionDetails>>(),
                DisplayName = "夹角检测结果",
                ControlType = PortControlType.Variable,
            },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [new MatImg { ParameterName = "output_mat", DisplayName = "夹角标注图" }];

    public static List<IConfigParameter>? ConfigParameters => [];

    private bool _disposed;

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat input = context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException("点云投影图未连接。");
        if (input.Empty())
            throw new InvalidOperationException("点云投影图为空。");

        RoiProjectionMapping mapping = ReadMapping(context.Get<string>("projection_mapping"));
        Mat line1Params = RequireMat(context, "line1_params", "直线1参数");
        Mat line2Params = RequireMat(context, "line2_params", "直线2参数");
        Mat line1Points = RequirePoints(context, "line1_points", "直线1内点");
        Mat line2Points = RequirePoints(context, "line2_points", "直线2内点");
        InspectionResult<InstallationAxisInspectionDetails> inspection =
            context.Get<InspectionResult<InstallationAxisInspectionDetails>>("inspection_result")
            ?? throw new InvalidOperationException("夹角检测结果未连接。");

        FittedLine line1 = ReadLine(line1Params, line1Points, mapping, "直线1");
        FittedLine line2 = ReadLine(line2Params, line2Points, mapping, "直线2");
        Mat output = EnsureBgra(input);

        Scalar line1Color = new(80, 220, 80, 255);
        Scalar line2Color = new(0, 165, 255, 255);
        Point2d? intersection = IntersectInfiniteLines(line1, line2);

        DrawLineAndExtension(output, line1, line1Color, intersection);
        DrawLineAndExtension(output, line2, line2Color, intersection);

        double angle = inspection.details?.axisAngle ?? CalculateAcute3dAngle(line1.Direction3d, line2.Direction3d);
        if (intersection is { } center && IsReasonablePoint(center, output))
        {
            Point intersectionPoint = ToPoint(center);
            Cv2.Circle(output, intersectionPoint, 5, new Scalar(0, 0, 255, 255), -1, LineTypes.AntiAlias);
            DrawAngleArc(output, center, line1, line2, angle);
        }

        string status = inspection.resultCode.ToString();
        Scalar statusColor = inspection.resultCode switch
        {
            InspectionResultCode.OK => new Scalar(80, 220, 80, 255),
            InspectionResultCode.NG => new Scalar(60, 60, 255, 255),
            _ => new Scalar(0, 200, 255, 255),
        };
        Cv2.PutText(
            output,
            $"{status} 3D ANGLE: {angle.ToString("F3", CultureInfo.InvariantCulture)} deg",
            new Point(14, 30),
            HersheyFonts.HersheySimplex,
            0.65,
            statusColor,
            2,
            LineTypes.AntiAlias
        );
        DrawLegend(output, line1Color, line2Color);

        context.Set("output_mat", output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Mat RequireMat(IWorkflowContext context, string name, string displayName)
    {
        Mat value = context.Get<Mat>(name)
            ?? throw new InvalidOperationException($"{displayName}未连接。");
        if (value.Empty() || value.Total() < 6)
            throw new InvalidOperationException($"{displayName}无效，应包含6个数值。");
        return value;
    }

    private static Mat RequirePoints(IWorkflowContext context, string name, string displayName)
    {
        PointCloudData value = context.Get<PointCloudData>(name)
            ?? throw new InvalidOperationException($"{displayName}未连接。");
        Mat points = value.PointCloud
            ?? throw new InvalidOperationException($"{displayName}为空。");
        if (points.Empty() || points.Rows < 2 || points.Cols < 3)
            throw new InvalidOperationException($"{displayName}至少需要2个三维点。");
        return points;
    }

    private static RoiProjectionMapping ReadMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("投影映射未连接。");
        RoiProjectionMapping? mapping = JsonSerializer.Deserialize<RoiProjectionMapping>(json, JsonOptions);
        if (mapping is null
            || mapping.ImageWidth < 2
            || mapping.ImageHeight < 2
            || mapping.WorldMaxX <= mapping.WorldMinX
            || mapping.WorldMaxY <= mapping.WorldMinY)
            throw new InvalidOperationException("投影映射无效。");
        return mapping;
    }

    private static FittedLine ReadLine(
        Mat parameters,
        Mat points,
        RoiProjectionMapping mapping,
        string displayName
    )
    {
        double[] p = [ReadParameter(parameters, 0), ReadParameter(parameters, 1), ReadParameter(parameters, 2)];
        double[] d = [ReadParameter(parameters, 3), ReadParameter(parameters, 4), ReadParameter(parameters, 5)];
        double norm = Math.Sqrt(d.Sum(value => value * value));
        if (!double.IsFinite(norm) || norm < 1e-12)
            throw new InvalidOperationException($"{displayName}方向向量无效。");
        for (int i = 0; i < d.Length; i++)
            d[i] /= norm;

        double minT = double.PositiveInfinity;
        double maxT = double.NegativeInfinity;
        int pointCount = points.Rows;
        for (int row = 0; row < pointCount; row++)
        {
            double t =
                (points.Get<float>(row, 0) - p[0]) * d[0]
                + (points.Get<float>(row, 1) - p[1]) * d[1]
                + (points.Get<float>(row, 2) - p[2]) * d[2];
            minT = Math.Min(minT, t);
            maxT = Math.Max(maxT, t);
        }

        double[] start3d = [p[0] + minT * d[0], p[1] + minT * d[1], p[2] + minT * d[2]];
        double[] end3d = [p[0] + maxT * d[0], p[1] + maxT * d[1], p[2] + maxT * d[2]];
        Point2d start = Project(start3d, mapping);
        Point2d end = Project(end3d, mapping);
        if (Distance(start, end) < 1d)
            throw new InvalidOperationException($"{displayName}在XY投影中长度不足，无法绘制夹角。");
        return new FittedLine(start, end, d);
    }

    private static double ReadParameter(Mat parameters, int index) =>
        parameters.Rows >= 6
            ? parameters.Get<double>(index, 0)
            : parameters.Get<double>(0, index);

    private static Point2d Project(double[] point, RoiProjectionMapping mapping) =>
        new(
            (point[0] - mapping.WorldMinX) / (mapping.WorldMaxX - mapping.WorldMinX) * (mapping.ImageWidth - 1),
            (point[1] - mapping.WorldMinY) / (mapping.WorldMaxY - mapping.WorldMinY) * (mapping.ImageHeight - 1)
        );

    private static Point2d? IntersectInfiniteLines(FittedLine first, FittedLine second)
    {
        double x1 = first.Start.X, y1 = first.Start.Y, x2 = first.End.X, y2 = first.End.Y;
        double x3 = second.Start.X, y3 = second.Start.Y, x4 = second.End.X, y4 = second.End.Y;
        double denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denominator) < 1e-8)
            return null;
        return new Point2d(
            ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denominator,
            ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denominator
        );
    }

    private static void DrawLineAndExtension(Mat image, FittedLine line, Scalar color, Point2d? intersection)
    {
        Cv2.Line(image, ToPoint(line.Start), ToPoint(line.End), color, 3, LineTypes.AntiAlias);
        if (intersection is not { } p || !IsReasonablePoint(p, image))
            return;
        Point2d nearest = Distance(line.Start, p) <= Distance(line.End, p) ? line.Start : line.End;
        DrawDashedLine(image, nearest, p, color);
    }

    private static void DrawDashedLine(Mat image, Point2d start, Point2d end, Scalar color)
    {
        double length = Distance(start, end);
        if (length < 1d)
            return;
        const double dashLength = 8d;
        for (double offset = 0; offset < length; offset += dashLength * 2)
        {
            double t1 = offset / length;
            double t2 = Math.Min(offset + dashLength, length) / length;
            Cv2.Line(
                image,
                ToPoint(Lerp(start, end, t1)),
                ToPoint(Lerp(start, end, t2)),
                color,
                2,
                LineTypes.AntiAlias
            );
        }
    }

    private static void DrawAngleArc(Mat image, Point2d center, FittedLine first, FittedLine second, double angle)
    {
        Point2d v1 = Normalize(Subtract(Midpoint(first), center));
        Point2d v2 = Normalize(Subtract(Midpoint(second), center));
        if (v1.X * v2.X + v1.Y * v2.Y < 0)
            v2 = new Point2d(-v2.X, -v2.Y);

        double start = Math.Atan2(v1.Y, v1.X);
        double delta = NormalizeSignedRadians(Math.Atan2(v2.Y, v2.X) - start);
        if (Math.Abs(delta) > Math.PI / 2)
            delta += delta > 0 ? -Math.PI : Math.PI;

        const double radius = 34d;
        Point[] arc = Enumerable.Range(0, 25)
            .Select(i =>
            {
                double radians = start + delta * i / 24d;
                return ToPoint(new Point2d(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians)));
            })
            .ToArray();
        Cv2.Polylines(image, [arc], false, new Scalar(255, 255, 255, 255), 2, LineTypes.AntiAlias);

        double middle = start + delta / 2;
        Point label = ToPoint(new Point2d(center.X + 48 * Math.Cos(middle), center.Y + 48 * Math.Sin(middle)));
        Cv2.PutText(
            image,
            $"{angle.ToString("F2", CultureInfo.InvariantCulture)} deg",
            label,
            HersheyFonts.HersheySimplex,
            0.5,
            new Scalar(255, 255, 255, 255),
            2,
            LineTypes.AntiAlias
        );
    }

    private static void DrawLegend(Mat image, Scalar line1Color, Scalar line2Color)
    {
        int y = Math.Max(52, image.Rows - 18);
        Cv2.Line(image, new Point(14, y), new Point(42, y), line1Color, 3, LineTypes.AntiAlias);
        Cv2.PutText(image, "LINE 1", new Point(48, y + 5), HersheyFonts.HersheySimplex, 0.45, line1Color, 1, LineTypes.AntiAlias);
        Cv2.Line(image, new Point(124, y), new Point(152, y), line2Color, 3, LineTypes.AntiAlias);
        Cv2.PutText(image, "LINE 2", new Point(158, y + 5), HersheyFonts.HersheySimplex, 0.45, line2Color, 1, LineTypes.AntiAlias);
    }

    private static double CalculateAcute3dAngle(double[] first, double[] second)
    {
        double dot = Math.Abs(first[0] * second[0] + first[1] * second[1] + first[2] * second[2]);
        return Math.Acos(Math.Clamp(dot, 0d, 1d)) * 180d / Math.PI;
    }

    private static Mat EnsureBgra(Mat input)
    {
        if (input.Channels() == 4)
            return input.Clone();
        Mat output = new();
        Cv2.CvtColor(input, output, input.Channels() == 3 ? ColorConversionCodes.BGR2BGRA : ColorConversionCodes.GRAY2BGRA);
        return output;
    }

    private static bool IsReasonablePoint(Point2d point, Mat image) =>
        double.IsFinite(point.X)
        && double.IsFinite(point.Y)
        && point.X >= -image.Cols
        && point.X <= image.Cols * 2
        && point.Y >= -image.Rows
        && point.Y <= image.Rows * 2;

    private static Point ToPoint(Point2d point) =>
        new((int)Math.Round(point.X), (int)Math.Round(point.Y));
    private static Point2d Lerp(Point2d a, Point2d b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static Point2d Subtract(Point2d a, Point2d b) => new(a.X - b.X, a.Y - b.Y);
    private static Point2d Normalize(Point2d value)
    {
        double length = Math.Sqrt(value.X * value.X + value.Y * value.Y);
        return length < 1e-12 ? new Point2d(1, 0) : new Point2d(value.X / length, value.Y / length);
    }
    private static Point2d Midpoint(FittedLine line) =>
        new((line.Start.X + line.End.X) / 2, (line.Start.Y + line.End.Y) / 2);
    private static double Distance(Point2d a, Point2d b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static double NormalizeSignedRadians(double radians)
    {
        while (radians <= -Math.PI) radians += Math.PI * 2;
        while (radians > Math.PI) radians -= Math.PI * 2;
        return radians;
    }

    private sealed record FittedLine(Point2d Start, Point2d End, double[] Direction3d);
}
