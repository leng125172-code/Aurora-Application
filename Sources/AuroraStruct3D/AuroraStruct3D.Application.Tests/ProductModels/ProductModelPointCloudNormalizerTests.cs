using System.Globalization;
using System.Text;
using AuroraStruct3D.ProductModels;
using Xunit;

namespace AuroraStruct3D.Application.Tests.ProductModels;

public class ProductModelPointCloudNormalizerTests
{
    [Fact]
    public void Normalize_Should_Sample_Obj_Triangle_Surface_Deterministically()
    {
        const string obj = """
            v 0 0 0
            v 10 0 0
            v 0 10 0
            f 1 2 3
            """;
        string first = NewTempPath();
        string second = NewTempPath();
        try
        {
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(obj)))
                ProductModelPointCloudNormalizer.Normalize(
                    source,
                    ".obj",
                    first,
                    ProductModelLengthUnit.Millimeter,
                    1d,
                    1_000
                );
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(obj)))
                ProductModelPointCloudNormalizer.Normalize(
                    source,
                    ".obj",
                    second,
                    ProductModelLengthUnit.Millimeter,
                    1d,
                    1_000
                );

            string text = File.ReadAllText(first);
            Assert.Contains("element vertex 50", text);
            Assert.Equal(text, File.ReadAllText(second));
            float[][] points = ReadPoints(text);
            Assert.Contains(points, point => point[0] > 0.1f && point[1] > 0.1f);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Theory]
    [InlineData(ProductModelLengthUnit.Millimeter, 1d)]
    [InlineData(ProductModelLengthUnit.Centimeter, 10d)]
    [InlineData(ProductModelLengthUnit.Meter, 1000d)]
    [InlineData(ProductModelLengthUnit.Inch, 25.4d)]
    public void Normalize_Should_Convert_Point_Cloud_To_Millimeters(
        ProductModelLengthUnit unit,
        double expectedX
    )
    {
        const string ply = """
            ply
            format ascii 1.0
            element vertex 1
            property float x
            property float y
            property float z
            end_header
            1 2 3
            """;
        string output = NewTempPath();
        try
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(ply));
            ProductModelPointCloudNormalizer.Normalize(source, ".ply", output, unit, 0.5d, 100);
            float[][] points = ReadPoints(File.ReadAllText(output));
            Assert.Single(points);
            Assert.Equal(expectedX, points[0][0], precision: 4);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void Normalize_Should_Respect_Maximum_Point_Count()
    {
        const string obj = """
            v 0 0 0
            v 100 0 0
            v 0 100 0
            f 1 2 3
            """;
        string output = NewTempPath();
        try
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(obj));
            ProductModelPointCloudNormalizer.Normalize(
                source,
                ".obj",
                output,
                ProductModelLengthUnit.Millimeter,
                0.05d,
                123
            );
            Assert.Equal(123, ReadPoints(File.ReadAllText(output)).Length);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void Normalize_Should_Read_SolidWorks_Binary_Little_Endian_Ply()
    {
        const string header = """
            ply
            format binary_little_endian 1.0
            comment SOLIDWORKS generated
            element vertex 3
            property float x
            property float y
            property float z
            element face 1
            property uchar red
            property uchar green
            property uchar blue
            property uchar alpha
            property list uchar int vertex_indices
            end_header
            """;
        using var source = new MemoryStream();
        byte[] headerBytes = Encoding.ASCII.GetBytes(header.Replace("\r", string.Empty) + "\n");
        source.Write(headerBytes);
        using (var writer = new BinaryWriter(source, Encoding.ASCII, leaveOpen: true))
        {
            foreach ((float x, float y, float z) in new[]
                     {
                         (0f, 0f, 0f),
                         (10f, 0f, 0f),
                         (0f, 10f, 0f),
                     })
            {
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
            }
            writer.Write((byte)10);
            writer.Write((byte)20);
            writer.Write((byte)30);
            writer.Write((byte)255);
            writer.Write((byte)3);
            writer.Write(0);
            writer.Write(1);
            writer.Write(2);
        }
        source.Position = 0;
        string output = NewTempPath();
        try
        {
            ProductModelPointCloudNormalizer.Normalize(
                source,
                ".ply",
                output,
                ProductModelLengthUnit.Millimeter,
                5d,
                100
            );
            string normalized = File.ReadAllText(output);
            Assert.Contains("element vertex 2", normalized);
            Assert.Contains("property float nx", normalized);
            Assert.Equal(2, ReadPoints(normalized).Length);
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"aurora-normalized-{Guid.NewGuid():N}.ply");

    private static float[][] ReadPoints(string ply)
    {
        string[] lines = ply.Replace("\r", string.Empty).Split('\n');
        int start = Array.IndexOf(lines, "end_header") + 1;
        return lines[start..]
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
                line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(3)
                    .Select(value => float.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray()
            )
            .ToArray();
    }
}
