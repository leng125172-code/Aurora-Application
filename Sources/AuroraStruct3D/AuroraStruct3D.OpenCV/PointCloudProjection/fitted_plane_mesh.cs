using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 根据拟合平面参数和内点边界生成严格共面的实体三角网格。
/// 该网格用于三维展示；测量仍应使用原始点云及平面参数。
/// </summary>
[Guid("c8427d91-3f26-4f68-9a35-67c2d019b501")]
[Category("3D重建分割")]
[DisplayName("拟合平面网格生成")]
[Description("把拟合平面按内点凸包生成连续实体三角网格，用于三维模型式展示。")]
public sealed class fitted_plane_mesh : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "plane_params", DisplayName = "平面参数" },
            new PointCloudData { ParameterName = "inlier_points", DisplayName = "平面内点" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "mesh_vertices", DisplayName = "网格顶点" },
            new MatImg { ParameterName = "mesh_indices", DisplayName = "三角形索引" },
            new MatImg { ParameterName = "mesh_normals", DisplayName = "顶点法向量" },
            new VisionParameter<string>
            {
                ParameterName = "boundary_json",
                DisplayName = "平面边界",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<string>
            {
                ParameterName = "mesh_info",
                DisplayName = "网格信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "boundaryPadding",
                DisplayName = "边界外扩",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly double _boundaryPadding;
    private bool _disposed;

    public fitted_plane_mesh(double boundaryPadding = 0)
    {
        if (!double.IsFinite(boundaryPadding) || boundaryPadding < 0)
            throw new ArgumentOutOfRangeException(
                nameof(boundaryPadding),
                "边界外扩必须是大于或等于 0 的有限数值。"
            );
        _boundaryPadding = boundaryPadding;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat plane =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException("平面参数为空，无法生成网格。");
        PointCloudData inliers =
            context.Get<PointCloudData>("inlier_points")
            ?? throw new InvalidOperationException("平面内点为空，无法生成网格。");
        Mat points =
            inliers.PointCloud
            ?? throw new InvalidOperationException("平面内点为空，无法生成网格。");

        if (plane.Empty() || plane.Total() < 4)
            throw new InvalidOperationException("平面参数必须包含 [a,b,c,d]。");
        if (points.Empty() || points.Rows < 3 || points.Cols < 3)
            throw new InvalidOperationException("至少需要 3 个三维内点才能生成平面网格。");

        double a = ReadScalar(plane, 0);
        double b = ReadScalar(plane, 1);
        double c = ReadScalar(plane, 2);
        double d = ReadScalar(plane, 3);
        double normalLength = Math.Sqrt(a * a + b * b + c * c);
        if (!double.IsFinite(normalLength) || normalLength < 1e-12)
            throw new InvalidOperationException("平面法向量无效，无法生成网格。");
        a /= normalLength;
        b /= normalLength;
        c /= normalLength;
        d /= normalLength;

        (double ux, double uy, double uz) =
            Math.Abs(c) < 0.9 ? Normalize(-b, a, 0) : Normalize(0, -c, b);
        double vx = b * uz - c * uy;
        double vy = c * ux - a * uz;
        double vz = a * uy - b * ux;

        // 取内点质心在拟合平面上的正交投影作为稳定的局部坐标原点。
        double cx = 0;
        double cy = 0;
        double cz = 0;
        for (int i = 0; i < points.Rows; i++)
        {
            float x = points.Get<float>(i, 0);
            float y = points.Get<float>(i, 1);
            float z = points.Get<float>(i, 2);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidOperationException("平面内点包含非有限坐标。");
            cx += x;
            cy += y;
            cz += z;
        }
        cx /= points.Rows;
        cy /= points.Rows;
        cz /= points.Rows;
        double centroidDistance = a * cx + b * cy + c * cz + d;
        double ox = cx - centroidDistance * a;
        double oy = cy - centroidDistance * b;
        double oz = cz - centroidDistance * c;

        List<Point2> projected = new(points.Rows);
        for (int i = 0; i < points.Rows; i++)
        {
            double dx = points.Get<float>(i, 0) - ox;
            double dy = points.Get<float>(i, 1) - oy;
            double dz = points.Get<float>(i, 2) - oz;
            projected.Add(new Point2(dx * ux + dy * uy + dz * uz, dx * vx + dy * vy + dz * vz));
        }

        List<Point2> hull = ConvexHull(projected);
        if (hull.Count < 3)
            throw new InvalidOperationException("平面内点边界退化为直线，无法生成实体网格。");

        if (_boundaryPadding > 0)
        {
            double hu = hull.Average(p => p.X);
            double hv = hull.Average(p => p.Y);
            for (int i = 0; i < hull.Count; i++)
            {
                double dx = hull[i].X - hu;
                double dy = hull[i].Y - hv;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-12)
                    hull[i] = new Point2(
                        hull[i].X + _boundaryPadding * dx / len,
                        hull[i].Y + _boundaryPadding * dy / len
                    );
            }
        }

        Mat vertices = new(hull.Count, 3, MatType.CV_32FC1);
        Mat normals = new(hull.Count, 3, MatType.CV_32FC1);
        var boundary = new List<object>(hull.Count);
        for (int i = 0; i < hull.Count; i++)
        {
            float x = (float)(ox + hull[i].X * ux + hull[i].Y * vx);
            float y = (float)(oy + hull[i].X * uy + hull[i].Y * vy);
            float z = (float)(oz + hull[i].X * uz + hull[i].Y * vz);
            vertices.Set(i, 0, x);
            vertices.Set(i, 1, y);
            vertices.Set(i, 2, z);
            normals.Set(i, 0, (float)a);
            normals.Set(i, 1, (float)b);
            normals.Set(i, 2, (float)c);
            boundary.Add(new { x, y, z });
        }

        Mat indices = new(hull.Count - 2, 3, MatType.CV_32SC1);
        for (int i = 0; i < hull.Count - 2; i++)
        {
            indices.Set(i, 0, 0);
            indices.Set(i, 1, i + 1);
            indices.Set(i, 2, i + 2);
        }

        double area = 0;
        for (int i = 0; i < hull.Count; i++)
        {
            Point2 p = hull[i];
            Point2 q = hull[(i + 1) % hull.Count];
            area += p.X * q.Y - p.Y * q.X;
        }
        area = Math.Abs(area) * 0.5;

        context.Set("mesh_vertices", vertices);
        context.Set("mesh_indices", indices);
        context.Set("mesh_normals", normals);
        context.Set("boundary_json", JsonSerializer.Serialize(boundary, JsonOptions));
        context.Set(
            "mesh_info",
            JsonSerializer.Serialize(
                new
                {
                    vertexCount = hull.Count,
                    triangleCount = hull.Count - 2,
                    area,
                    boundaryPadding = _boundaryPadding,
                    plane = new[] { a, b, c, d },
                },
                JsonOptions
            )
        );
    }

    private static double ReadScalar(Mat mat, int index) =>
        mat.Depth() switch
        {
            MatType.CV_32F => mat.Get<float>(index),
            MatType.CV_64F => mat.Get<double>(index),
            _ => throw new InvalidOperationException("平面参数必须为 CV_32F 或 CV_64F。"),
        };

    private static (double x, double y, double z) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        return (x / length, y / length, z / length);
    }

    private static List<Point2> ConvexHull(List<Point2> points)
    {
        Point2[] sorted = points
            .Distinct()
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToArray();
        if (sorted.Length <= 2)
            return sorted.ToList();

        List<Point2> lower = new();
        foreach (Point2 p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        List<Point2> upper = new();
        for (int i = sorted.Length - 1; i >= 0; i--)
        {
            Point2 p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(Point2 o, Point2 a, Point2 b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private readonly record struct Point2(double X, double Y);
}
