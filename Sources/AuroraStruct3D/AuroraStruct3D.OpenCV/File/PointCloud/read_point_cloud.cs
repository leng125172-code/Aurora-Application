using System.Buffers.Binary;
using System.Globalization;
using System.Text;

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
        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan
        );
        PlyHeader header = ReadPlyHeader(stream);
        var points = new List<float[]>(header.VertexCount);
        var colors = header.HasColors ? new List<byte[]>(header.VertexCount) : null;

        if (header.Format == PlyFormat.Ascii)
        {
            ReadAsciiPlyVertices(stream, header, points, colors);
        }
        else
        {
            ReadBinaryPlyVertices(stream, header, points, colors);
        }

        return (
            ConvertToMat(points),
            colors is { Count: > 0 } ? ConvertColorsToMat(colors) : null
        );
    }

    private static PlyHeader ReadPlyHeader(Stream stream)
    {
        string? firstLine = ReadAsciiLine(stream);
        if (!string.Equals(firstLine?.Trim(), "ply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PLY 文件头无效：缺少 ply 标识。");

        PlyFormat? format = null;
        int vertexCount = -1;
        bool readingVertexProperties = false;
        var properties = new List<PlyProperty>();

        while (true)
        {
            string line = ReadAsciiLine(stream)?.Trim()
                ?? throw new InvalidDataException("PLY 文件头不完整：缺少 end_header。");
            if (line.Length == 0 || line.StartsWith("comment ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                break;

            string[] parts = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length >= 3 && parts[0].Equals("format", StringComparison.OrdinalIgnoreCase))
            {
                format = parts[1].ToLowerInvariant() switch
                {
                    "ascii" => PlyFormat.Ascii,
                    "binary_little_endian" => PlyFormat.BinaryLittleEndian,
                    "binary_big_endian" => PlyFormat.BinaryBigEndian,
                    _ => throw new InvalidDataException($"不支持的 PLY 编码格式：{parts[1]}。"),
                };
            }
            else if (
                parts.Length >= 3
                && parts[0].Equals("element", StringComparison.OrdinalIgnoreCase)
            )
            {
                readingVertexProperties = parts[1].Equals(
                    "vertex",
                    StringComparison.OrdinalIgnoreCase
                );
                if (readingVertexProperties)
                {
                    if (
                        !int.TryParse(
                            parts[2],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out vertexCount
                        )
                        || vertexCount < 0
                    )
                        throw new InvalidDataException($"PLY 顶点数量无效：{parts[2]}。");
                }
            }
            else if (
                readingVertexProperties
                && parts.Length >= 3
                && parts[0].Equals("property", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("暂不支持顶点元素中的 PLY list 属性。");
                properties.Add(new PlyProperty(parts[^1].ToLowerInvariant(), ParsePlyScalarType(parts[1])));
            }
        }

        if (format is null)
            throw new InvalidDataException("PLY 文件头不完整：缺少 format 声明。");
        if (vertexCount < 0)
            throw new InvalidDataException("PLY 文件头不完整：缺少 element vertex 声明。");

        var header = new PlyHeader(format.Value, vertexCount, properties);
        if (header.XIndex < 0 || header.YIndex < 0 || header.ZIndex < 0)
            throw new InvalidDataException("PLY 顶点属性必须包含 x、y、z。");
        return header;
    }

    private static void ReadAsciiPlyVertices(
        Stream stream,
        PlyHeader header,
        List<float[]> points,
        List<byte[]>? colors
    )
    {
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: true
        );
        while (points.Count < header.VertexCount)
        {
            string line = reader.ReadLine()
                ?? throw new InvalidDataException(
                    $"PLY 顶点数据提前结束：声明 {header.VertexCount}，实际 {points.Count}。"
                );
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] values = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (values.Length < header.Properties.Count)
                throw new InvalidDataException(
                    $"PLY 顶点第 {points.Count + 1} 行列数不足：需要 {header.Properties.Count}，实际 {values.Length}。"
                );

            double[] parsed = new double[header.Properties.Count];
            for (int i = 0; i < parsed.Length; i++)
            {
                if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                    throw new InvalidDataException(
                        $"PLY 顶点第 {points.Count + 1} 行属性 {header.Properties[i].Name} 不是有效数字。"
                    );
            }
            AddPlyVertex(header, parsed, points, colors);
        }
    }

    private static void ReadBinaryPlyVertices(
        Stream stream,
        PlyHeader header,
        List<float[]> points,
        List<byte[]>? colors
    )
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        bool littleEndian = header.Format == PlyFormat.BinaryLittleEndian;
        try
        {
            for (int row = 0; row < header.VertexCount; row++)
            {
                double[] values = new double[header.Properties.Count];
                for (int i = 0; i < values.Length; i++)
                    values[i] = ReadPlyScalar(reader, header.Properties[i].Type, littleEndian);
                AddPlyVertex(header, values, points, colors);
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(
                $"PLY 二进制顶点数据提前结束：声明 {header.VertexCount}，实际 {points.Count}。",
                ex
            );
        }
    }

    private static void AddPlyVertex(
        PlyHeader header,
        double[] values,
        List<float[]> points,
        List<byte[]>? colors
    )
    {
        var point = new float[header.HasNormals ? 6 : 3];
        point[0] = checked((float)values[header.XIndex]);
        point[1] = checked((float)values[header.YIndex]);
        point[2] = checked((float)values[header.ZIndex]);
        if (header.HasNormals)
        {
            point[3] = checked((float)values[header.NxIndex]);
            point[4] = checked((float)values[header.NyIndex]);
            point[5] = checked((float)values[header.NzIndex]);
        }
        points.Add(point);

        if (colors is not null)
        {
            colors.Add(
                [
                    ToColorByte(values[header.BlueIndex]),
                    ToColorByte(values[header.GreenIndex]),
                    ToColorByte(values[header.RedIndex]),
                ]
            );
        }
    }

    private static byte ToColorByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue);

    private static string? ReadAsciiLine(Stream stream)
    {
        var bytes = new List<byte>(128);
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (value == '\n')
                break;
            if (value != '\r')
                bytes.Add((byte)value);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static PlyScalarType ParsePlyScalarType(string value) =>
        value.ToLowerInvariant() switch
        {
            "char" or "int8" => PlyScalarType.Int8,
            "uchar" or "uint8" => PlyScalarType.UInt8,
            "short" or "int16" => PlyScalarType.Int16,
            "ushort" or "uint16" => PlyScalarType.UInt16,
            "int" or "int32" => PlyScalarType.Int32,
            "uint" or "uint32" => PlyScalarType.UInt32,
            "float" or "float32" => PlyScalarType.Float32,
            "double" or "float64" => PlyScalarType.Float64,
            _ => throw new InvalidDataException($"不支持的 PLY 属性类型：{value}。"),
        };

    private static double ReadPlyScalar(
        BinaryReader reader,
        PlyScalarType type,
        bool littleEndian
    ) =>
        type switch
        {
            PlyScalarType.Int8 => unchecked((sbyte)reader.ReadByte()),
            PlyScalarType.UInt8 => reader.ReadByte(),
            PlyScalarType.Int16 => ReadInt16(reader, littleEndian),
            PlyScalarType.UInt16 => ReadUInt16(reader, littleEndian),
            PlyScalarType.Int32 => ReadInt32(reader, littleEndian),
            PlyScalarType.UInt32 => ReadUInt32(reader, littleEndian),
            PlyScalarType.Float32 => BitConverter.Int32BitsToSingle(ReadInt32(reader, littleEndian)),
            PlyScalarType.Float64 => BitConverter.Int64BitsToDouble(ReadInt64(reader, littleEndian)),
            _ => throw new InvalidDataException($"不支持的 PLY 属性类型：{type}。"),
        };

    private static short ReadInt16(BinaryReader reader, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[2];
        reader.BaseStream.ReadExactly(bytes);
        return littleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadInt16BigEndian(bytes);
    }

    private static ushort ReadUInt16(BinaryReader reader, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[2];
        reader.BaseStream.ReadExactly(bytes);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static int ReadInt32(BinaryReader reader, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[4];
        reader.BaseStream.ReadExactly(bytes);
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    private static uint ReadUInt32(BinaryReader reader, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[4];
        reader.BaseStream.ReadExactly(bytes);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static long ReadInt64(BinaryReader reader, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[8];
        reader.BaseStream.ReadExactly(bytes);
        return littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    private enum PlyFormat
    {
        Ascii,
        BinaryLittleEndian,
        BinaryBigEndian,
    }

    private enum PlyScalarType
    {
        Int8,
        UInt8,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float32,
        Float64,
    }

    private sealed record PlyProperty(string Name, PlyScalarType Type);

    private sealed class PlyHeader
    {
        public PlyHeader(PlyFormat format, int vertexCount, List<PlyProperty> properties)
        {
            Format = format;
            VertexCount = vertexCount;
            Properties = properties;
            XIndex = Find("x");
            YIndex = Find("y");
            ZIndex = Find("z");
            NxIndex = Find("nx");
            NyIndex = Find("ny");
            NzIndex = Find("nz");
            RedIndex = Find("red");
            GreenIndex = Find("green");
            BlueIndex = Find("blue");
        }

        public PlyFormat Format { get; }
        public int VertexCount { get; }
        public List<PlyProperty> Properties { get; }
        public int XIndex { get; }
        public int YIndex { get; }
        public int ZIndex { get; }
        public int NxIndex { get; }
        public int NyIndex { get; }
        public int NzIndex { get; }
        public int RedIndex { get; }
        public int GreenIndex { get; }
        public int BlueIndex { get; }
        public bool HasNormals => NxIndex >= 0 && NyIndex >= 0 && NzIndex >= 0;
        public bool HasColors => RedIndex >= 0 && GreenIndex >= 0 && BlueIndex >= 0;

        private int Find(string name) =>
            Properties.FindIndex(property => property.Name.Equals(name, StringComparison.Ordinal));
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
