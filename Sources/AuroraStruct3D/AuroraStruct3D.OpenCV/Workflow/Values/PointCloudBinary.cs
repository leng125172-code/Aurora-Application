namespace AuroraStruct3D.OpenCV.Workflow.Values;

/// <summary>
/// <see cref="PointCloudData"/> 与原始二进制互转：依次写入坐标 Mat 与（可选）颜色 Mat，
/// 复用 <see cref="MatBinary"/> 的精确帧格式。
/// </summary>
internal static class PointCloudBinary
{
    /// <summary>编码点云（坐标 + 颜色）为字节数组。</summary>
    public static byte[] Encode(PointCloudData cloud)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            MatBinary.Write(writer, cloud.PointCloud);
            MatBinary.Write(writer, cloud.HasColors ? cloud.Colors : null);
        }
        return ms.ToArray();
    }

    /// <summary>从字节数组解码点云。</summary>
    public static PointCloudData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        Mat? coordinates = MatBinary.Read(reader);
        Mat? colors = MatBinary.Read(reader);

        var cloud = new PointCloudData { Value = coordinates };
        if (colors is not null)
            cloud.SetColors(colors);

        return cloud;
    }
}
