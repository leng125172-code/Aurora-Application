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
        int rows = leftPhase.Rows;
        int cols = leftPhase.Cols;

        Mat disparity = new(rows, cols, MatType.CV_64FC1);

        numDisparities = numDisparities / 16 * 16;
        if (numDisparities <= 0)
            numDisparities = 64;

        for (int y = blockSize / 2; y < rows - blockSize / 2; y++)
        {
            for (int x = blockSize / 2; x < cols - blockSize / 2; x++)
            {
                double minCost = double.MaxValue;
                int bestDisp = 0;

                int searchEnd = Math.Min(x - minDisparity, numDisparities);
                for (int d = 0; d <= searchEnd; d++)
                {
                    int rightX = x - d;
                    if (rightX < blockSize / 2)
                        break;

                    double cost = 0;
                    int count = 0;

                    for (int by = -blockSize / 2; by <= blockSize / 2; by++)
                    {
                        for (int bx = -blockSize / 2; bx <= blockSize / 2; bx++)
                        {
                            double lVal = leftPhase.At<double>(y + by, x + bx);
                            double rVal = rightPhase.At<double>(y + by, rightX + bx);

                            double diff = Math.Abs(lVal - rVal);
                            if (diff > Math.PI)
                            {
                                diff = Math.Abs(diff - 2 * Math.PI);
                            }

                            cost += diff;
                            count++;
                        }
                    }

                    cost /= count;

                    if (cost < minCost)
                    {
                        minCost = cost;
                        bestDisp = d;
                    }
                }

                disparity.Set(y, x, bestDisp);
            }
        }

        return disparity;
    }

    public static Mat ComputeDepthFromDisparity(
        Mat disparity,
        Mat projectionP1,
        Mat projectionP2,
        double baselineMm = 100.0)
    {
        int rows = disparity.Rows;
        int cols = disparity.Cols;

        double fx = projectionP1.At<double>(0, 0);

        Mat depth = new(rows, cols, MatType.CV_64FC1);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double disp = disparity.At<double>(y, x);
                if (disp > 0.1)
                {
                    double z = fx * baselineMm / disp;
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

    public static (Mat pointCloud, Mat colors) GeneratePointCloud(
        Mat depth,
        Mat leftImage,
        Mat projectionP1)
    {
        int rows = depth.Rows;
        int cols = depth.Cols;

        List<Point3f> points = new();
        List<Vec3b> colors = new();

        double fx = projectionP1.At<double>(0, 0);
        double fy = projectionP1.At<double>(1, 1);
        double cx = projectionP1.At<double>(0, 2);
        double cy = projectionP1.At<double>(1, 2);

        for (int y = 0; y < rows; y += 2)
        {
            for (int x = 0; x < cols; x += 2)
            {
                double z = depth.At<double>(y, x);
                if (double.IsNaN(z) || z <= 0)
                    continue;

                double px = (x - cx) * z / fx;
                double py = (y - cy) * z / fy;

                points.Add(new Point3f((float)px, (float)py, (float)z));

                Vec3b color = leftImage.At<Vec3b>(y, x);
                colors.Add(color);
            }
        }

        Mat pointCloudMat = new(points.Count, 3, MatType.CV_32FC3);
        Mat colorsMat = new(colors.Count, 3, MatType.CV_8UC3);

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
        Mat baseline = new(3, 1, MatType.CV_64FC1);

        Mat p1 = projectionP1[new Rect(0, 0, 3, 3)];
        Mat p2 = projectionP2[new Rect(0, 0, 3, 3)];

        Mat p1Inv = p1.Inv();
        Mat p2Inv = p2.Inv();

        Mat t1 = new();
        Mat t2 = new();
        Cv2.Gemm(p1Inv, projectionP1[new Rect(3, 0, 1, 3)].T(), 1.0, null, 0.0, t1);
        Cv2.Gemm(p2Inv, projectionP2[new Rect(3, 0, 1, 3)].T(), 1.0, null, 0.0, t2);

        Cv2.Subtract(t2, t1, baseline);

        p1.Dispose();
        p2.Dispose();
        p1Inv.Dispose();
        p2Inv.Dispose();
        t1.Dispose();
        t2.Dispose();

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