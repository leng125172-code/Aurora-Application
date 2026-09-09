using System.Text;
using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

public static class StereoReconstructionUtils
{
    private const int DepthOutlierSampleLimit = 50_000;
    private const double DepthOutlierTailFraction = 0.005d;
    private const double DepthOutlierSupportRadiusMm = 3d;
    private const int DepthOutlierMinimumStandaloneComponentSize = 64;
    private const int DepthOutlierMaximumPixelSearchRadius = 64;
    private const int TriangulationBatchSize = 65_536;
    private const double MaximumTriangulationReprojectionErrorPixels = 0.5d;

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
        int disparitySign = 1,
        double[]? secondaryYCoordinates = null)
    {
        ArgumentNullException.ThrowIfNull(disparity);
        ArgumentNullException.ThrowIfNull(projectionP1);
        ArgumentNullException.ThrowIfNull(projectionP2);
        if (disparity.Empty() || disparity.Type() != MatType.CV_64FC1)
            throw new ArgumentException("Disparity must be a non-empty CV_64FC1 matrix.", nameof(disparity));
        if (projectionP1.Rows != 3 || projectionP1.Cols != 4
            || projectionP2.Rows != 3 || projectionP2.Cols != 4)
            throw new ArgumentException("Stereo projection matrices must both be 3x4.");
        if (!double.IsFinite(baselineMm) || baselineMm <= 0d)
            throw new ArgumentOutOfRangeException(nameof(baselineMm));
        if (disparitySign is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(disparitySign));
        if (secondaryYCoordinates is not null
            && secondaryYCoordinates.Length != checked(disparity.Rows * disparity.Cols))
            throw new ArgumentException(
                "Secondary Y coordinates must match the disparity matrix dimensions.",
                nameof(secondaryYCoordinates)
            );

        int rows = disparity.Rows;
        int cols = disparity.Cols;
        int pixelCount = checked(rows * cols);
        double[] disparityValues = new double[pixelCount];
        System.Runtime.InteropServices.Marshal.Copy(
            disparity.Data,
            disparityValues,
            0,
            disparityValues.Length
        );
        double[] depthValues = Enumerable.Repeat(double.NaN, pixelCount).ToArray();

        using Mat p1 = new();
        using Mat p2 = new();
        projectionP1.ConvertTo(p1, MatType.CV_64FC1);
        projectionP2.ConvertTo(p2, MatType.CV_64FC1);
        double[] p1Values = new double[12];
        double[] p2Values = new double[12];
        System.Runtime.InteropServices.Marshal.Copy(p1.Data, p1Values, 0, p1Values.Length);
        System.Runtime.InteropServices.Marshal.Copy(p2.Data, p2Values, 0, p2Values.Length);

        double p1Scale = p1Values[10];
        double p2Scale = p2Values[10];
        if (Math.Abs(p1Scale) < 1e-12 || Math.Abs(p2Scale) < 1e-12)
            throw new ArgumentException("Stereo projection matrices have an invalid homogeneous scale.");
        double principalPointDifference =
            p1Values[2] / p1Scale - p2Values[2] / p2Scale;
        double encodedBaseline = Math.Abs(
            -p2Values[3] / p2Values[0] + p1Values[3] / p1Values[0]
        );
        if (!double.IsFinite(encodedBaseline) || encodedBaseline <= 1e-12)
            throw new ArgumentException("Stereo projection matrices encode an invalid baseline.");
        double unitScale = Math.Abs(baselineMm) / encodedBaseline;

        int[] pixelOffsets = new int[TriangulationBatchSize];
        double[] leftPoints = new double[TriangulationBatchSize * 2];
        double[] rightPoints = new double[TriangulationBatchSize * 2];
        int batchCount = 0;
        for (int offset = 0; offset < pixelCount; offset++)
        {
            double disp = disparityValues[offset];
            double signedGeometricDisparity =
                (disp - principalPointDifference) * disparitySign;
            if (!double.IsFinite(disp) || signedGeometricDisparity <= 0.1d)
                continue;

            int y = offset / cols;
            int x = offset - y * cols;
            double secondaryY = secondaryYCoordinates?[offset] ?? y;
            if (!double.IsFinite(secondaryY))
                continue;
            pixelOffsets[batchCount] = offset;
            leftPoints[batchCount] = x;
            leftPoints[TriangulationBatchSize + batchCount] = y;
            rightPoints[batchCount] = x - disp;
            rightPoints[TriangulationBatchSize + batchCount] = secondaryY;
            batchCount++;

            if (batchCount == TriangulationBatchSize)
            {
                TriangulateBatch(batchCount);
                batchCount = 0;
            }
        }
        if (batchCount > 0)
            TriangulateBatch(batchCount);

        Mat depth = new(rows, cols, MatType.CV_64FC1);
        System.Runtime.InteropServices.Marshal.Copy(
            depthValues,
            0,
            depth.Data,
            depthValues.Length
        );
        return depth;

        void TriangulateBatch(int count)
        {
            // 工作数组按最大批次保存第二行；尾批次需移动到紧邻第一行的位置，
            // 以构成 OpenCV 期望的 2xN 单通道矩阵。
            if (count != TriangulationBatchSize)
            {
                Array.Copy(leftPoints, TriangulationBatchSize, leftPoints, count, count);
                Array.Copy(rightPoints, TriangulationBatchSize, rightPoints, count, count);
            }

            using Mat left = new(2, count, MatType.CV_64FC1);
            using Mat right = new(2, count, MatType.CV_64FC1);
            System.Runtime.InteropServices.Marshal.Copy(leftPoints, 0, left.Data, count * 2);
            System.Runtime.InteropServices.Marshal.Copy(rightPoints, 0, right.Data, count * 2);
            using Mat homogeneous = new();
            Cv2.TriangulatePoints(p1, p2, left, right, homogeneous);
            using Mat homogeneous64 = new();
            homogeneous.ConvertTo(homogeneous64, MatType.CV_64FC1);
            double[] coordinates = new double[checked(count * 4)];
            System.Runtime.InteropServices.Marshal.Copy(
                homogeneous64.Data,
                coordinates,
                0,
                coordinates.Length
            );

            for (int i = 0; i < count; i++)
            {
                double w = coordinates[3 * count + i];
                if (!double.IsFinite(w) || Math.Abs(w) < 1e-12)
                    continue;
                double worldX = coordinates[i] / w;
                double worldY = coordinates[count + i] / w;
                double worldZ = coordinates[2 * count + i] / w;
                if (!double.IsFinite(worldX)
                    || !double.IsFinite(worldY)
                    || !double.IsFinite(worldZ)
                    || worldZ <= 0d)
                    continue;

                if (!TryProject(p1Values, worldX, worldY, worldZ, out double leftX, out double leftY)
                    || !TryProject(p2Values, worldX, worldY, worldZ, out double rightX, out double rightY))
                    continue;
                double leftError = Math.Sqrt(
                    Math.Pow(leftX - leftPoints[i], 2d)
                        + Math.Pow(leftY - leftPoints[count + i], 2d)
                );
                double rightError = Math.Sqrt(
                    Math.Pow(rightX - rightPoints[i], 2d)
                        + Math.Pow(rightY - rightPoints[count + i], 2d)
                );
                if (Math.Max(leftError, rightError)
                    > MaximumTriangulationReprojectionErrorPixels)
                    continue;

                double scaledDepth = worldZ * unitScale;
                if (double.IsFinite(scaledDepth) && scaledDepth > 0d)
                    depthValues[pixelOffsets[i]] = scaledDepth;
            }
        }
    }

    private static bool TryProject(
        double[] projection,
        double x,
        double y,
        double z,
        out double imageX,
        out double imageY)
    {
        double denominator = projection[8] * x
            + projection[9] * y
            + projection[10] * z
            + projection[11];
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-12)
        {
            imageX = imageY = double.NaN;
            return false;
        }
        imageX = (
            projection[0] * x
                + projection[1] * y
                + projection[2] * z
                + projection[3]
        ) / denominator;
        imageY = (
            projection[4] * x
                + projection[5] * y
                + projection[6] * z
                + projection[7]
        ) / denominator;
        return double.IsFinite(imageX) && double.IsFinite(imageY);
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

    /// <summary>
    /// Removes only a statistically sparse far-depth tail. This targets spatially unsupported
    /// near-zero-disparity matches without imposing a scene-specific maximum depth.
    /// A tail larger than 0.5% is retained because it may be real scene geometry.
    /// </summary>
    public static DepthOutlierFilterResult RemoveSparseFarDepthOutliers(
        Mat depth,
        Mat projectionP1)
    {
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(projectionP1);
        if (depth.Empty() || depth.Type() != MatType.CV_64FC1)
            throw new ArgumentException("Depth must be a non-empty CV_64FC1 matrix.", nameof(depth));
        if (projectionP1.Rows != 3 || projectionP1.Cols != 4)
            throw new ArgumentException("Projection P1 must be a 3x4 matrix.", nameof(projectionP1));

        double fx = projectionP1.At<double>(0, 0);
        double fy = projectionP1.At<double>(1, 1);
        double cx = projectionP1.At<double>(0, 2);
        double cy = projectionP1.At<double>(1, 2);
        if (!double.IsFinite(fx) || !double.IsFinite(fy)
            || Math.Abs(fx) < 1e-12 || Math.Abs(fy) < 1e-12)
            throw new ArgumentException("Projection P1 contains an invalid focal length.", nameof(projectionP1));

        int rows = depth.Rows;
        int cols = depth.Cols;
        int validPointCount = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double value = depth.At<double>(y, x);
                if (double.IsFinite(value) && value > 0)
                    validPointCount++;
            }
        }

        if (validPointCount < 1_000)
            return new DepthOutlierFilterResult(validPointCount, 0, double.NaN);

        int sampleCount = Math.Min(validPointCount, DepthOutlierSampleLimit);
        double[] samples = new double[sampleCount];
        int validIndex = 0;
        int written = 0;
        for (int y = 0; y < rows && written < sampleCount; y++)
        {
            for (int x = 0; x < cols && written < sampleCount; x++)
            {
                double value = depth.At<double>(y, x);
                if (!double.IsFinite(value) || value <= 0)
                    continue;

                if ((long)validIndex * sampleCount >= (long)written * validPointCount)
                    samples[written++] = value;
                validIndex++;
            }
        }

        Array.Sort(samples);
        double firstQuartile = Percentile(samples, 0.25d);
        double thirdQuartile = Percentile(samples, 0.75d);
        double highPercentile = Percentile(samples, 1d - DepthOutlierTailFraction);
        double interquartileRange = Math.Max(0d, thirdQuartile - firstQuartile);
        // Tukey's 1.5 IQR fence finds the beginning of the abnormal tail. The 50 mm
        // floor and P99.5 bound below prevent clipping a naturally shallow depth range
        // or considering more than the sparsest 0.5% of valid depths.
        double upperFence = thirdQuartile + Math.Max(50d, interquartileRange * 1.5d);
        double cutoff = Math.Max(upperFence, highPercentile);
        // Use Ceiling here. Percentile rounding can leave ceil(N * fraction) values above
        // the selected percentile; Floor made the guard reject the entire filtering pass
        // by a single point (for example, 334 candidates out of 333,825 points).
        int maximumRemovalCount = Math.Max(
            1,
            (int)Math.Ceiling(validPointCount * DepthOutlierTailFraction)
        );
        List<int> outlierOffsets = new(Math.Min(maximumRemovalCount, 1_024));

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double value = depth.At<double>(y, x);
                if (!double.IsFinite(value) || value <= cutoff)
                    continue;

                outlierOffsets.Add(checked(y * cols + x));
                if (outlierOffsets.Count > maximumRemovalCount)
                    return new DepthOutlierFilterResult(validPointCount, 0, cutoff);
            }
        }

        double supportRadiusSquared = DepthOutlierSupportRadiusMm * DepthOutlierSupportRadiusMm;
        double maximumFocalLength = Math.Max(Math.Abs(fx), Math.Abs(fy));
        HashSet<int> outlierOffsetSet = new(outlierOffsets);
        HashSet<int> visitedOffsets = [];
        Queue<int> pendingOffsets = new();
        List<int> unsupportedOutliers = [];
        foreach (int seedOffset in outlierOffsets)
        {
            if (!visitedOffsets.Add(seedOffset))
                continue;

            List<int> componentOffsets = [];
            bool connectedToMainSurface = false;
            pendingOffsets.Enqueue(seedOffset);
            while (pendingOffsets.Count > 0)
            {
                int offset = pendingOffsets.Dequeue();
                componentOffsets.Add(offset);
                int y = offset / cols;
                int x = offset % cols;
                double z = depth.At<double>(y, x);
                double pointX = (x - cx) * z / fx;
                double pointY = (y - cy) * z / fy;
                int pixelSearchRadius = Math.Clamp(
                    (int)Math.Ceiling(DepthOutlierSupportRadiusMm * maximumFocalLength / z) + 1,
                    1,
                    DepthOutlierMaximumPixelSearchRadius
                );

                for (int dy = -pixelSearchRadius; dy <= pixelSearchRadius; dy++)
                for (int dx = -pixelSearchRadius; dx <= pixelSearchRadius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int neighborY = y + dy;
                    int neighborX = x + dx;
                    if (neighborY < 0 || neighborY >= rows || neighborX < 0 || neighborX >= cols)
                        continue;
                    double neighborZ = depth.At<double>(neighborY, neighborX);
                    if (!double.IsFinite(neighborZ) || neighborZ <= 0d)
                        continue;

                    double neighborPointX = (neighborX - cx) * neighborZ / fx;
                    double neighborPointY = (neighborY - cy) * neighborZ / fy;
                    double deltaX = neighborPointX - pointX;
                    double deltaY = neighborPointY - pointY;
                    double deltaZ = neighborZ - z;
                    if (deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ
                        > supportRadiusSquared)
                        continue;

                    int neighborOffset = checked(neighborY * cols + neighborX);
                    if (!outlierOffsetSet.Contains(neighborOffset))
                    {
                        connectedToMainSurface = true;
                    }
                    else if (visitedOffsets.Add(neighborOffset))
                    {
                        pendingOffsets.Enqueue(neighborOffset);
                    }
                }
            }

            // A real continuous surface is retained when it connects back to the main
            // depth distribution. A separate surface also survives when it contains
            // enough samples to be reliable. This removes tiny line/spot clusters whose
            // members previously kept one another alive after structured-light decoding.
            if (!connectedToMainSurface
                && componentOffsets.Count < DepthOutlierMinimumStandaloneComponentSize)
                unsupportedOutliers.AddRange(componentOffsets);
        }

        foreach (int offset in unsupportedOutliers)
            depth.Set(offset / cols, offset % cols, double.NaN);

        return new DepthOutlierFilterResult(validPointCount, unsupportedOutliers.Count, cutoff);

        static double Percentile(double[] sortedValues, double percentile)
        {
            int index = (int)Math.Round(
                (sortedValues.Length - 1) * percentile,
                MidpointRounding.AwayFromZero
            );
            return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
        }
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

public readonly record struct DepthOutlierFilterResult(
    int ValidPointCount,
    int RemovedPointCount,
    double DepthCutoffMm
)
{
    public bool Applied => RemovedPointCount > 0;
}
