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

    private static Mat CreateProjection(double fx, double cx, double tx)
    {
        Mat result = Mat.Zeros(3, 4, MatType.CV_64FC1).ToMat();
        result.Set(0, 0, fx);
        result.Set(1, 1, fx);
        result.Set(0, 2, cx);
        result.Set(1, 2, 100d);
        result.Set(0, 3, tx);
        result.Set(2, 2, 1d);
        return result;
    }
}
