namespace AuroraStruct3D.OpenCV.File.PointCloud;

/// <summary>
/// 工作流算子：从文件系统读取点云文件，输出 <see cref="PointCloudData"/> 点云对象。
/// <para>
/// 支持的点云格式：
/// <list type="bullet">
///   <item><description>PLY - Polygon File Format</description></item>
///   <item><description>OBJ - Wavefront 网格顶点（读取 v 记录为参考点云）</description></item>
///   <item><description>PCD - Point Cloud Data</description></item>
///   <item><description>XYZ - 简单文本格式</description></item>
///   <item><description>TXT - 文本格式</description></item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>point_cloud_path</c>（string）— 点云文件路径</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 读取结果，内部包含点坐标矩阵与可选颜色矩阵</item>
/// </list>
/// </para>
/// <para>
/// 输出格式：
/// <list type="bullet">
///   <item><description>坐标矩阵：列 0-2：X, Y, Z 坐标（必须）</description></item>
///   <item><description>坐标矩阵：列 3-5：Nx, Ny, Nz 法向量（可选）</description></item>
///   <item><description>颜色矩阵：Mat(N, 3, CV_8UC3)，BGR 顺序（可选）</description></item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
[Category("数据读取")]
[DisplayName("读取点云")]
[Description("从文件把点云读进来，支持 PLY/PCD/XYZ 等格式，3D 流程的起点。")]
public class read_point_cloud : IOperator
{
    /// <summary>
    /// 输入端口定义（工作流引擎反射用）。
    /// </summary>
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudFilePath(errorCheck: true)
            {
                ParameterName = "point_cloud_path",
                DisplayName = "点云路径",
            },
        };

    /// <summary>
    /// 输出端口定义（工作流引擎反射用）。
    /// </summary>
    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    /// <summary>
    /// 构造函数配置参数定义（工作流引擎反射用）。
    /// </summary>
    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    /// <summary>
    /// 初始化读取点云算子。
    /// </summary>
    public read_point_cloud() { }

    /// <summary>
    /// 执行读取点云操作。
    /// 从 <paramref name="context"/> 读取 <c>point_cloud_path</c> 变量，
    /// 读取完成后将点云数据写入 <c>output_point_cloud</c> 变量。
    /// </summary>
    /// <param name="context">工作流运行时上下文。</param>
    /// <exception cref="ObjectDisposedException">算子已被释放时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// point_cloud_path 变量为空，或点云读取失败时抛出。
    /// </exception>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string filePath =
            context.Get<string>("point_cloud_path")
            ?? throw new InvalidOperationException(
                "上下文变量 'point_cloud_path' 为空，请确认 OperatorCallStatement 的输入绑定已正确设置。"
            );

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        (Mat pointCloud, Mat? colors) = extension switch
        {
            ".ply" => ReadPlyFile(filePath),
            ".obj" => ReadObjFile(filePath),
            ".pcd" => ReadPcdFile(filePath),
            ".xyz" or ".txt" or ".asc" => ReadXyzFile(filePath),
            ".pts" => ReadPtsFile(filePath),
            _ => throw new InvalidOperationException(
                $"不支持的点云文件格式：{extension}。支持的格式：.ply, .obj, .pcd, .xyz, .txt, .asc, .pts"
            ),
        };

        if (pointCloud.Empty())
        {
            pointCloud.Dispose();
            colors?.Dispose();
            throw new InvalidOperationException(
                $"点云文件读取失败：{filePath}。文件可能已损坏或格式不正确。"
            );
        }

        var result = new PointCloudData();
        result.Value = pointCloud;
        result.SetColors(colors);

        context.Set("output_point_cloud", result);
    }

    private static (Mat, Mat?) ReadObjFile(string filePath)
    {
        List<float[]> points = [];
        foreach (string rawLine in System.IO.File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("v ", StringComparison.Ordinal))
                continue;
            string[] parts = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (
                parts.Length >= 4
                && float.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float x
                )
                && float.TryParse(
                    parts[2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float y
                )
                && float.TryParse(
                    parts[3],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float z
                )
            )
                points.Add([x, y, z]);
        }

        Mat result = new(points.Count, 3, MatType.CV_32FC1);
        for (int i = 0; i < points.Count; i++)
        {
            result.Set(i, 0, points[i][0]);
            result.Set(i, 1, points[i][1]);
            result.Set(i, 2, points[i][2]);
        }
        return (result, null);
    }

    /// <summary>
    /// 读取 PLY 格式点云文件。
    /// </summary>
    private (Mat, Mat?) ReadPlyFile(string filePath)
    {
        var points = new List<float[]>();
        var colors = new List<byte[]>();
        bool hasColor = false;
        bool hasNormal = false;
        int vertexCount = 0;

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            bool inHeader = true;
            bool inVertexSection = false;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (inHeader)
                {
                    if (line.StartsWith("element vertex", StringComparison.OrdinalIgnoreCase))
                    {
                        vertexCount = int.Parse(line.Split(' ')[^1]);
                    }
                    else if (line.StartsWith("property", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ');
                        string propertyName = parts[^1].ToLower();

                        if (
                            propertyName == "red"
                            || propertyName == "green"
                            || propertyName == "blue"
                        )
                        {
                            hasColor = true;
                        }
                        else if (
                            propertyName == "nx"
                            || propertyName == "ny"
                            || propertyName == "nz"
                        )
                        {
                            hasNormal = true;
                        }
                    }
                    else if (line == "end_header")
                    {
                        inHeader = false;
                        inVertexSection = true;
                        continue;
                    }
                }
                else if (inVertexSection)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var values = line.Split(
                        new[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    if (values.Length >= 3)
                    {
                        int columnCount = 3;
                        if (hasNormal)
                            columnCount += 3;

                        var point = new float[columnCount];
                        point[0] = float.Parse(values[0]);
                        point[1] = float.Parse(values[1]);
                        point[2] = float.Parse(values[2]);

                        int normalValueOffset = 3;
                        if (hasColor && values.Length >= 6)
                        {
                            colors.Add(
                                new[]
                                {
                                    (byte)float.Parse(values[5]), // B
                                    (byte)float.Parse(values[4]), // G
                                    (byte)float.Parse(values[3]), // R
                                }
                            );
                            normalValueOffset = 6;
                        }

                        if (hasNormal && values.Length >= normalValueOffset + 3)
                        {
                            // 坐标 Mat 的法向量始终位于列 3..5；normalValueOffset 仅表示
                            // PLY 文本中的法向量位置（颜色存在时为 6..8）。
                            point[3] = float.Parse(values[normalValueOffset]);
                            point[4] = float.Parse(values[normalValueOffset + 1]);
                            point[5] = float.Parse(values[normalValueOffset + 2]);
                        }

                        points.Add(point);
                    }

                    if (points.Count >= vertexCount && vertexCount > 0)
                        break;
                }
            }
        }

        return (ConvertToMat(points), hasColor ? ConvertColorsToMat(colors) : null);
    }

    /// <summary>
    /// 读取 PCD 格式点云文件。
    /// </summary>
    private (Mat, Mat?) ReadPcdFile(string filePath)
    {
        var points = new List<float[]>();
        int pointCount = 0;

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            bool inData = false;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.StartsWith("POINTS", StringComparison.OrdinalIgnoreCase))
                {
                    pointCount = int.Parse(line.Split(' ')[^1]);
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    inData = true;
                    continue;
                }

                if (inData && !string.IsNullOrWhiteSpace(line))
                {
                    var values = line.Split(
                        new[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    if (values.Length >= 3)
                    {
                        var point = new float[values.Length];
                        for (int i = 0; i < values.Length; i++)
                        {
                            point[i] = float.Parse(values[i]);
                        }
                        points.Add(point);
                    }

                    if (pointCount > 0 && points.Count >= pointCount)
                        break;
                }
            }
        }

        return (ConvertToMat(points), null);
    }

    /// <summary>
    /// 读取 XYZ 格式点云文件（简单文本格式）。
    /// </summary>
    private (Mat, Mat?) ReadXyzFile(string filePath)
    {
        var points = new List<float[]>();

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var values = line.Split(
                    new[] { ' ', '\t', ',' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                if (values.Length >= 3)
                {
                    var point = new float[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        point[i] = float.Parse(values[i]);
                    }
                    points.Add(point);
                }
            }
        }

        return (ConvertToMat(points), null);
    }

    /// <summary>
    /// 读取 PTS 格式点云文件（Leica 格式）。
    /// </summary>
    private (Mat, Mat?) ReadPtsFile(string filePath)
    {
        var points = new List<float[]>();
        var colors = new List<byte[]>();

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            bool firstLine = true;
            int pointCount = 0;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (firstLine)
                {
                    pointCount = int.Parse(line);
                    firstLine = false;
                    continue;
                }

                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (values.Length >= 3)
                {
                    var point = new float[3];
                    point[0] = float.Parse(values[0]);
                    point[1] = float.Parse(values[1]);
                    point[2] = float.Parse(values[2]);

                    if (values.Length >= 7)
                    {
                        colors.Add(
                            new[]
                            {
                                (byte)float.Parse(values[6]), // B
                                (byte)float.Parse(values[5]), // G
                                (byte)float.Parse(values[4]), // R
                            }
                        );
                    }

                    points.Add(point);
                }

                if (pointCount > 0 && points.Count >= pointCount)
                    break;
            }
        }

        return (ConvertToMat(points), colors.Count > 0 ? ConvertColorsToMat(colors) : null);
    }

    /// <summary>
    /// 将点列表转换为 Mat 对象。
    /// </summary>
    private Mat ConvertToMat(List<float[]> points)
    {
        if (points.Count == 0)
            return new Mat();

        int columns = points[0].Length;
        Mat mat = new Mat(points.Count, columns, MatType.CV_32FC1);

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                mat.Set(i, j, points[i][j]);
            }
        }

        return mat;
    }

    /// <summary>
    /// 将颜色列表转换为 Mat 对象，类型为 CV_8UC3（BGR 顺序）。
    /// </summary>
    private Mat ConvertColorsToMat(List<byte[]> colors)
    {
        if (colors.Count == 0)
            return new Mat();

        Mat mat = new Mat(colors.Count, 3, MatType.CV_8UC1);

        for (int i = 0; i < colors.Count; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                mat.Set<byte>(i, j, colors[i][j]);
            }
        }

        return mat;
    }

    /// <summary>
    /// 释放算子持有的资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
