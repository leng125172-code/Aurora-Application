using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("d3f12345-6789-0123-4567-89012345670e")]
[Category("3D平面处理")]
[DisplayName("平面矩形检测")]
[Description("在平面点云中检测矩形，输出四角点3D坐标和尺寸。")]
public class plane_rectangle_detect : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "plane_points", DisplayName = "平面点云" },
            new MatImg() { ParameterName = "plane_params", DisplayName = "平面参数" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "标注点云" },
            new VisionParameter<string>
            {
                ParameterName = "rectangles_json",
                DisplayName = "矩形参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "rectangle_count",
                DisplayName = "矩形数量",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "minArea",
                DisplayName = "最小面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxArea",
                DisplayName = "最大面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "epsilonFactor",
                DisplayName = "近似精度系数",
                ParameterType = typeof(double),
                DefaultValue = "0.02",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minAspectRatio",
                DisplayName = "最小宽高比",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxAspectRatio",
                DisplayName = "最大宽高比",
                ParameterType = typeof(double),
                DefaultValue = "10",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "gridSize",
                DisplayName = "网格尺寸",
                ParameterType = typeof(double),
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _minArea;
    private readonly double _maxArea;
    private readonly double _epsilonFactor;
    private readonly double _minAspectRatio;
    private readonly double _maxAspectRatio;
    private readonly double _gridSize;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public plane_rectangle_detect(
        double minArea = 0,
        double maxArea = 0,
        double epsilonFactor = 0.02,
        double minAspectRatio = 0.1,
        double maxAspectRatio = 10,
        double gridSize = 0.01
    )
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _epsilonFactor = epsilonFactor;
        _minAspectRatio = minAspectRatio;
        _maxAspectRatio = maxAspectRatio;
        _gridSize = gridSize;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("plane_points")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_points' 为空，请确认输入绑定已正确设置。"
            );

        Mat planeParams =
            context.Get<Mat>("plane_params")
            ?? throw new InvalidOperationException(
                "上下文变量 'plane_params' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行平面矩形检测。");

        if (pointCloud.Rows < 4)
            throw new InvalidOperationException("点云点数少于 4，不足以检测矩形。");

        double a = planeParams.Get<double>(0, 0);
        double b = planeParams.Get<double>(1, 0);
        double c = planeParams.Get<double>(2, 0);
        double d = planeParams.Get<double>(3, 0);

        (Mat projected, double[,] transform) = ProjectToPlane(pointCloud, a, b, c, d);

        Mat binaryImg = CreateBinaryImage(projected);

        Cv2.FindContours(
            binaryImg,
            out Point[][] contours,
            out HierarchyIndex[] _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        List<Rectangle3DInfo> rectangles = new();

        foreach (Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (_minArea > 0 && area < _minArea / (_gridSize * _gridSize))
                continue;
            if (_maxArea > 0 && area > _maxArea / (_gridSize * _gridSize))
                continue;

            double perimeter = Cv2.ArcLength(contour, closed: true);
            if (perimeter < 10)
                continue;

            Point[] approx = Cv2.ApproxPolyDP(
                contour,
                _epsilonFactor * perimeter,
                closed: true
            );

            if (approx.Length != 4)
                continue;

            if (!Cv2.IsContourConvex(approx))
                continue;

            Rect boundingRect = Cv2.BoundingRect(approx);
            double aspectRatio = boundingRect.Width > 0
                ? (double)boundingRect.Height / boundingRect.Width
                : double.MaxValue;
            aspectRatio = Math.Max(aspectRatio, 1.0 / aspectRatio);

            if (aspectRatio < _minAspectRatio || aspectRatio > _maxAspectRatio)
                continue;

            List<Point3D> corners3D = new();
            foreach (var pt in approx)
            {
                double x2d = pt.X * _gridSize;
                double y2d = pt.Y * _gridSize;
                var pt3d = UnprojectPoint(x2d, y2d, transform);
                corners3D.Add(pt3d);
            }

            double width3d = Distance3D(corners3D[0], corners3D[1]);
            double height3d = Distance3D(corners3D[1], corners3D[2]);
            double area3d = width3d * height3d;

            (double cx, double cy, double cz) = Centroid3D(corners3D);

            rectangles.Add(
                new Rectangle3DInfo
                {
                    Centroid = new Point3D { X = cx, Y = cy, Z = cz },
                    Width = width3d,
                    Height = height3d,
                    Area = area3d,
                    Perimeter = perimeter * _gridSize,
                    AspectRatio = aspectRatio,
                    Corners = corners3D,
                }
            );
        }

        string json = JsonSerializer.Serialize(
            new { RectangleCount = rectangles.Count, Rectangles = rectangles },
            JsonOptions
        );

        context.Set("output_point_cloud", input);
        context.Set("rectangles_json", json);
        context.Set("rectangle_count", rectangles.Count);
    }

    private (Mat projected, double[,] transform) ProjectToPlane(Mat pointCloud, double a, double b, double c, double d)
    {
        double[] normal = { a, b, c };
        double[] u, v;
        ComputeBasis(normal, out u, out v);

        double cx = 0, cy = 0, cz = 0;
        int n = pointCloud.Rows;
        for (int i = 0; i < n; i++)
        {
            cx += pointCloud.Get<float>(i, 0);
            cy += pointCloud.Get<float>(i, 1);
            cz += pointCloud.Get<float>(i, 2);
        }
        cx /= n;
        cy /= n;
        cz /= n;

        Mat projected = new Mat(n, 2, MatType.CV_64FC1);
        for (int i = 0; i < n; i++)
        {
            double x = pointCloud.Get<float>(i, 0) - cx;
            double y = pointCloud.Get<float>(i, 1) - cy;
            double z = pointCloud.Get<float>(i, 2) - cz;

            double px = x * u[0] + y * u[1] + z * u[2];
            double py = x * v[0] + y * v[1] + z * v[2];

            projected.Set(i, 0, px);
            projected.Set(i, 1, py);
        }

        double[,] transform = {
            { u[0], u[1], u[2], cx },
            { v[0], v[1], v[2], cy },
            { a, b, c, cz }
        };

        return (projected, transform);
    }

    private Point3D UnprojectPoint(double x2d, double y2d, double[,] transform)
    {
        double x = x2d * transform[0, 0] + y2d * transform[1, 0] + transform[0, 3];
        double y = x2d * transform[0, 1] + y2d * transform[1, 1] + transform[1, 3];
        double z = x2d * transform[0, 2] + y2d * transform[1, 2] + transform[2, 3];
        return new Point3D { X = x, Y = y, Z = z };
    }

    private void ComputeBasis(double[] normal, out double[] u, out double[] v)
    {
        double[] tmp = new double[3];
        if (Math.Abs(normal[0]) < Math.Abs(normal[1]))
        {
            if (Math.Abs(normal[0]) < Math.Abs(normal[2]))
                tmp = new[] { 1.0, 0.0, 0.0 };
            else
                tmp = new[] { 0.0, 0.0, 1.0 };
        }
        else
        {
            if (Math.Abs(normal[1]) < Math.Abs(normal[2]))
                tmp = new[] { 0.0, 1.0, 0.0 };
            else
                tmp = new[] { 0.0, 0.0, 1.0 };
        }

        u = CrossProduct(tmp, normal);
        double uNorm = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        u = u.Select(x => x / uNorm).ToArray();

        v = CrossProduct(normal, u);
        double vNorm = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        v = v.Select(x => x / vNorm).ToArray();
    }

    private double[] CrossProduct(double[] a, double[] b)
    {
        return new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]
        };
    }

    private Mat CreateBinaryImage(Mat projected)
    {
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        for (int i = 0; i < projected.Rows; i++)
        {
            double x = projected.Get<double>(i, 0);
            double y = projected.Get<double>(i, 1);
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        int width = (int)Math.Ceiling((maxX - minX) / _gridSize) + 2;
        int height = (int)Math.Ceiling((maxY - minY) / _gridSize) + 2;

        Mat binaryImg = Mat.Zeros(height, width, MatType.CV_8UC1);

        for (int i = 0; i < projected.Rows; i++)
        {
            double x = projected.Get<double>(i, 0);
            double y = projected.Get<double>(i, 1);
            int col = (int)((x - minX) / _gridSize) + 1;
            int row = (int)((y - minY) / _gridSize) + 1;

            if (row >= 0 && row < height && col >= 0 && col < width)
                binaryImg.Set<byte>(row, col, 255);
        }

        Cv2.MorphologyEx(binaryImg, binaryImg, MorphTypes.Close, Mat.Ones(3, 3, MatType.CV_8UC1));
        Cv2.MorphologyEx(binaryImg, binaryImg, MorphTypes.Open, Mat.Ones(3, 3, MatType.CV_8UC1));

        return binaryImg;
    }

    private double Distance3D(Point3D p1, Point3D p2)
    {
        double dx = p1.X - p2.X;
        double dy = p1.Y - p2.Y;
        double dz = p1.Z - p2.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private (double x, double y, double z) Centroid3D(List<Point3D> points)
    {
        double cx = 0, cy = 0, cz = 0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
            cz += p.Z;
        }
        int n = points.Count;
        return (cx / n, cy / n, cz / n);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class Rectangle3DInfo
    {
        public Point3D Centroid { get; set; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
        public double Area { get; set; }
        public double Perimeter { get; set; }
        public double AspectRatio { get; set; }
        public List<Point3D> Corners { get; set; } = new();
    }

    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}