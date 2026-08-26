using System.Globalization;
using System.Numerics;
using System.Text;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 将产品网格或点云转换成毫米制、确定性采样的 ASCII PLY 参考点云。
/// </summary>
internal static class ProductModelPointCloudNormalizer
{
    private readonly record struct Vertex(
        Vector3 Position,
        Vector3 Normal,
        bool HasNormal,
        Vector3 Color,
        bool HasColor
    );

    private readonly record struct Triangle(int A, int B, int C);

    public static void Normalize(
        Stream source,
        string extension,
        string destinationPath,
        ProductModelLengthUnit lengthUnit,
        double samplingSpacingMm,
        int maxPointCount,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxPointCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPointCount));

        double scale = lengthUnit.ToMillimeterScale();
        (List<Vertex> vertices, List<Triangle> triangles) = extension.ToLowerInvariant() switch
        {
            ".ply" => ReadPly(source, scale, cancellationToken),
            ".obj" => ReadObj(source, scale, cancellationToken),
            _ => throw new InvalidDataException(
                $"格式 {extension} 不能生成工作流参考点云，请先转换为 PLY 或 OBJ。"
            ),
        };

        if (vertices.Count == 0)
            throw new InvalidDataException("产品数模没有可读取的顶点。" );

        IReadOnlyList<Vertex> points = triangles.Count > 0
            ? SampleSurface(vertices, triangles, samplingSpacingMm, maxPointCount, cancellationToken)
            : DownsamplePoints(vertices, maxPointCount);

        WriteAsciiPly(destinationPath, points, cancellationToken);
    }

    private static (List<Vertex>, List<Triangle>) ReadObj(
        Stream source,
        double scale,
        CancellationToken cancellationToken
    )
    {
        var vertices = new List<Vertex>();
        var triangles = new List<Triangle>();
        using var reader = new StreamReader(source, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        string? raw;
        int lineNumber = 0;
        while ((raw = reader.ReadLine()) is not null)
        {
            if ((++lineNumber & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            string line = raw.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] values = line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                if (values.Length < 4)
                    continue;
                Vector3 position = new(
                    ParseFloat(values[1]) * (float)scale,
                    ParseFloat(values[2]) * (float)scale,
                    ParseFloat(values[3]) * (float)scale
                );
                bool hasColor = values.Length >= 7;
                Vector3 color = hasColor
                    ? NormalizeObjColor(
                        new Vector3(
                            ParseFloat(values[4]),
                            ParseFloat(values[5]),
                            ParseFloat(values[6])
                        )
                    )
                    : default;
                vertices.Add(new Vertex(position, default, false, color, hasColor));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                string[] values = line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                if (values.Length < 4)
                    continue;
                var indices = new List<int>(values.Length - 1);
                for (int i = 1; i < values.Length; i++)
                {
                    string vertexToken = values[i].Split('/')[0];
                    if (!int.TryParse(vertexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                        continue;
                    int zeroBased = index > 0 ? index - 1 : vertices.Count + index;
                    if (zeroBased >= 0 && zeroBased < vertices.Count)
                        indices.Add(zeroBased);
                }
                for (int i = 1; i + 1 < indices.Count; i++)
                    triangles.Add(new Triangle(indices[0], indices[i], indices[i + 1]));
            }
        }
        return (vertices, triangles);
    }

    private sealed record PlyProperty(
        string Name,
        string ScalarType,
        string? ListCountType = null,
        string? ListValueType = null
    )
    {
        public bool IsList => ListCountType is not null;
    }

    private sealed class PlyElement(string name, int count)
    {
        public string Name { get; } = name;
        public int Count { get; } = count;
        public List<PlyProperty> Properties { get; } = [];
    }

    private sealed record PlyHeader(string Format, List<PlyElement> Elements);

    private static (List<Vertex>, List<Triangle>) ReadPly(
        Stream source,
        double scale,
        CancellationToken cancellationToken
    )
    {
        PlyHeader header = ReadPlyHeader(source);
        PlyElement? vertexElement = header.Elements.FirstOrDefault(x => x.Name == "vertex");
        if (vertexElement is null || vertexElement.Count <= 0)
            throw new InvalidDataException("PLY 文件不包含顶点。");
        if (!vertexElement.Properties.Any(x => x.Name == "x")
            || !vertexElement.Properties.Any(x => x.Name == "y")
            || !vertexElement.Properties.Any(x => x.Name == "z"))
            throw new InvalidDataException("PLY 顶点缺少 x/y/z 属性。");

        return header.Format switch
        {
            "ascii" => ReadAsciiPlyBody(source, header, scale, cancellationToken),
            "binary_little_endian" => ReadBinaryLittleEndianPlyBody(
                source, header, scale, cancellationToken
            ),
            "binary_big_endian" => throw new InvalidDataException(
                "暂不支持 binary_big_endian PLY，请转换为 ASCII 或 binary_little_endian PLY。"
            ),
            _ => throw new InvalidDataException($"不支持的 PLY 格式：{header.Format}。"),
        };
    }

    private static PlyHeader ReadPlyHeader(Stream source)
    {
        string? first = ReadHeaderLine(source);
        if (!string.Equals(first?.Trim(), "ply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("无效的 PLY 文件头。");

        string format = string.Empty;
        var elements = new List<PlyElement>();
        PlyElement? currentElement = null;
        while (true)
        {
            string line = ReadHeaderLine(source)
                ?? throw new InvalidDataException("PLY 文件头缺少 end_header。");
            string trimmed = line.Trim();
            if (trimmed.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                break;
            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].Equals("comment", StringComparison.OrdinalIgnoreCase))
                continue;
            if (parts[0].Equals("format", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                format = parts[1].ToLowerInvariant();
            }
            else if (parts[0].Equals("element", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
            {
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                    || count < 0)
                    throw new InvalidDataException($"PLY 元素数量无效：{trimmed}");
                currentElement = new PlyElement(parts[1].ToLowerInvariant(), count);
                elements.Add(currentElement);
            }
            else if (parts[0].Equals("property", StringComparison.OrdinalIgnoreCase))
            {
                if (currentElement is null)
                    throw new InvalidDataException("PLY property 出现在 element 之前。");
                if (parts.Length >= 5 && parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    currentElement.Properties.Add(
                        new PlyProperty(
                            parts[4].ToLowerInvariant(),
                            string.Empty,
                            parts[2].ToLowerInvariant(),
                            parts[3].ToLowerInvariant()
                        )
                    );
                }
                else if (parts.Length >= 3)
                {
                    currentElement.Properties.Add(
                        new PlyProperty(parts[2].ToLowerInvariant(), parts[1].ToLowerInvariant())
                    );
                }
                else
                {
                    throw new InvalidDataException($"PLY property 定义无效：{trimmed}");
                }
            }
        }
        if (string.IsNullOrWhiteSpace(format))
            throw new InvalidDataException("PLY 文件头缺少 format。");
        return new PlyHeader(format, elements);
    }

    private static string? ReadHeaderLine(Stream source)
    {
        var bytes = new List<byte>(128);
        while (true)
        {
            int value = source.ReadByte();
            if (value < 0)
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (value == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
            if (value != '\r')
                bytes.Add((byte)value);
            if (bytes.Count > 64 * 1024)
                throw new InvalidDataException("PLY 文件头单行过长。");
        }
    }

    private static (List<Vertex>, List<Triangle>) ReadAsciiPlyBody(
        Stream source,
        PlyHeader header,
        double scale,
        CancellationToken cancellationToken
    )
    {
        int vertexCapacity = header.Elements.First(x => x.Name == "vertex").Count;
        var vertices = new List<Vertex>(vertexCapacity);
        var triangles = new List<Triangle>();
        using var reader = new StreamReader(source, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        foreach (PlyElement element in header.Elements)
        {
            for (int row = 0; row < element.Count; row++)
            {
                if ((row & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                string line = reader.ReadLine()
                    ?? throw new InvalidDataException($"PLY {element.Name} 数据提前结束。");
                string[] values = line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                int offset = 0;
                if (element.Name == "vertex")
                {
                    Dictionary<string, double> fields = ReadAsciiFields(
                        element.Properties, values, ref offset, null
                    );
                    vertices.Add(CreateVertex(fields, scale));
                }
                else if (element.Name == "face")
                {
                    var indices = new List<int>();
                    ReadAsciiFields(element.Properties, values, ref offset, indices);
                    AddTriangles(indices, vertices.Count, triangles);
                }
                else
                {
                    ReadAsciiFields(element.Properties, values, ref offset, null);
                }
            }
        }
        return (vertices, triangles);
    }

    private static Dictionary<string, double> ReadAsciiFields(
        IReadOnlyList<PlyProperty> properties,
        string[] values,
        ref int offset,
        List<int>? vertexIndices
    )
    {
        var fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (PlyProperty property in properties)
        {
            if (property.IsList)
            {
                if (offset >= values.Length)
                    throw new InvalidDataException("PLY list 缺少元素数量。");
                int count = int.Parse(values[offset++], NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (count < 0 || offset + count > values.Length)
                    throw new InvalidDataException("PLY list 长度无效。");
                for (int i = 0; i < count; i++)
                {
                    if (vertexIndices is not null
                        && property.Name is "vertex_indices" or "vertex_index")
                        vertexIndices.Add(int.Parse(values[offset], NumberStyles.Integer, CultureInfo.InvariantCulture));
                    offset++;
                }
            }
            else
            {
                if (offset >= values.Length)
                    throw new InvalidDataException($"PLY 属性 {property.Name} 缺少值。");
                fields[property.Name] = double.Parse(
                    values[offset++], NumberStyles.Float, CultureInfo.InvariantCulture
                );
            }
        }
        return fields;
    }

    private static (List<Vertex>, List<Triangle>) ReadBinaryLittleEndianPlyBody(
        Stream source,
        PlyHeader header,
        double scale,
        CancellationToken cancellationToken
    )
    {
        int vertexCapacity = header.Elements.First(x => x.Name == "vertex").Count;
        var vertices = new List<Vertex>(vertexCapacity);
        var triangles = new List<Triangle>();
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
        foreach (PlyElement element in header.Elements)
        {
            for (int row = 0; row < element.Count; row++)
            {
                if ((row & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, double>? fields = element.Name == "vertex"
                    ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    : null;
                List<int>? indices = element.Name == "face" ? [] : null;
                foreach (PlyProperty property in element.Properties)
                {
                    if (property.IsList)
                    {
                        int count = ReadBinaryListCount(reader, property.ListCountType!);
                        for (int i = 0; i < count; i++)
                        {
                            double value = ReadBinaryScalar(reader, property.ListValueType!);
                            if (indices is not null
                                && property.Name is "vertex_indices" or "vertex_index")
                                indices.Add(checked((int)value));
                        }
                    }
                    else
                    {
                        double value = ReadBinaryScalar(reader, property.ScalarType);
                        if (fields is not null)
                            fields[property.Name] = value;
                    }
                }
                if (fields is not null)
                    vertices.Add(CreateVertex(fields, scale));
                if (indices is not null)
                    AddTriangles(indices, vertices.Count, triangles);
            }
        }
        return (vertices, triangles);
    }

    private static int ReadBinaryListCount(BinaryReader reader, string type)
    {
        double value = ReadBinaryScalar(reader, type);
        if (!double.IsFinite(value) || value < 0 || value > 10_000_000 || value != Math.Truncate(value))
            throw new InvalidDataException($"PLY list 长度无效：{value}。");
        return checked((int)value);
    }

    private static double ReadBinaryScalar(BinaryReader reader, string type) => type switch
    {
        "char" or "int8" => reader.ReadSByte(),
        "uchar" or "uint8" => reader.ReadByte(),
        "short" or "int16" => reader.ReadInt16(),
        "ushort" or "uint16" => reader.ReadUInt16(),
        "int" or "int32" => reader.ReadInt32(),
        "uint" or "uint32" => reader.ReadUInt32(),
        "float" or "float32" => reader.ReadSingle(),
        "double" or "float64" => reader.ReadDouble(),
        _ => throw new InvalidDataException($"不支持的 PLY 属性类型：{type}。"),
    };

    private static Vertex CreateVertex(IReadOnlyDictionary<string, double> fields, double scale)
    {
        if (!fields.TryGetValue("x", out double x)
            || !fields.TryGetValue("y", out double y)
            || !fields.TryGetValue("z", out double z))
            throw new InvalidDataException("PLY 顶点缺少 x/y/z 属性值。");
        double nx = 0;
        double ny = 0;
        double nz = 0;
        double red = 0;
        double green = 0;
        double blue = 0;
        bool hasNormal = fields.TryGetValue("nx", out nx)
            && fields.TryGetValue("ny", out ny)
            && fields.TryGetValue("nz", out nz);
        bool hasColor = fields.TryGetValue("red", out red)
            && fields.TryGetValue("green", out green)
            && fields.TryGetValue("blue", out blue);
        return new Vertex(
            new Vector3((float)(x * scale), (float)(y * scale), (float)(z * scale)),
            hasNormal ? new Vector3((float)nx, (float)ny, (float)nz) : default,
            hasNormal,
            hasColor ? new Vector3((float)red, (float)green, (float)blue) : default,
            hasColor
        );
    }

    private static void AddTriangles(
        IReadOnlyList<int> indices,
        int vertexCount,
        ICollection<Triangle> triangles
    )
    {
        if (indices.Count < 3 || indices.Any(index => index < 0 || index >= vertexCount))
            return;
        for (int i = 1; i + 1 < indices.Count; i++)
            triangles.Add(new Triangle(indices[0], indices[i], indices[i + 1]));
    }

    private static IReadOnlyList<Vertex> SampleSurface(
        IReadOnlyList<Vertex> vertices,
        IReadOnlyList<Triangle> triangles,
        double spacingMm,
        int maxPointCount,
        CancellationToken cancellationToken
    )
    {
        var usable = new List<(Triangle Triangle, double CumulativeArea)>();
        double totalArea = 0;
        foreach (Triangle triangle in triangles)
        {
            Vector3 a = vertices[triangle.A].Position;
            Vector3 b = vertices[triangle.B].Position;
            Vector3 c = vertices[triangle.C].Position;
            double area = Vector3.Cross(b - a, c - a).Length() * 0.5d;
            if (!double.IsFinite(area) || area <= 1e-12d)
                continue;
            totalArea += area;
            usable.Add((triangle, totalArea));
        }
        if (usable.Count == 0 || totalArea <= 0)
            return DownsamplePoints(vertices, maxPointCount);

        double desired = Math.Ceiling(totalArea / (spacingMm * spacingMm));
        int count = (int)Math.Clamp(desired, 1d, maxPointCount);
        var result = new List<Vertex>(count);
        int triangleIndex = 0;
        for (int i = 0; i < count; i++)
        {
            if ((i & 0x3fff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            double areaTarget = (i + 0.5d) * totalArea / count;
            while (
                triangleIndex + 1 < usable.Count
                && usable[triangleIndex].CumulativeArea < areaTarget
            )
                triangleIndex++;

            Triangle triangle = usable[triangleIndex].Triangle;
            Vertex va = vertices[triangle.A];
            Vertex vb = vertices[triangle.B];
            Vertex vc = vertices[triangle.C];
            double u = RadicalInverse((uint)i + 1u, 2u);
            double v = RadicalInverse((uint)i + 1u, 3u);
            float root = (float)Math.Sqrt(u);
            float w0 = 1f - root;
            float w1 = root * (1f - (float)v);
            float w2 = root * (float)v;
            Vector3 position = va.Position * w0 + vb.Position * w1 + vc.Position * w2;
            Vector3 normal = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
            if (normal.LengthSquared() > 1e-20f)
                normal = Vector3.Normalize(normal);
            bool hasColor = va.HasColor && vb.HasColor && vc.HasColor;
            Vector3 color = hasColor
                ? va.Color * w0 + vb.Color * w1 + vc.Color * w2
                : default;
            result.Add(new Vertex(position, normal, true, color, hasColor));
        }
        return result;
    }

    private static IReadOnlyList<Vertex> DownsamplePoints(
        IReadOnlyList<Vertex> points,
        int maxPointCount
    )
    {
        if (points.Count <= maxPointCount)
            return points;
        var sampled = new List<Vertex>(maxPointCount);
        for (int i = 0; i < maxPointCount; i++)
        {
            int index = (int)((long)i * points.Count / maxPointCount);
            sampled.Add(points[index]);
        }
        return sampled;
    }

    private static void WriteAsciiPly(
        string destinationPath,
        IReadOnlyList<Vertex> points,
        CancellationToken cancellationToken
    )
    {
        bool hasNormals = points.Count > 0 && points.All(x => x.HasNormal);
        bool hasColors = points.Count > 0 && points.All(x => x.HasColor);
        using var file = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.SequentialScan
        );
        using var writer = new StreamWriter(file, new UTF8Encoding(false), 64 * 1024);
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine("comment coordinates normalized to millimeters by AuroraStruct3D");
        writer.WriteLine($"element vertex {points.Count}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        if (hasColors)
        {
            writer.WriteLine("property uchar red");
            writer.WriteLine("property uchar green");
            writer.WriteLine("property uchar blue");
        }
        if (hasNormals)
        {
            writer.WriteLine("property float nx");
            writer.WriteLine("property float ny");
            writer.WriteLine("property float nz");
        }
        writer.WriteLine("end_header");
        for (int i = 0; i < points.Count; i++)
        {
            if ((i & 0x3fff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            Vertex point = points[i];
            writer.Write(point.Position.X.ToString("G9", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(point.Position.Y.ToString("G9", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(point.Position.Z.ToString("G9", CultureInfo.InvariantCulture));
            if (hasColors)
            {
                writer.Write(' ');
                writer.Write(ToByte(point.Color.X));
                writer.Write(' ');
                writer.Write(ToByte(point.Color.Y));
                writer.Write(' ');
                writer.Write(ToByte(point.Color.Z));
            }
            if (hasNormals)
            {
                writer.Write(' ');
                writer.Write(point.Normal.X.ToString("G9", CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(point.Normal.Y.ToString("G9", CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(point.Normal.Z.ToString("G9", CultureInfo.InvariantCulture));
            }
            writer.WriteLine();
        }
    }

    private static float ParseFloat(string value) =>
        float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector3 NormalizeObjColor(Vector3 color) =>
        color.X <= 1f && color.Y <= 1f && color.Z <= 1f ? color * 255f : color;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static double RadicalInverse(uint value, uint radix)
    {
        double result = 0;
        double factor = 1d / radix;
        while (value > 0)
        {
            result += factor * (value % radix);
            value /= radix;
            factor /= radix;
        }
        return result;
    }
}
