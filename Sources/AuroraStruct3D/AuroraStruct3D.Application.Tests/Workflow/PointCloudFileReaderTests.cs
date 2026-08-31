using System.Buffers.Binary;
using System.Text;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class PointCloudFileReaderTests
{
    [Fact]
    public void ReadPointCloud_Should_Read_Ascii_Ply_By_Property_Name()
    {
        string path = NewTempPlyPath();
        File.WriteAllText(
            path,
            """
            ply
            format ascii 1.0
            element vertex 2
            property uchar blue
            property float z
            property uchar red
            property float x
            property uchar green
            property float y
            end_header
            30 3 10 1 20 2
            60 6 40 4 50 5
            """,
            Encoding.ASCII
        );

        try
        {
            PointCloudData cloud = Read(path);
            try
            {
                Assert.Equal(2, cloud.PointCount);
                Assert.Equal(1f, cloud.PointCloud!.Get<float>(0, 0));
                Assert.Equal(2f, cloud.PointCloud.Get<float>(0, 1));
                Assert.Equal(3f, cloud.PointCloud.Get<float>(0, 2));
                Assert.Equal((byte)30, cloud.Colors!.Get<byte>(0, 0));
                Assert.Equal((byte)20, cloud.Colors.Get<byte>(0, 1));
                Assert.Equal((byte)10, cloud.Colors.Get<byte>(0, 2));
            }
            finally
            {
                cloud.DisposePointCloud();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPointCloud_Should_Read_Binary_Ply(bool bigEndian)
    {
        string path = NewTempPlyPath();
        using (FileStream stream = File.Create(path))
        {
            string format = bigEndian ? "binary_big_endian" : "binary_little_endian";
            byte[] header = Encoding.ASCII.GetBytes(
                $"""
                ply
                format {format} 1.0
                element vertex 2
                property float x
                property float y
                property float z
                property uchar red
                property uchar green
                property uchar blue
                property float nx
                property float ny
                property float nz
                end_header
                """.Replace("\r\n", "\n") + "\n"
            );
            stream.Write(header);
            WriteVertex(stream, bigEndian, 1, 2, 3, 10, 20, 30, 0.1f, 0.2f, 0.3f);
            WriteVertex(stream, bigEndian, 4, 5, 6, 40, 50, 60, 0.4f, 0.5f, 0.6f);
        }

        try
        {
            PointCloudData cloud = Read(path);
            try
            {
                Assert.Equal(2, cloud.PointCount);
                Assert.Equal(6, cloud.PointCloud!.Cols);
                Assert.Equal(4f, cloud.PointCloud.Get<float>(1, 0));
                Assert.Equal(5f, cloud.PointCloud.Get<float>(1, 1));
                Assert.Equal(6f, cloud.PointCloud.Get<float>(1, 2));
                Assert.Equal(0.4f, cloud.PointCloud.Get<float>(1, 3), 5);
                Assert.Equal(0.5f, cloud.PointCloud.Get<float>(1, 4), 5);
                Assert.Equal(0.6f, cloud.PointCloud.Get<float>(1, 5), 5);
                Assert.Equal((byte)60, cloud.Colors!.Get<byte>(1, 0));
                Assert.Equal((byte)50, cloud.Colors.Get<byte>(1, 1));
                Assert.Equal((byte)40, cloud.Colors.Get<byte>(1, 2));
            }
            finally
            {
                cloud.DisposePointCloud();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PointCloudData Read(string path)
    {
        using var context = new WorkflowContext();
        context.Set("point_cloud_path", path);
        using var op = new read_point_cloud();
        op.Execute(context);
        PointCloudData value = Assert.IsType<PointCloudData>(context.Get("output_point_cloud"));
        PointCloudData copy = new() { Value = value.PointCloud!.Clone() };
        copy.SetColors(value.Colors?.Clone());
        return copy;
    }

    private static string NewTempPlyPath() =>
        Path.Combine(Path.GetTempPath(), $"aurora-ply-reader-{Guid.NewGuid():N}.ply");

    private static void WriteVertex(
        Stream stream,
        bool bigEndian,
        float x,
        float y,
        float z,
        byte red,
        byte green,
        byte blue,
        float nx,
        float ny,
        float nz
    )
    {
        WriteSingle(stream, x, bigEndian);
        WriteSingle(stream, y, bigEndian);
        WriteSingle(stream, z, bigEndian);
        stream.WriteByte(red);
        stream.WriteByte(green);
        stream.WriteByte(blue);
        WriteSingle(stream, nx, bigEndian);
        WriteSingle(stream, ny, bigEndian);
        WriteSingle(stream, nz, bigEndian);
    }

    private static void WriteSingle(Stream stream, float value, bool bigEndian)
    {
        Span<byte> bytes = stackalloc byte[4];
        int bits = BitConverter.SingleToInt32Bits(value);
        if (bigEndian)
            BinaryPrimitives.WriteInt32BigEndian(bytes, bits);
        else
            BinaryPrimitives.WriteInt32LittleEndian(bytes, bits);
        stream.Write(bytes);
    }
}
