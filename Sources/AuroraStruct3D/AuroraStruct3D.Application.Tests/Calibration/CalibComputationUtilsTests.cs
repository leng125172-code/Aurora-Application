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
}
