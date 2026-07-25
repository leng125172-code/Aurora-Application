using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d3f12345-6789-0123-4567-890123456712")]
[Category("3D平面处理")]
[DisplayName("平面角度测量")]
[Description("测量平面内两条线段之间的夹角。")]
public class plane_angle_measure : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "plane_points", DisplayName = "平面点云" },
            new VisionParameter<string>
            {
                ParameterName = "lines_json",
                DisplayName = "线段参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
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
                ParameterName = "angle_degrees",
                DisplayName = "角度(度)",
                ParameterType = typeof(double),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<double>
            {
                ParameterName = "angle_radians",
                DisplayName = "角度(弧度)",
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

        string linesJson =
            context.Get<string>("lines_json")
            ?? throw new InvalidOperationException(
                "上下文变量 'lines_json' 为空，请确认输入绑定已正确设置。"
            );

        var lines = JsonSerializer.Deserialize<List<LineData>>(linesJson, JsonOptions)
            ?? throw new InvalidOperationException("线段参数JSON解析失败。");

        if (lines.Count < 2)
            throw new InvalidOperationException("至少需要两条线段才能测量夹角。");

        LineData line1 = lines[0];
        LineData line2 = lines[1];

        double[] vec1 = {
            line1.End.X - line1.Start.X,
            line1.End.Y - line1.Start.Y,
            line1.End.Z - line1.Start.Z
        };
        double[] vec2 = {
            line2.End.X - line2.Start.X,
            line2.End.Y - line2.Start.Y,
            line2.End.Z - line2.Start.Z
        };

        double dot = vec1[0] * vec2[0] + vec1[1] * vec2[1] + vec1[2] * vec2[2];
        double len1 = Math.Sqrt(vec1[0] * vec1[0] + vec1[1] * vec1[1] + vec1[2] * vec1[2]);
        double len2 = Math.Sqrt(vec2[0] * vec2[0] + vec2[1] * vec2[1] + vec2[2] * vec2[2]);

        if (len1 < 1e-10 || len2 < 1e-10)
            throw new InvalidOperationException("线段长度接近零，无法计算夹角。");

        double cosAngle = Math.Clamp(dot / (len1 * len2), -1.0, 1.0);
        double angleRad = Math.Acos(cosAngle);
        double angleDeg = angleRad * 180 / Math.PI;

        var measurement = new MeasurementInfo
        {
            Line1 = line1,
            Line2 = line2,
            AngleRadians = angleRad,
            AngleDegrees = angleDeg,
        };

        string json = JsonSerializer.Serialize(measurement, JsonOptions);

        context.Set("output_point_cloud", input);
        context.Set("measurement_json", json);
        context.Set("angle_degrees", angleDeg);
        context.Set("angle_radians", angleRad);
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
        public LineData Line1 { get; set; } = new();
        public LineData Line2 { get; set; } = new();
        public double AngleRadians { get; set; }
        public double AngleDegrees { get; set; }
    }

    public class LineData
    {
        public Point3D Start { get; set; } = new();
        public Point3D End { get; set; } = new();
    }

    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}