using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class ScanFrameExposureAnalyzerTests
{
    [Fact]
    public void Analyze_should_report_clipped_ratios_and_percentiles()
    {
        using Mat image = new(1, 100, MatType.CV_8UC1, Scalar.All(100));
        for (int x = 0; x < 10; x++) image.Set(0, x, (byte)0);
        for (int x = 90; x < 100; x++) image.Set(0, x, (byte)255);
        Cv2.ImEncode(".bmp", image, out byte[] bytes);

        ScanFrameExposureMetrics result = ScanFrameExposureAnalyzer.Analyze(bytes);

        Assert.InRange(result.CrushedRatio, 0.099, 0.101);
        Assert.InRange(result.SaturatedRatio, 0.099, 0.101);
        Assert.Equal((byte)0, result.P01);
        Assert.Equal((byte)100, result.P50);
        Assert.Equal((byte)255, result.P99);
    }
}
