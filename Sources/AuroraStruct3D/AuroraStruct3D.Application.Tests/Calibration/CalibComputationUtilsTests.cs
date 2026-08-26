using AuroraStruct3D.Calibration;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

/// <summary>
/// 标定纯计算工具的单元测试。
/// 仅覆盖无硬件 / 数据库 / OpenCV 原生调用依赖的确定性函数，可在 CI 上稳定运行。
/// </summary>
public class CalibComputationUtilsTests
{
    // ─── ComputeReprojectionError ───────────────────────────────────────────

    [Fact]
    public void ComputeReprojectionError_IdenticalPoints_ReturnsZero()
    {
        Point2f[] points = [new(0, 0), new(10, 20), new(-5, 7)];

        double error = CalibComputationUtils.ComputeReprojectionError(points, points);

        Assert.Equal(0d, error, 6);
    }

    [Fact]
    public void ComputeReprojectionError_ConstantOffset_ReturnsEuclideanDistance()
    {
        // 每个点偏移 (3,4) → 欧氏距离恒为 5，平均误差应为 5
        Point2f[] image = [new(0, 0), new(100, 100)];
        Point2f[] projected = [new(3, 4), new(103, 104)];

        double error = CalibComputationUtils.ComputeReprojectionError(image, projected);

        Assert.Equal(5d, error, 6);
    }

    [Fact]
    public void ComputeReprojectionError_EmptyArrays_ReturnsZero()
    {
        double error = CalibComputationUtils.ComputeReprojectionError([], []);

        Assert.Equal(0d, error, 6);
    }

    [Fact]
    public void ComputeReprojectionError_LengthMismatch_Throws()
    {
        Point2f[] image = [new(0, 0), new(1, 1)];
        Point2f[] projected = [new(0, 0)];

        Assert.Throws<ArgumentException>(
            () => CalibComputationUtils.ComputeReprojectionError(image, projected)
        );
    }

    // ─── ComputeReprojectionErrorStats ──────────────────────────────────────

    [Fact]
    public void ComputeReprojectionErrorStats_MixedDistances_ReturnsAvgMaxMin()
    {
        // 距离分别为 0 与 5 → avg=2.5, max=5, min=0
        Point2f[] image = [new(0, 0), new(0, 0)];
        Point2f[] projected = [new(0, 0), new(3, 4)];

        (double avg, double max, double min) =
            CalibComputationUtils.ComputeReprojectionErrorStats(image, projected);

        Assert.Equal(2.5d, avg, 6);
        Assert.Equal(5d, max, 6);
        Assert.Equal(0d, min, 6);
    }

    [Fact]
    public void ComputeReprojectionErrorStats_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CalibComputationUtils.ComputeReprojectionErrorStats([new(0, 0)], [])
        );
    }

    [Fact]
    public void TryValidateCameraModel_RejectsRunawayK3()
    {
        using Mat camera = Mat.Eye(3, 3, MatType.CV_64FC1);
        camera.Set(0, 0, 2000d);
        camera.Set(1, 1, 2000d);
        camera.Set(0, 2, 1024d);
        camera.Set(1, 2, 1224d);
        using Mat distortion = Mat.Zeros(5, 1, MatType.CV_64FC1).ToMat();
        distortion.Set(4, 0, -173d);

        bool valid = CalibComputationUtils.TryValidateCameraModel(
            camera, distortion, new Size(2048, 2448), out string reason
        );

        Assert.False(valid);
        Assert.Contains("d4", reason);
    }

    [Fact]
    public void ComputeRectificationMapCoverage_DetectsZeroSecondaryCoverage()
    {
        using Mat map1x = new(20, 20, MatType.CV_32FC1);
        using Mat map1y = new(20, 20, MatType.CV_32FC1);
        using Mat map2x = new(20, 20, MatType.CV_32FC1, Scalar.All(3086));
        using Mat map2y = new(20, 20, MatType.CV_32FC1, Scalar.All(0));
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            map1x.Set(y, x, (float)x);
            map1y.Set(y, x, (float)y);
        }

        var coverage = CalibComputationUtils.ComputeRectificationMapCoverage(
            map1x, map1y, map2x, map2y, 2048, 2448, sampleStep: 1
        );

        Assert.Equal(100d, coverage.MainPercent, 6);
        Assert.Equal(0d, coverage.SecondaryPercent, 6);
        Assert.Equal(0d, coverage.OverlapPercent, 6);
    }

    [Fact]
    public void ComputeRectificationMapCoverage_IdentityMapsHaveFullCoverage()
    {
        using Mat map1x = new(10, 10, MatType.CV_32FC1);
        using Mat map1y = new(10, 10, MatType.CV_32FC1);
        using Mat map2x = new(10, 10, MatType.CV_32FC1);
        using Mat map2y = new(10, 10, MatType.CV_32FC1);
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 10; x++)
        {
            map1x.Set(y, x, (float)x);
            map1y.Set(y, x, (float)y);
            map2x.Set(y, x, (float)x);
            map2y.Set(y, x, (float)y);
        }

        var coverage = CalibComputationUtils.ComputeRectificationMapCoverage(
            map1x, map1y, map2x, map2y, 20, 20, sampleStep: 1
        );

        Assert.Equal(100d, coverage.MainPercent, 6);
        Assert.Equal(100d, coverage.SecondaryPercent, 6);
        Assert.Equal(100d, coverage.OverlapPercent, 6);
    }

    [Fact]
    public void StereoRectifyMaximizeUsefulArea_ConvergingCamerasKeepCommonCoverage()
    {
        Size imageSize = new(2048, 2448);
        using Mat mainCamera = CreateMatrix(3, 3,
            3669.547241808708, 0, 982.9604995534988,
            0, 3666.094964190975, 1246.2956595011628,
            0, 0, 1);
        using Mat secondaryCamera = CreateMatrix(3, 3,
            3655.6036911052615, 0, 1045.070195545117,
            0, 3654.302755370544, 1251.9208688856393,
            0, 0, 1);
        using Mat mainDistortion = CreateMatrix(5, 1,
            -0.08256228791496271, 0.23847364820515607,
            -0.0010108787300751652, -0.00017304551273752906, 0);
        using Mat secondaryDistortion = CreateMatrix(5, 1,
            -0.07505449221291191, 0.22890059196148566,
            0.0011100787125713993, 0.0012936763150976816, 0);
        using Mat rotation = CreateMatrix(3, 3,
            0.846168211824049, -0.017574155584872363, 0.5326260473859405,
            0.01872485975187174, 0.9998194192781241, 0.0032416757436986186,
            -0.5325868351036891, 0.0072303450701366725, 0.8463444837561137);
        using Mat translation = CreateMatrix(3, 1,
            -164.61408003199193, 0.46696521948036934, 47.45219026333754);
        using Mat r1 = new(), r2 = new(), p1 = new(), p2 = new(), q = new();

        CalibComputationUtils.StereoRectifyMaximizeUsefulArea(
            mainCamera, mainDistortion, secondaryCamera, secondaryDistortion,
            imageSize, rotation, translation, r1, r2, p1, p2, q
        );

        using Mat map1x = new(), map1y = new(), map2x = new(), map2y = new();
        Cv2.InitUndistortRectifyMap(
            mainCamera, mainDistortion, r1, p1, imageSize,
            MatType.CV_32FC1, map1x, map1y
        );
        Cv2.InitUndistortRectifyMap(
            secondaryCamera, secondaryDistortion, r2, p2, imageSize,
            MatType.CV_32FC1, map2x, map2y
        );
        var coverage = CalibComputationUtils.ComputeRectificationMapCoverage(
            map1x, map1y, map2x, map2y, imageSize.Width, imageSize.Height
        );

        Assert.True(coverage.OverlapPercent > 95d, $"共同覆盖率仅 {coverage.OverlapPercent:F2}%");
        Assert.True(Math.Abs(p1.At<double>(0, 2) - p2.At<double>(0, 2)) > 1000d);
    }

    // ─── CreateProjectorPixelPoints ─────────────────────────────────────────

    [Fact]
    public void CreateProjectorPixelPoints_BuildsRowMajorGridWithOneCellMargin()
    {
        Size pattern = new(3, 2); // 3 列 2 行
        const int px = 10;

        Point2f[] pixels = CalibComputationUtils.CreateProjectorPixelPoints(pattern, px);

        Assert.Equal(6, pixels.Length);
        // 首点 (c=0,r=0) → ((0+1)*px, (0+1)*px)
        Assert.Equal(new Point2f(10, 10), pixels[0]);
        // 末点 (c=2,r=1) → ((2+1)*px, (1+1)*px)
        Assert.Equal(new Point2f(30, 20), pixels[5]);
    }

    // ─── ComputeProjectorSize ───────────────────────────────────────────────

    [Fact]
    public void ComputeProjectorSize_AddsTwoCellMarginOnEachAxis()
    {
        Size size = CalibComputationUtils.ComputeProjectorSize(new Size(3, 2), 10);

        Assert.Equal((3 + 2) * 10, size.Width);
        Assert.Equal((2 + 2) * 10, size.Height);
    }

    private static Mat CreateMatrix(int rows, int columns, params double[] values)
    {
        Assert.Equal(rows * columns, values.Length);
        Mat result = new(rows, columns, MatType.CV_64FC1);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
            result.Set(row, column, values[row * columns + column]);
        return result;
    }
}
