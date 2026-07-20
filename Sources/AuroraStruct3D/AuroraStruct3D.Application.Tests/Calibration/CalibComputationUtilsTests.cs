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
