using System.Text.Json;

namespace AuroraStruct3D.OpenCV.MeasureOps;

/// <summary>
/// 根据标准 OK 图中标注的两条特征及搜索区，在实际产品图中重新查找直线并检测安装角度。
/// 标注 JSON 可携带 3x3 单应矩阵，将标准图坐标映射到当前产品图坐标。
/// </summary>
[Guid("64f76f2e-8572-4cb5-9a4f-3a22b1417401")]
[Category("2D尺寸测量")]
[DisplayName("安装角度特征检测")]
[Description("按标准OK图标注的两条特征搜索区，在实际产品图中重新拟合直线并计算安装角度。")]
public sealed class installation_angle_feature_inspection : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "actual_image", DisplayName = "实际产品图" },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            new MatImg { ParameterName = "result_image", DisplayName = "角度结果图" },
            InspectionResults.Output<ResultPayload>("安装角特征检测结果"),
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "featureTemplateJson",
                DisplayName = "标准图特征标注",
                ParameterType = typeof(string),
                DefaultValue = """{"features":[]}""",
                Required = true,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "angleType",
                DisplayName = "角度类型",
                ParameterType = typeof(string),
                DefaultValue = "acute",
                ValueLimit = new[] { "acute", "obtuse", "directed" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "nominalAngle",
                DisplayName = "标称角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minDeviation",
                DisplayName = "最小允许偏差(度)",
                ParameterType = typeof(double),
                DefaultValue = "-0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxDeviation",
                DisplayName = "最大允许偏差(度)",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minimumLineLength",
                DisplayName = "最小线段长度(像素)",
                ParameterType = typeof(double),
                DefaultValue = "20",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maximumDirectionError",
                DisplayName = "最大方向偏差(度)",
                ParameterType = typeof(double),
                DefaultValue = "20",
                Required = false,
                ControlType = PortControlType.Input,
            },
        ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly FeatureTemplate _template;
    private readonly string _angleType;
    private readonly double _nominalAngle;
    private readonly double _minDeviation;
    private readonly double _maxDeviation;
    private readonly double _minimumLineLength;
    private readonly double _maximumDirectionError;
    private bool _disposed;

    public installation_angle_feature_inspection(
        string featureTemplateJson,
        string angleType = "acute",
        double nominalAngle = 0,
        double minDeviation = -0.5,
        double maxDeviation = 0.5,
        double minimumLineLength = 20,
        double maximumDirectionError = 20
    )
    {
        _template = JsonSerializer.Deserialize<FeatureTemplate>(featureTemplateJson, JsonOptions)
            ?? throw new ArgumentException("标准图特征标注 JSON 无效。", nameof(featureTemplateJson));
        if (_template.Features.Count != 2)
            throw new ArgumentException("标准图必须标注两条角度特征。", nameof(featureTemplateJson));
        if (_template.Features.Any(x => x.SearchRegion.Count < 3))
            throw new ArgumentException("每条特征必须包含至少 3 个点的搜索区。", nameof(featureTemplateJson));
        _angleType = angleType.Trim().ToLowerInvariant();
        if (_angleType is not ("acute" or "obtuse" or "directed"))
            throw new ArgumentException("角度类型必须为 acute、obtuse 或 directed。", nameof(angleType));
        if (!double.IsFinite(nominalAngle))
            throw new ArgumentOutOfRangeException(nameof(nominalAngle));
        if (!double.IsFinite(minDeviation) || !double.IsFinite(maxDeviation) || minDeviation > maxDeviation)
            throw new ArgumentException("允许偏差范围无效。");
        if (!double.IsFinite(minimumLineLength) || minimumLineLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLineLength));
        if (!double.IsFinite(maximumDirectionError) || maximumDirectionError is <= 0 or > 90)
            throw new ArgumentOutOfRangeException(nameof(maximumDirectionError));

        _nominalAngle = nominalAngle;
        _minDeviation = minDeviation;
        _maxDeviation = maxDeviation;
        _minimumLineLength = minimumLineLength;
        _maximumDirectionError = maximumDirectionError;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat input = context.Get<Mat>("actual_image")
            ?? throw new InvalidOperationException("实际产品图未连接。");
        if (input.Empty())
            throw new InvalidOperationException("实际产品图为空。");

        using Mat gray = ToGray(input);
        Mat result = ToBgr(input);
        DetectedLine? first = DetectFeature(gray, _template.Features[0]);
        DetectedLine? second = DetectFeature(gray, _template.Features[1]);

        if (first is null || second is null)
        {
            WriteUnknown(context, result, first, second, "未在搜索区内找到可信的对应直线特征。");
            return;
        }

        double measured = CalculateAngle(first, second, _angleType);
        double deviation = NormalizeDeviation(measured - _nominalAngle, _angleType);
        bool isOk = deviation >= _minDeviation && deviation <= _maxDeviation;
        string status = isOk ? "OK" : "NG";
        Point2d? intersection = IntersectInfiniteLines(first, second);

        DrawLineAndExtension(result, first, new Scalar(255, 120, 0), intersection);
        DrawLineAndExtension(result, second, new Scalar(0, 165, 255), intersection);
        if (intersection is { } p && IsReasonablePoint(p, result))
            Cv2.Circle(result, new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)), 5, new Scalar(0, 0, 255), -1);

        Cv2.PutText(
            result,
            $"{status} {measured:F2} deg (dev {deviation:+0.00;-0.00;0.00})",
            new Point(12, 28),
            HersheyFonts.HersheySimplex,
            0.65,
            isOk ? new Scalar(0, 200, 0) : new Scalar(0, 0, 255),
            2
        );

        var payload = new ResultPayload
        {
            Status = status,
            IsValid = true,
            IsOk = isOk,
            NominalAngle = _nominalAngle,
            MeasuredAngle = measured,
            AngleDeviation = deviation,
            AngleType = _angleType,
            Line1 = first,
            Line2 = second,
            Intersection = intersection is { } detectedIntersection
                ? new PointDefinition
                {
                    X = detectedIntersection.X,
                    Y = detectedIntersection.Y,
                }
                : null,
        };
        SetOutputs(context, result, measured, deviation, true, isOk, status, payload);
    }

    private DetectedLine? DetectFeature(Mat gray, FeatureDefinition feature)
    {
        List<Point2d> polygon = feature.SearchRegion.Select(Transform).ToList();
        Rect bounds = BoundingRect(polygon, gray.Cols, gray.Rows);
        if (bounds.Width < 2 || bounds.Height < 2)
            return null;

        using Mat mask = Mat.Zeros(gray.Size(), MatType.CV_8UC1);
        Point[] maskPoints = polygon
            .Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))
            .ToArray();
        Cv2.FillPoly(mask, [maskPoints], Scalar.White);
        using Mat edges = new();
        Cv2.Canny(gray, edges, 50, 150);
        Cv2.BitwiseAnd(edges, mask, edges);
        using Mat roi = new(edges, bounds);

        LineSegmentPoint[] candidates = Cv2.HoughLinesP(
            roi,
            1,
            Math.PI / 180,
            15,
            Math.Max(5, _minimumLineLength),
            10
        );
        Point2d standardStart = Transform(feature.Start);
        Point2d standardEnd = Transform(feature.End);
        double standardAngle = DirectionAngle(standardStart, standardEnd);

        return candidates
            .Select(line =>
            {
                var start = new Point2d(line.P1.X + bounds.X, line.P1.Y + bounds.Y);
                var end = new Point2d(line.P2.X + bounds.X, line.P2.Y + bounds.Y);
                double length = Distance(start, end);
                double directionError = UndirectedAngleDifference(
                    DirectionAngle(start, end),
                    standardAngle
                );
                return new DetectedLine
                {
                    Start = start,
                    End = end,
                    Length = length,
                    DirectionError = directionError,
                    Confidence = Math.Clamp(
                        (1 - directionError / _maximumDirectionError)
                            * Math.Min(1, length / Math.Max(_minimumLineLength, 1)),
                        0,
                        1
                    ),
                };
            })
            .Where(x => x.Length >= _minimumLineLength && x.DirectionError <= _maximumDirectionError)
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.Length)
            .FirstOrDefault();
    }

    private Point2d Transform(PointDefinition point)
    {
        if (_template.Transform is not { Length: 9 } h)
            return new Point2d(point.X, point.Y);
        double w = h[6] * point.X + h[7] * point.Y + h[8];
        if (Math.Abs(w) < 1e-12)
            throw new InvalidOperationException("标准图到实际图的映射矩阵无效。");
        return new Point2d(
            (h[0] * point.X + h[1] * point.Y + h[2]) / w,
            (h[3] * point.X + h[4] * point.Y + h[5]) / w
        );
    }

    private void WriteUnknown(
        IWorkflowContext context,
        Mat result,
        DetectedLine? first,
        DetectedLine? second,
        string reason
    )
    {
        if (first is not null)
            DrawLineAndExtension(result, first, new Scalar(255, 120, 0), null);
        if (second is not null)
            DrawLineAndExtension(result, second, new Scalar(0, 165, 255), null);
        Cv2.PutText(result, $"UNKNOWN: {reason}", new Point(12, 28), HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 200, 255), 2);
        SetOutputs(
            context,
            result,
            double.NaN,
            double.NaN,
            false,
            false,
            "UNKNOWN",
            new ResultPayload
            {
                Status = "UNKNOWN",
                IsValid = false,
                IsOk = false,
                NominalAngle = _nominalAngle,
                AngleType = _angleType,
                Line1 = first,
                Line2 = second,
                Reason = reason,
            }
        );
    }

    private static void SetOutputs(
        IWorkflowContext context,
        Mat result,
        double measured,
        double deviation,
        bool isValid,
        bool isOk,
        string status,
        ResultPayload payload
    )
    {
        context.Set("result_image", result);
        context.Set("result", InspectionResults.CreateTyped(isValid, isOk, payload, status));
    }

    private static double CalculateAngle(DetectedLine first, DetectedLine second, string angleType)
    {
        double a1 = DirectionAngle(first.Start, first.End);
        double a2 = DirectionAngle(second.Start, second.End);
        if (angleType == "directed")
        {
            double directed = (a2 - a1) * 180 / Math.PI;
            return NormalizeSignedAngle(directed);
        }

        double acute = UndirectedAngleDifference(a1, a2);
        return angleType == "obtuse" ? 180 - acute : acute;
    }

    private static double NormalizeDeviation(double value, string angleType) =>
        angleType == "directed" ? NormalizeSignedAngle(value) : value;

    private static double NormalizeSignedAngle(double degrees)
    {
        while (degrees <= -180)
            degrees += 360;
        while (degrees > 180)
            degrees -= 360;
        return degrees;
    }

    private static double UndirectedAngleDifference(double a, double b)
    {
        double degrees = Math.Abs(a - b) * 180 / Math.PI % 180;
        return Math.Min(degrees, 180 - degrees);
    }

    private static double DirectionAngle(Point2d start, Point2d end) =>
        Math.Atan2(end.Y - start.Y, end.X - start.X);

    private static Point2d? IntersectInfiniteLines(DetectedLine first, DetectedLine second)
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

    private static void DrawLineAndExtension(
        Mat image,
        DetectedLine line,
        Scalar color,
        Point2d? intersection
    )
    {
        Cv2.Line(image, ToPoint(line.Start), ToPoint(line.End), color, 2, LineTypes.AntiAlias);
        if (intersection is not { } p || !IsReasonablePoint(p, image))
            return;
        Point nearest = Distance(line.Start, p) <= Distance(line.End, p)
            ? ToPoint(line.Start)
            : ToPoint(line.End);
        DrawDashedLine(image, nearest, ToPoint(p), color);
    }

    private static void DrawDashedLine(Mat image, Point start, Point end, Scalar color)
    {
        double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        if (length < 1)
            return;
        const double dash = 8;
        for (double offset = 0; offset < length; offset += dash * 2)
        {
            double t1 = offset / length;
            double t2 = Math.Min(offset + dash, length) / length;
            Cv2.Line(
                image,
                new Point((int)Math.Round(start.X + (end.X - start.X) * t1), (int)Math.Round(start.Y + (end.Y - start.Y) * t1)),
                new Point((int)Math.Round(start.X + (end.X - start.X) * t2), (int)Math.Round(start.Y + (end.Y - start.Y) * t2)),
                color,
                1,
                LineTypes.AntiAlias
            );
        }
    }

    private static bool IsReasonablePoint(Point2d point, Mat image) =>
        double.IsFinite(point.X)
        && double.IsFinite(point.Y)
        && point.X >= -image.Cols
        && point.X <= image.Cols * 2
        && point.Y >= -image.Rows
        && point.Y <= image.Rows * 2;

    private static Rect BoundingRect(List<Point2d> points, int width, int height)
    {
        int left = Math.Clamp((int)Math.Floor(points.Min(p => p.X)), 0, width);
        int top = Math.Clamp((int)Math.Floor(points.Min(p => p.Y)), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(points.Max(p => p.X)), 0, width);
        int bottom = Math.Clamp((int)Math.Ceiling(points.Max(p => p.Y)), 0, height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static Mat ToGray(Mat input)
    {
        Mat gray = new();
        if (input.Channels() == 1)
            input.CopyTo(gray);
        else if (input.Channels() == 4)
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGRA2GRAY);
        else
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat ToBgr(Mat input)
    {
        Mat output = new();
        if (input.Channels() == 1)
            Cv2.CvtColor(input, output, ColorConversionCodes.GRAY2BGR);
        else if (input.Channels() == 4)
            Cv2.CvtColor(input, output, ColorConversionCodes.BGRA2BGR);
        else
            input.CopyTo(output);
        return output;
    }

    private static Point ToPoint(Point2d point) =>
        new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    private static double Distance(Point2d a, Point2d b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public sealed class FeatureTemplate
    {
        public List<FeatureDefinition> Features { get; set; } = [];
        public double[]? Transform { get; set; }
    }

    public sealed class FeatureDefinition
    {
        public string? Name { get; set; }
        public PointDefinition Start { get; set; } = new();
        public PointDefinition End { get; set; } = new();
        public List<PointDefinition> SearchRegion { get; set; } = [];
    }

    public sealed class PointDefinition
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class DetectedLine
    {
        public Point2d Start { get; set; }
        public Point2d End { get; set; }
        public double Length { get; set; }
        public double DirectionError { get; set; }
        public double Confidence { get; set; }
    }

    public sealed class ResultPayload
    {
        public string Status { get; set; } = "UNKNOWN";
        public bool IsValid { get; set; }
        public bool IsOk { get; set; }
        public double NominalAngle { get; set; }
        public double? MeasuredAngle { get; set; }
        public double? AngleDeviation { get; set; }
        public string AngleType { get; set; } = string.Empty;
        public DetectedLine? Line1 { get; set; }
        public DetectedLine? Line2 { get; set; }
        public PointDefinition? Intersection { get; set; }
        public string? Reason { get; set; }
    }
}
