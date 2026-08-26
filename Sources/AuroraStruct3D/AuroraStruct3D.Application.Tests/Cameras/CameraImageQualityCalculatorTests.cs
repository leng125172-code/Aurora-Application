using AuroraStruct3D.Cameras;
using AuroraStruct3D.Calibration;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Cameras;

public class CameraImageQualityCalculatorTests
{
    [Fact]
    public void UniformMidGray_HasNoUsableTargetContrast()
    {
        byte[] pixels = Enumerable.Repeat((byte)115, 64 * 64).ToArray();

        CameraImageQualityCalculator.Scores scores =
            CameraImageQualityCalculator.ComputeGray(pixels, 64, 64);

        Assert.InRange(scores.ExposureScore, 1, 30);
        Assert.Equal(1, scores.SharpnessScore);
    }

    [Fact]
    public void ClippedImage_HasLowExposureScore()
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 64 * 64).ToArray();

        CameraImageQualityCalculator.Scores scores =
            CameraImageQualityCalculator.ComputeGray(pixels, 64, 64);

        Assert.InRange(scores.ExposureScore, 1, 30);
        Assert.InRange(scores.SharpnessScore, 1, 100);
    }

    [Fact]
    public void WhiteBackgroundWithBlackDots_IsWellExposed()
    {
        using Mat target = CreateDotTarget(background: 235, dot: 25);
        byte[] pixels = CalibImageUtils.CopyGrayPixels(target);

        CameraImageQualityCalculator.Scores scores =
            CameraImageQualityCalculator.ComputeGray(pixels, target.Cols, target.Rows);

        Assert.InRange(scores.ExposureScore, 85, 100);
        Assert.InRange(scores.SharpnessScore, 60, 100);
    }

    [Fact]
    public void SaturatedWhiteBackground_IsPenalizedWithoutTreatingWhiteAsInvalid()
    {
        using Mat normal = CreateDotTarget(background: 235, dot: 25);
        using Mat saturated = CreateDotTarget(background: 255, dot: 25);

        CameraImageQualityCalculator.Scores normalScores =
            CameraImageQualityCalculator.ComputeGray(
                CalibImageUtils.CopyGrayPixels(normal),
                normal.Cols,
                normal.Rows
            );
        CameraImageQualityCalculator.Scores saturatedScores =
            CameraImageQualityCalculator.ComputeGray(
                CalibImageUtils.CopyGrayPixels(saturated),
                saturated.Cols,
                saturated.Rows
            );

        Assert.InRange(saturatedScores.ExposureScore, 55, 75);
        Assert.True(saturatedScores.ExposureScore < normalScores.ExposureScore);
    }

    [Fact]
    public void DimLowContrastTarget_HasLowExposureScore()
    {
        using Mat target = CreateDotTarget(background: 140, dot: 100);

        CameraImageQualityCalculator.Scores scores =
            CameraImageQualityCalculator.ComputeGray(
                CalibImageUtils.CopyGrayPixels(target),
                target.Cols,
                target.Rows
            );

        Assert.InRange(scores.ExposureScore, 1, 30);
    }

    [Fact]
    public void BlurredDots_AreLessSharpThanCrispDots()
    {
        using Mat crisp = CreateDotTarget(background: 235, dot: 25);
        using var blurred = new Mat();
        Cv2.GaussianBlur(crisp, blurred, new Size(15, 15), 4);

        CameraImageQualityCalculator.Scores crispScores =
            CameraImageQualityCalculator.ComputeGray(
                CalibImageUtils.CopyGrayPixels(crisp),
                crisp.Cols,
                crisp.Rows
            );
        CameraImageQualityCalculator.Scores blurredScores =
            CameraImageQualityCalculator.ComputeGray(
                CalibImageUtils.CopyGrayPixels(blurred),
                blurred.Cols,
                blurred.Rows
            );

        Assert.True(crispScores.SharpnessScore > blurredScores.SharpnessScore);
    }

    [Fact]
    public void OpenCvMat_IsCopiedAsRawGrayPixels()
    {
        using var gray = new Mat(48, 64, MatType.CV_8UC1, Scalar.All(115));

        byte[] pixels = CalibImageUtils.CopyGrayPixels(gray);
        CameraImageQualityCalculator.Scores scores =
            CameraImageQualityCalculator.ComputeGray(pixels, gray.Cols, gray.Rows);

        Assert.Equal(48 * 64, pixels.Length);
        Assert.All(pixels, value => Assert.Equal((byte)115, value));
        Assert.InRange(scores.ExposureScore, 1, 30);
    }

    [Fact]
    public void NonContinuousOpenCvRoi_IsCopiedWithoutRowPadding()
    {
        using var source = new Mat(48, 80, MatType.CV_8UC1, Scalar.All(115));
        using Mat roi = source[new Rect(8, 4, 64, 32)];

        byte[] pixels = CalibImageUtils.CopyGrayPixels(roi);

        Assert.False(roi.IsContinuous());
        Assert.Equal(32 * 64, pixels.Length);
        Assert.All(pixels, value => Assert.Equal((byte)115, value));
    }

    private static Mat CreateDotTarget(byte background, byte dot)
    {
        var target = new Mat(128, 128, MatType.CV_8UC1, Scalar.All(background));
        for (int y = 8; y < 128; y += 12)
        for (int x = 8; x < 128; x += 12)
            Cv2.Circle(target, new Point(x, y), 3, Scalar.All(dot), -1);
        return target;
    }
}
