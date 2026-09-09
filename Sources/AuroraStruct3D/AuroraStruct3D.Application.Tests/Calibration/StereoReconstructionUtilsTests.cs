using AuroraStruct3D.Calibration;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class StereoReconstructionUtilsTests
{
    [Fact]
    public void ComputeDepthFromDisparity_AccountsForDifferentRectifiedPrincipalPoints()
    {
        using Mat disparity = new(1, 1, MatType.CV_64FC1, Scalar.All(120d));
        using Mat p1 = CreateProjection(fx: 1000d, cx: 100d, tx: 0d);
        using Mat p2 = CreateProjection(fx: 1000d, cx: 80d, tx: -50_000d);

        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity, p1, p2, baselineMm: 50d, disparitySign: 1
        );

        // 几何视差 = 120 - (100-80) = 100 px，Z = 1000*50/100 = 500 mm。
        Assert.Equal(500d, depth.At<double>(0, 0), 6);
    }

    [Fact]
    public void ComputeDepthFromDisparity_UsesHomogeneousProjectionMatrices()
    {
        using Mat disparity = new(1, 1, MatType.CV_64FC1, Scalar.All(120d));
        using Mat p1 = CreateProjection(fx: 1000d, cx: 100d, tx: 0d);
        using Mat p2 = CreateProjection(fx: 1000d, cx: 80d, tx: -50_000d);
        p1.ConvertTo(p1, MatType.CV_64FC1, 3d);

        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity, p1, p2, baselineMm: 50d, disparitySign: 1
        );

        // 投影矩阵允许乘任意非零齐次尺度，三角化结果不应因此改变。
        Assert.Equal(500d, depth.At<double>(0, 0), 6);
    }

    [Fact]
    public void ComputeDepthFromDisparity_RejectsWrongDisparityDirection()
    {
        using Mat disparity = new(1, 2, MatType.CV_64FC1);
        disparity.Set(0, 0, -10d);
        disparity.Set(0, 1, double.NaN);
        using Mat p1 = CreateProjection(fx: 1000d, cx: 100d, tx: 0d);
        using Mat p2 = CreateProjection(fx: 1000d, cx: 80d, tx: -50_000d);

        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity, p1, p2, baselineMm: 50d, disparitySign: 1
        );

        Assert.True(double.IsNaN(depth.At<double>(0, 0)));
        Assert.True(double.IsNaN(depth.At<double>(0, 1)));
    }

    [Fact]
    public void ComputeDepthFromDisparity_PreservesTailAcrossTriangulationBatches()
    {
        const int columns = 65_537;
        using Mat disparity = new(1, columns, MatType.CV_64FC1, Scalar.All(120d));
        using Mat p1 = CreateProjection(fx: 1000d, cx: 100d, tx: 0d);
        using Mat p2 = CreateProjection(fx: 1000d, cx: 80d, tx: -50_000d);

        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity, p1, p2, baselineMm: 50d, disparitySign: 1
        );

        Assert.Equal(500d, depth.At<double>(0, 0), 6);
        Assert.Equal(500d, depth.At<double>(0, 65_535), 6);
        Assert.Equal(500d, depth.At<double>(0, 65_536), 6);
    }

    [Fact]
    public void ComputeDepthFromDisparity_UsesSecondarySubpixelYForFullProjectionGeometry()
    {
        const int rows = 4;
        const int columns = 21;
        using Mat disparity = new(rows, columns, MatType.CV_64FC1, Scalar.All(double.NaN));
        disparity.Set(1, 20, 10d);
        double[] secondaryY = Enumerable.Repeat(double.NaN, rows * columns).ToArray();
        secondaryY[1 * columns + 20] = 3d;
        using Mat p1 = CreateProjection(fx: 100d, cx: 0d, tx: 0d, cy: 0d);
        using Mat p2 = CreateProjection(fx: 100d, cx: 0d, tx: -1_000d, cy: 2d);

        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity,
            p1,
            p2,
            baselineMm: 10d,
            disparitySign: 1,
            secondaryYCoordinates: secondaryY
        );

        Assert.Equal(100d, depth.At<double>(1, 20), 6);
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_RemovesTinyDistantTail()
    {
        using Mat depth = new(100, 100, MatType.CV_64FC1);
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 50d, tx: 0d, cy: 50d);
        const int rows = 100;
        const int cols = 100;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
                depth.Set(y, x, 350d + (x % 10));
        }
        depth.Set(0, 0, 800d);
        depth.Set(10, 20, 900d);
        depth.Set(30, 40, 1_200d);
        depth.Set(50, 60, 1_900d);

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.True(result.Applied);
        Assert.Equal(4, result.RemovedPointCount);
        Assert.True(double.IsNaN(depth.At<double>(0, 0)));
        Assert.True(double.IsNaN(depth.At<double>(50, 60)));
        Assert.Equal(355d, depth.At<double>(55, 55));
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_RemovesScatteredSubPercentTail()
    {
        const int rows = 100;
        const int cols = 100;
        using Mat depth = new(rows, cols, MatType.CV_64FC1);
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 50d, tx: 0d, cy: 50d);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
                depth.Set(y, x, 350d + (x % 10));
        }

        const int outlierCount = 40;
        for (int i = 0; i < outlierCount; i++)
        {
            int y = i / 10 * 20;
            int x = i % 10 * 10;
            depth.Set(y, x, 800d + i);
        }

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.True(result.Applied);
        Assert.Equal(outlierCount, result.RemovedPointCount);
        for (int i = 0; i < outlierCount; i++)
        {
            int y = i / 10 * 20;
            int x = i % 10 * 10;
            Assert.True(double.IsNaN(depth.At<double>(y, x)));
        }
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_PreservesNonSparseDistantSurface()
    {
        using Mat depth = new(100, 100, MatType.CV_64FC1);
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 50d, tx: 0d, cy: 50d);
        const int rows = 100;
        const int cols = 100;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
                depth.Set(y, x, y < 2 ? 1_000d : 350d + (x % 10));
        }

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.False(result.Applied);
        Assert.Equal(0, result.RemovedPointCount);
        Assert.Equal(1_000d, depth.At<double>(0, 0));
        Assert.Equal(1_000d, depth.At<double>(1, 99));
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_RemovesSmallDisconnectedDistantCluster()
    {
        using Mat depth = new(200, 200, MatType.CV_64FC1, Scalar.All(350d));
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 100d, tx: 0d, cy: 100d);
        depth.Set(80, 90, 1_000d);
        depth.Set(80, 91, 1_000d);
        depth.Set(81, 90, 1_000d);
        depth.Set(81, 91, 1_000d);

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.True(result.Applied);
        Assert.Equal(4, result.RemovedPointCount);
        Assert.True(double.IsNaN(depth.At<double>(80, 90)));
        Assert.True(double.IsNaN(depth.At<double>(81, 91)));
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_PreservesTailConnectedToMainSurface()
    {
        using Mat depth = new(200, 200, MatType.CV_64FC1, Scalar.All(350d));
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 100d, tx: 0d, cy: 100d);
        for (int x = 90; x <= 110; x++)
            depth.Set(100, x, 400d + (x - 90));

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.False(result.Applied);
        Assert.Equal(0, result.RemovedPointCount);
        Assert.Equal(420d, depth.At<double>(100, 110));
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_PreservesReliableStandaloneSurface()
    {
        using Mat depth = new(200, 200, MatType.CV_64FC1, Scalar.All(350d));
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 100d, tx: 0d, cy: 100d);
        for (int y = 40; y < 48; y++)
        {
            for (int x = 40; x < 48; x++)
                depth.Set(y, x, 1_000d);
        }

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.False(result.Applied);
        Assert.Equal(0, result.RemovedPointCount);
        Assert.Equal(1_000d, depth.At<double>(40, 40));
        Assert.Equal(1_000d, depth.At<double>(47, 47));
    }

    [Fact]
    public void RemoveSparseFarDepthOutliers_RemovesAdjacentPixelsWithoutThreeDimensionalSupport()
    {
        using Mat depth = new(100, 100, MatType.CV_64FC1, Scalar.All(350d));
        using Mat projectionP1 = CreateProjection(fx: 1_000d, cx: 50d, tx: 0d, cy: 50d);
        depth.Set(50, 50, 900d);
        depth.Set(50, 51, 1_200d);

        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);

        Assert.True(result.Applied);
        Assert.Equal(2, result.RemovedPointCount);
        Assert.True(double.IsNaN(depth.At<double>(50, 50)));
        Assert.True(double.IsNaN(depth.At<double>(50, 51)));
    }

    private static Mat CreateProjection(double fx, double cx, double tx, double cy = 100d)
    {
        Mat result = Mat.Zeros(3, 4, MatType.CV_64FC1).ToMat();
        result.Set(0, 0, fx);
        result.Set(1, 1, fx);
        result.Set(0, 2, cx);
        result.Set(1, 2, cy);
        result.Set(0, 3, tx);
        result.Set(2, 2, 1d);
        return result;
    }
}
