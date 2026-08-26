using System.Text;
using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

public static class StereoReconstructionUtils
{
    public static (Mat rectifiedLeft, Mat rectifiedRight) RectifyImages(
        Mat leftImage,
        Mat rightImage,
        Mat map1x,
        Mat map1y,
        Mat map2x,
        Mat map2y)
    {
        Mat rectifiedLeft = new();
        Mat rectifiedRight = new();

        Cv2.Remap(leftImage, rectifiedLeft, map1x, map1y, InterpolationFlags.Linear);
        Cv2.Remap(rightImage, rectifiedRight, map2x, map2y, InterpolationFlags.Linear);

        return (rectifiedLeft, rectifiedRight);
    }

    public static Mat ComputeDisparity(
        Mat leftPhase,
        Mat rightPhase,
        int minDisparity = 0,
        int numDisparities = 64,
        int blockSize = 15)
    {
        if (leftPhase.Size() != rightPhase.Size())
            throw new ArgumentException("主从相机相位图尺寸不一致");

        numDisparities = Math.Max(16, numDisparities / 16 * 16);
        blockSize = Math.Max(3, blockSize | 1);

        int remainingWidth = leftPhase.Cols - (minDisparity + numDisparities);
        if (remainingWidth <= blockSize / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numDisparities),
                $"SGBM 视差窗口超出图像宽度：width={leftPhase.Cols}, "
                    + $"minDisparity={minDisparity}, numDisparities={numDisparities}, "
                    + $"blockSize={blockSize}。要求 width-(minDisparity+numDisparities)>{blockSize / 2}。"
            );
        }

        Cv2.MinMaxLoc(leftPhase, out double leftMin, out double leftMax);
        Cv2.MinMaxLoc(rightPhase, out double rightMin, out double rightMax);
        double minimum = Math.Min(leftMin, rightMin);
        double maximum = Math.Max(leftMax, rightMax);
        double scale = maximum - minimum > 1e-9 ? 255d / (maximum - minimum) : 1d;

        using Mat left8 = new();
        using Mat right8 = new();
        using Mat disparity16 = new();
        leftPhase.ConvertTo(left8, MatType.CV_8UC1, scale, -minimum * scale);
        rightPhase.ConvertTo(right8, MatType.CV_8UC1, scale, -minimum * scale);

        using StereoSGBM matcher = StereoSGBM.Create(
            minDisparity,
            numDisparities,
            blockSize,
            8 * blockSize * blockSize,
            32 * blockSize * blockSize,
            1,
            31,
            10,
            100,
            2,
            StereoSGBMMode.SGBM
        );
        matcher.Compute(left8, right8, disparity16);

        Mat disparity = new();
        disparity16.ConvertTo(disparity, MatType.CV_64FC1, 1d / 16d);
        return disparity;
    }

    public static Mat ComputeDepthFromDisparity(
        Mat disparity,
        Mat projectionP1,
        Mat projectionP2,
        double baselineMm = 100.0,
        int disparitySign = 1)
    {
        int rows = disparity.Rows;
        int cols = disparity.Cols;

        double fx = projectionP1.At<double>(0, 0);
        double principalPointDifference =
            projectionP1.At<double>(0, 2) - projectionP2.At<double>(0, 2);

        Mat depth = new(rows, cols, MatType.CV_64FC1);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double disp = disparity.At<double>(y, x);
                // 非 ZeroDisparity 整平会保留 cx1 != cx2。此时几何视差为
                // d - (cx1-cx2)，不能直接用图像坐标差 d 计算深度。
                double signedGeometricDisparity =
                    (disp - principalPointDifference) * disparitySign;
                if (signedGeometricDisparity > 0.1)
                {
                    double z = fx * Math.Abs(baselineMm) / signedGeometricDisparity;
                    depth.Set(y, x, z);
                }
                else
                {
                    depth.Set(y, x, double.NaN);
                }
            }
        }

        return depth;
    }

    /// <summary>
    /// 根据整平后的投影矩阵判断有效视差的符号。
    /// 标准 P2(0,3)=-fx*B 时右相机中心位于左相机右侧，视差为正；
    /// 若标定时主从顺序相反，则有效视差为负。
    /// </summary>
    public static int ComputeDisparitySign(Mat projectionP1, Mat projectionP2)
    {
        double fx1 = projectionP1.At<double>(0, 0);
        double fx2 = projectionP2.At<double>(0, 0);
        if (Math.Abs(fx1) < double.Epsilon || Math.Abs(fx2) < double.Epsilon)
        {
            throw new ArgumentException("双目投影矩阵焦距无效，无法判断视差方向。");
        }

        double centerX1 = -projectionP1.At<double>(0, 3) / fx1;
        double centerX2 = -projectionP2.At<double>(0, 3) / fx2;
        return centerX2 >= centerX1 ? 1 : -1;
    }

    public static (Mat pointCloud, Mat colors) GeneratePointCloud(
        Mat depth,
        Mat leftImage,
        Mat projectionP1,
        int sampleStep = 2,
        double maxDepth = 100000d)
    {
        if (sampleStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleStep));

        int rows = depth.Rows;
        int cols = depth.Cols;

        List<Point3f> points = new();
        List<Vec3b> colors = new();

        double fx = projectionP1.At<double>(0, 0);
        double fy = projectionP1.At<double>(1, 1);
        double cx = projectionP1.At<double>(0, 2);
        double cy = projectionP1.At<double>(1, 2);

        for (int y = 0; y < rows; y += sampleStep)
        {
            for (int x = 0; x < cols; x += sampleStep)
            {
                double z = depth.At<double>(y, x);
                if (!double.IsFinite(z) || z <= 0 || z > maxDepth)
                    continue;

                double px = (x - cx) * z / fx;
                double py = (y - cy) * z / fy;

                points.Add(new Point3f((float)px, (float)py, (float)z));

                Vec3b color = leftImage.At<Vec3b>(y, x);
                colors.Add(color);
            }
        }

        // 使用 N×3 单通道矩阵。此前使用 CV_32FC3/CV_8UC3 的同时又创建
        // 3 列，实际布局变成每个单元3通道，和 WritePly 的标量访问不一致。
        Mat pointCloudMat = new(points.Count, 3, MatType.CV_32FC1);
        Mat colorsMat = new(colors.Count, 3, MatType.CV_8UC1);

        for (int i = 0; i < points.Count; i++)
        {
            pointCloudMat.Set(i, 0, points[i].X);
            pointCloudMat.Set(i, 1, points[i].Y);
            pointCloudMat.Set(i, 2, points[i].Z);

            colorsMat.Set(i, 0, colors[i].Item0);
            colorsMat.Set(i, 1, colors[i].Item1);
            colorsMat.Set(i, 2, colors[i].Item2);
        }

        return (pointCloudMat, colorsMat);
    }

    public static byte[] WritePly(Mat pointCloud, Mat colors)
    {
        int pointCount = pointCloud.Rows;

        StringBuilder sb = new();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {pointCount}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");

        if (colors != null && !colors.Empty())
        {
            sb.AppendLine("property uchar red");
            sb.AppendLine("property uchar green");
            sb.AppendLine("property uchar blue");
        }

        sb.AppendLine("end_header");

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.At<float>(i, 0);
            float y = pointCloud.At<float>(i, 1);
            float z = pointCloud.At<float>(i, 2);

            sb.Append($"{x:F6} {y:F6} {z:F6}");

            if (colors != null && !colors.Empty())
            {
                byte r = colors.At<byte>(i, 2);
                byte g = colors.At<byte>(i, 1);
                byte b = colors.At<byte>(i, 0);

                sb.Append($" {r} {g} {b}");
            }

            sb.AppendLine();
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public static Mat ComputeBaselineFromStereoResult(
        Mat projectionP1,
        Mat projectionP2)
    {
        if (
            projectionP1.Rows != 3
            || projectionP1.Cols != 4
            || projectionP2.Rows != 3
            || projectionP2.Cols != 4
        )
        {
            throw new ArgumentException(
                $"双目投影矩阵必须为3×4，实际 P1={projectionP1.Rows}×{projectionP1.Cols}，"
                + $"P2={projectionP2.Rows}×{projectionP2.Cols}"
            );
        }

        double fx1 = projectionP1.At<double>(0, 0);
        double fx2 = projectionP2.At<double>(0, 0);
        if (Math.Abs(fx1) < 1e-12 || Math.Abs(fx2) < 1e-12)
        {
            throw new InvalidOperationException("双目投影矩阵焦距无效，无法计算基线");
        }

        // StereoRectify 输出的标准投影矩阵满足 P(0,3) = -fx * Cx。
        // 直接计算两个整平相机中心的 X 差值，避免不必要的矩阵求逆/Gemm。
        double centerX1 = -projectionP1.At<double>(0, 3) / fx1;
        double centerX2 = -projectionP2.At<double>(0, 3) / fx2;
        Mat baseline = Mat.Zeros(3, 1, MatType.CV_64FC1).ToMat();
        baseline.Set(0, 0, centerX2 - centerX1);
        return baseline;
    }

    public static double ComputeBaselineDistance(Mat baseline)
    {
        double dx = baseline.At<double>(0, 0);
        double dy = baseline.At<double>(1, 0);
        double dz = baseline.At<double>(2, 0);

        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
