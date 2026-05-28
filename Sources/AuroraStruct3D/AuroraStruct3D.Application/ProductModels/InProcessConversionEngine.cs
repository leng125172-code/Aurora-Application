using System.Numerics;
using System.Text;
using System.Text.Json;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 三维文件格式转换引擎实现。
/// 支持 STL、GLB、PCD → PLY 的纯托管代码转换，无需 native 依赖，兼容 linux-arm64。
/// GLTF 文本格式因可能含外部引用，建议用户转为 GLB 后上传。
/// STEP/IGES 需等待 OCC NativeAOT 封装，当前暂不支持。
/// </summary>
public class InProcessConversionEngine : IConversionEngine, ITransientDependency
{
    /// <inheritdoc/>
    public Task<Stream> ConvertToPlyAsync(
        Stream input,
        ProductModelFormat format,
        CancellationToken cancellationToken = default
    )
    {
        byte[] data = ReadAllBytes(input);

        Stream result = format switch
        {
            ProductModelFormat.STL => ConvertStlToPly(data),
            ProductModelFormat.GLB => ConvertGlbToPly(data),
            ProductModelFormat.PCD => ConvertPcdToPly(data),
            ProductModelFormat.GLTF => throw new NotSupportedException(
                "GLTF 文本格式可能包含外部引用文件，无法在服务端安全加载。建议将文件导出为 GLB 格式后重新上传。"
            ),
            ProductModelFormat.STEP or ProductModelFormat.IGES => throw new NotSupportedException(
                $"格式 {format} 需要 OCC NativeAOT 封装支持，当前版本暂不支持，请期待后续更新。"
            ),
            _ => throw new NotSupportedException($"不支持的文件格式：{format}"),
        };

        return Task.FromResult(result);
    }

    // ─────────────────────────── STL → PLY ───────────────────────────

    private static Stream ConvertStlToPly(byte[] data)
    {
        List<(Vector3 Normal, Vector3 V0, Vector3 V1, Vector3 V2)> triangles = IsBinaryStl(data)
            ? ParseBinaryStl(data)
            : ParseAsciiStl(data);
        return WritePlyMesh(triangles);
    }

    /// <summary>
    /// 通过文件大小与 binary STL 期望大小比较来判断格式。
    /// ASCII STL 以 "solid" 开头，但 binary 文件大小能精确匹配 84+N*50 字节。
    /// </summary>
    private static bool IsBinaryStl(byte[] data)
    {
        if (data.Length < 84)
            return false;
        uint count = BitConverter.ToUInt32(data, 80);
        return (long)data.Length == 84L + count * 50L;
    }

    private static List<(Vector3, Vector3, Vector3, Vector3)> ParseBinaryStl(byte[] data)
    {
        uint count = BitConverter.ToUInt32(data, 80);
        var result = new List<(Vector3, Vector3, Vector3, Vector3)>((int)count);
        int offset = 84;
        for (uint i = 0; i < count && offset + 50 <= data.Length; i++, offset += 50)
        {
            result.Add(
                (
                    ReadVec3(data, offset),
                    ReadVec3(data, offset + 12),
                    ReadVec3(data, offset + 24),
                    ReadVec3(data, offset + 36)
                )
            );
        }
        return result;
    }

    private static List<(Vector3, Vector3, Vector3, Vector3)> ParseAsciiStl(byte[] data)
    {
        var result = new List<(Vector3, Vector3, Vector3, Vector3)>();
        Vector3 normal = default;
        var verts = new Vector3[3];
        int vertIdx = 0;

        foreach (string raw in Encoding.UTF8.GetString(data).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("facet normal ", StringComparison.OrdinalIgnoreCase))
            {
                normal = ParseVec3FromTokens(line, 2);
                vertIdx = 0;
            }
            else if (line.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase) && vertIdx < 3)
            {
                verts[vertIdx++] = ParseVec3FromTokens(line, 1);
            }
            else if (
                line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase)
                && vertIdx == 3
            )
            {
                result.Add((normal, verts[0], verts[1], verts[2]));
            }
        }
        return result;
    }

    private static Stream WritePlyMesh(List<(Vector3 N, Vector3 V0, Vector3 V1, Vector3 V2)> tris)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {tris.Count * 3}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        sb.AppendLine("property float nx");
        sb.AppendLine("property float ny");
        sb.AppendLine("property float nz");
        sb.AppendLine($"element face {tris.Count}");
        sb.AppendLine("property list uchar int vertex_indices");
        sb.AppendLine("end_header");

        foreach (var (n, v0, v1, v2) in tris)
        {
            AppendVertex(sb, v0, n, ci);
            AppendVertex(sb, v1, n, ci);
            AppendVertex(sb, v2, n, ci);
        }
        for (int i = 0; i < tris.Count; i++)
            sb.AppendLine($"3 {i * 3} {i * 3 + 1} {i * 3 + 2}");

        return ToStream(sb);
    }

    private static void AppendVertex(
        StringBuilder sb,
        Vector3 p,
        Vector3 n,
        System.Globalization.CultureInfo ci
    )
    {
        sb.Append(p.X.ToString("G6", ci));
        sb.Append(' ');
        sb.Append(p.Y.ToString("G6", ci));
        sb.Append(' ');
        sb.Append(p.Z.ToString("G6", ci));
        sb.Append(' ');
        sb.Append(n.X.ToString("G6", ci));
        sb.Append(' ');
        sb.Append(n.Y.ToString("G6", ci));
        sb.Append(' ');
        sb.AppendLine(n.Z.ToString("G6", ci));
    }

    // ─────────────────────────── GLB → PLY ───────────────────────────

    private static Stream ConvertGlbToPly(byte[] data)
    {
        // GLB 文件头：magic "glTF"(4) + version(4) + length(4)
        if (
            data.Length < 12
            || data[0] != 'g'
            || data[1] != 'l'
            || data[2] != 'T'
            || data[3] != 'F'
        )
        {
            throw new InvalidDataException("无效的 GLB 文件头（magic 不匹配，应为 glTF）");
        }

        // JSON chunk: length(4) + type "JSON"(4) + data
        int jsonLen = (int)BitConverter.ToUInt32(data, 12);
        string json = Encoding.UTF8.GetString(data, 20, jsonLen);

        // BIN chunk（4 字节对齐后紧跟 JSON chunk）
        int binStart = 20 + ((jsonLen + 3) & ~3);
        byte[]? binData = null;
        if (binStart + 8 <= data.Length)
        {
            int binLen = (int)BitConverter.ToUInt32(data, binStart);
            uint binType = BitConverter.ToUInt32(data, binStart + 4);
            if (binType == 0x004E4942u) // "BIN\0"
                binData = data[(binStart + 8)..(binStart + 8 + binLen)];
        }

        return ExtractGltfGeometryToPly(json, binData);
    }

    private static Stream ExtractGltfGeometryToPly(string json, byte[]? bin)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 读取 bufferViews：每个条目记录 (byteOffset, byteLength)
        var bufferViews = root.TryGetProperty("bufferViews", out var bvProp)
            ? bvProp
                .EnumerateArray()
                .Select(bv =>
                    (
                        ByteOffset: bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0,
                        ByteLength: bv.GetProperty("byteLength").GetInt32()
                    )
                )
                .ToArray()
            : Array.Empty<(int ByteOffset, int ByteLength)>();

        // 读取 accessors：记录 (bufferView, byteOffset, componentType, count, type)
        var accessors = root.TryGetProperty("accessors", out var accProp)
            ? accProp
                .EnumerateArray()
                .Select(a =>
                    (
                        BufferView: a.TryGetProperty("bufferView", out var bv) ? bv.GetInt32() : -1,
                        ByteOffset: a.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0,
                        ComponentType: a.GetProperty("componentType").GetInt32(),
                        Count: a.GetProperty("count").GetInt32(),
                        Type: a.GetProperty("type").GetString() ?? string.Empty
                    )
                )
                .ToArray()
            : Array.Empty<(int, int, int, int, string)>();

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var faces = new List<(int A, int B, int C)>();

        if (!root.TryGetProperty("meshes", out var meshes))
            return WritePlyPointCloud(positions);

        foreach (var mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out var prims))
                continue;
            foreach (var prim in prims.EnumerateArray())
            {
                if (!prim.TryGetProperty("attributes", out var attrs))
                    continue;
                if (!attrs.TryGetProperty("POSITION", out var posAccIdxElem))
                    continue;

                int baseIdx = positions.Count;
                int posAccIdx = posAccIdxElem.GetInt32();
                var posVerts = ReadVec3Accessor(accessors[posAccIdx], bufferViews, bin);
                positions.AddRange(posVerts);

                // 法线（可选）
                if (attrs.TryGetProperty("NORMAL", out var normAccIdxElem))
                    normals.AddRange(
                        ReadVec3Accessor(accessors[normAccIdxElem.GetInt32()], bufferViews, bin)
                    );
                else
                    for (int i = 0; i < posVerts.Count; i++)
                        normals.Add(Vector3.Zero);

                // 索引
                if (prim.TryGetProperty("indices", out var idxProp))
                {
                    var (bvIdx, byteOff, compType, cnt, _) = accessors[idxProp.GetInt32()];
                    var indices = ReadIndexAccessor(
                        bvIdx,
                        byteOff,
                        compType,
                        cnt,
                        bufferViews,
                        bin
                    );
                    for (int i = 0; i + 2 < indices.Count; i += 3)
                        faces.Add(
                            (
                                baseIdx + indices[i],
                                baseIdx + indices[i + 1],
                                baseIdx + indices[i + 2]
                            )
                        );
                }
                else
                {
                    for (int i = baseIdx; i + 2 < baseIdx + posVerts.Count; i += 3)
                        faces.Add((i, i + 1, i + 2));
                }
            }
        }

        return WritePlyFromBuffers(positions, normals, faces);
    }

    private static List<Vector3> ReadVec3Accessor(
        (int BufferView, int ByteOffset, int ComponentType, int Count, string Type) acc,
        (int ByteOffset, int ByteLength)[] bufferViews,
        byte[]? bin
    )
    {
        var result = new List<Vector3>(acc.Count);
        if (acc.BufferView < 0 || bin == null)
            return result;

        int start = bufferViews[acc.BufferView].ByteOffset + acc.ByteOffset;
        for (int i = 0; i < acc.Count; i++)
        {
            int offset = start + i * 12;
            result.Add(
                new Vector3(
                    BitConverter.ToSingle(bin, offset),
                    BitConverter.ToSingle(bin, offset + 4),
                    BitConverter.ToSingle(bin, offset + 8)
                )
            );
        }
        return result;
    }

    private static List<int> ReadIndexAccessor(
        int bufferViewIdx,
        int byteOffset,
        int componentType,
        int count,
        (int ByteOffset, int ByteLength)[] bufferViews,
        byte[]? bin
    )
    {
        var result = new List<int>(count);
        if (bufferViewIdx < 0 || bin == null)
            return result;

        int start = bufferViews[bufferViewIdx].ByteOffset + byteOffset;
        // componentType: 5121=UNSIGNED_BYTE, 5123=UNSIGNED_SHORT, 5125=UNSIGNED_INT
        int stride = componentType switch
        {
            5121 => 1,
            5123 => 2,
            _ => 4,
        };

        for (int i = 0; i < count; i++)
        {
            int offset = start + i * stride;
            result.Add(
                componentType switch
                {
                    5121 => bin[offset],
                    5123 => BitConverter.ToUInt16(bin, offset),
                    _ => (int)BitConverter.ToUInt32(bin, offset),
                }
            );
        }
        return result;
    }

    // ─────────────────────────── PCD → PLY ───────────────────────────

    private static Stream ConvertPcdToPly(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        var fieldNames = new List<string>();
        string dataType = "ascii";
        bool headerDone = false;
        var positions = new List<Vector3>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (!headerDone)
            {
                if (line.StartsWith("FIELDS ", StringComparison.OrdinalIgnoreCase))
                    fieldNames.AddRange(
                        line[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    );
                else if (line.StartsWith("DATA ", StringComparison.OrdinalIgnoreCase))
                {
                    dataType = line[5..].Trim().ToLowerInvariant();
                    headerDone = true;
                }
                continue;
            }

            if (dataType != "ascii" || string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int xi = fieldNames.IndexOf("x"),
                yi = fieldNames.IndexOf("y"),
                zi = fieldNames.IndexOf("z");
            if (xi < 0 || yi < 0 || zi < 0 || Math.Max(xi, Math.Max(yi, zi)) >= parts.Length)
                continue;

            float.TryParse(parts[xi], System.Globalization.NumberStyles.Float, ci, out float x);
            float.TryParse(parts[yi], System.Globalization.NumberStyles.Float, ci, out float y);
            float.TryParse(parts[zi], System.Globalization.NumberStyles.Float, ci, out float z);
            positions.Add(new Vector3(x, y, z));
        }

        return WritePlyPointCloud(positions);
    }

    // ─────────────────────────── PLY Writers ───────────────────────────

    private static Stream WritePlyFromBuffers(
        List<Vector3> positions,
        List<Vector3> normals,
        List<(int A, int B, int C)> faces
    )
    {
        bool hasNormals = normals.Count == positions.Count && normals.Any(n => n != Vector3.Zero);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {positions.Count}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        if (hasNormals)
        {
            sb.AppendLine("property float nx");
            sb.AppendLine("property float ny");
            sb.AppendLine("property float nz");
        }
        if (faces.Count > 0)
        {
            sb.AppendLine($"element face {faces.Count}");
            sb.AppendLine("property list uchar int vertex_indices");
        }
        sb.AppendLine("end_header");

        for (int i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            sb.Append(p.X.ToString("G6", ci));
            sb.Append(' ');
            sb.Append(p.Y.ToString("G6", ci));
            sb.Append(' ');
            sb.Append(p.Z.ToString("G6", ci));
            if (hasNormals)
            {
                var n = normals[i];
                sb.Append(' ');
                sb.Append(n.X.ToString("G6", ci));
                sb.Append(' ');
                sb.Append(n.Y.ToString("G6", ci));
                sb.Append(' ');
                sb.Append(n.Z.ToString("G6", ci));
            }
            sb.AppendLine();
        }

        foreach (var (a, b, c) in faces)
            sb.AppendLine($"3 {a} {b} {c}");

        return ToStream(sb);
    }

    private static Stream WritePlyPointCloud(List<Vector3> positions)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {positions.Count}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        sb.AppendLine("end_header");
        foreach (var p in positions)
        {
            sb.Append(p.X.ToString("G6", ci));
            sb.Append(' ');
            sb.Append(p.Y.ToString("G6", ci));
            sb.Append(' ');
            sb.AppendLine(p.Z.ToString("G6", ci));
        }
        return ToStream(sb);
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static Vector3 ReadVec3(byte[] data, int offset) =>
        new(
            BitConverter.ToSingle(data, offset),
            BitConverter.ToSingle(data, offset + 4),
            BitConverter.ToSingle(data, offset + 8)
        );

    private static Vector3 ParseVec3FromTokens(string line, int start)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float x = 0,
            y = 0,
            z = 0;
        if (tokens.Length > start)
            float.TryParse(tokens[start], System.Globalization.NumberStyles.Float, ci, out x);
        if (tokens.Length > start + 1)
            float.TryParse(tokens[start + 1], System.Globalization.NumberStyles.Float, ci, out y);
        if (tokens.Length > start + 2)
            float.TryParse(tokens[start + 2], System.Globalization.NumberStyles.Float, ci, out z);
        return new Vector3(x, y, z);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms)
            return ms.ToArray();
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }

    private static Stream ToStream(StringBuilder sb) =>
        new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
}
