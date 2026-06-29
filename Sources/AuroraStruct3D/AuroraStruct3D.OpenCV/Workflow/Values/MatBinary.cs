using System.Runtime.InteropServices;

namespace AuroraStruct3D.OpenCV.Workflow.Values;

/// <summary>
/// <see cref="Mat"/> 与原始二进制的精确互转（类型/通道/位深完全保真，区别于 PNG 等图像编码）。
/// <para>
/// 帧格式（小端）：<c>int rows | int cols | int matType | int byteLen | byte[byteLen]</c>。
/// 空 / null Mat 写为 <c>0,0,0,0</c>。
/// </para>
/// </summary>
internal static class MatBinary
{
    /// <summary>把一个 Mat（可空）写入二进制流。</summary>
    public static void Write(BinaryWriter writer, Mat? mat)
    {
        if (mat is null || mat.Empty())
        {
            writer.Write(0); // rows
            writer.Write(0); // cols
            writer.Write(0); // type
            writer.Write(0); // byteLen
            return;
        }

        // 非连续（ROI/子矩阵）先克隆为连续内存，保证可整块拷贝。
        Mat continuous = mat.IsContinuous() ? mat : mat.Clone();
        try
        {
            long byteLen = (long)continuous.Total() * continuous.ElemSize();
            int len = checked((int)byteLen);

            var buffer = new byte[len];
            if (len > 0)
                Marshal.Copy(continuous.Data, buffer, 0, len);

            writer.Write(continuous.Rows);
            writer.Write(continuous.Cols);
            writer.Write(continuous.Type().Value);
            writer.Write(len);
            writer.Write(buffer);
        }
        finally
        {
            if (!ReferenceEquals(continuous, mat))
                continuous.Dispose();
        }
    }

    /// <summary>从二进制流读出一个 Mat；空帧返回 null。</summary>
    public static Mat? Read(BinaryReader reader)
    {
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        int type = reader.ReadInt32();
        int len = reader.ReadInt32();

        if (rows == 0 && cols == 0)
        {
            if (len > 0)
                reader.ReadBytes(len); // 容错：跳过潜在残留字节
            return null;
        }

        byte[] buffer = reader.ReadBytes(len);
        var mat = new Mat(rows, cols, (MatType)type);
        if (len > 0)
            Marshal.Copy(buffer, 0, mat.Data, len);
        return mat;
    }

    /// <summary>把单个 Mat 编码为独立字节数组。</summary>
    public static byte[] Encode(Mat mat)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, mat);
        }
        return ms.ToArray();
    }

    /// <summary>把字节数组解码为 Mat（空帧返回空 Mat）。</summary>
    public static Mat Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return Read(reader) ?? new Mat();
    }
}
