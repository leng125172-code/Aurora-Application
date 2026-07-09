using System.Globalization;
using System.Text;
using AuroraStruct3D.OpenCV.VisionParameters;

namespace AuroraStruct3D.OpenCV.File.PointCloud;

/// <summary>
/// 点云 ASCII PLY 写出辅助工具。
/// </summary>
public static class PointCloudPlyWriter
{
    /// <summary>
    /// 将点云对象编码为 ASCII PLY 字节数组。
    /// </summary>
    /// <param name="cloud">点云对象。</param>
    /// <returns>PLY 文件字节数组。</returns>
    public static byte[] WriteAscii(PointCloudData cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        Mat pointCloud =
            cloud.PointCloud
            ?? throw new InvalidOperationException("点云坐标矩阵为空，无法导出 PLY。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("点云为空，无法导出 PLY。");
        }

        if (pointCloud.Cols < 3)
        {
            throw new InvalidOperationException("点云列数不足 3，无法导出 XYZ 坐标。");
        }

        bool hasNormals = pointCloud.Cols >= 6;
        bool hasColors =
            cloud.HasColors
            && cloud.Colors is not null
            && !cloud.Colors.Empty()
            && cloud.Colors.Rows == pointCloud.Rows
            && cloud.Colors.Cols >= 3;

        using Mat pointCloud64 = new();
        pointCloud.ConvertTo(pointCloud64, MatType.CV_64FC1);

        StringBuilder sb = new();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {pointCloud.Rows}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        if (hasNormals)
        {
            sb.AppendLine("property float nx");
            sb.AppendLine("property float ny");
            sb.AppendLine("property float nz");
        }

        if (hasColors)
        {
            sb.AppendLine("property uchar red");
            sb.AppendLine("property uchar green");
            sb.AppendLine("property uchar blue");
        }

        sb.AppendLine("end_header");

        CultureInfo culture = CultureInfo.InvariantCulture;
        Mat? colors = hasColors ? cloud.Colors : null;
        int rows = pointCloud64.Rows;
        for (int i = 0; i < rows; i++)
        {
            AppendScalar(sb, pointCloud64.Get<double>(i, 0), culture);
            sb.Append(' ');
            AppendScalar(sb, pointCloud64.Get<double>(i, 1), culture);
            sb.Append(' ');
            AppendScalar(sb, pointCloud64.Get<double>(i, 2), culture);

            if (hasNormals)
            {
                sb.Append(' ');
                AppendScalar(sb, pointCloud64.Get<double>(i, 3), culture);
                sb.Append(' ');
                AppendScalar(sb, pointCloud64.Get<double>(i, 4), culture);
                sb.Append(' ');
                AppendScalar(sb, pointCloud64.Get<double>(i, 5), culture);
            }

            if (hasColors && colors is not null)
            {
                byte b = colors.Get<byte>(i, 0);
                byte g = colors.Get<byte>(i, 1);
                byte r = colors.Get<byte>(i, 2);
                sb.Append(' ');
                sb.Append(r.ToString(culture));
                sb.Append(' ');
                sb.Append(g.ToString(culture));
                sb.Append(' ');
                sb.Append(b.ToString(culture));
            }

            sb.AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendScalar(StringBuilder sb, double value, CultureInfo culture)
    {
        sb.Append(value.ToString("G6", culture));
    }
}
