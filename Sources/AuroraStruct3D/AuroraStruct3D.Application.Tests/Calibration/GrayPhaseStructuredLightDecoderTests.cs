using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class GrayPhaseStructuredLightDecoderTests
{
    [Fact]
    public void Match_should_adapt_to_projector_sampling_scale()
    {
        const int width = 10;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];

        mainX[5] = 2.5d;
        mainY[5] = 10d;
        mainValid[5] = 1;
        secondaryX[1] = 0d;
        secondaryY[1] = 7d;
        secondaryValid[1] = 1;
        secondaryX[2] = 5d;
        secondaryY[2] = 7d;
        secondaryValid[2] = 1;

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, 1);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, 2
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main,
            secondary,
            disparitySign: 1,
            out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.Equal(1, diagnostics.MatchedPixels);
        Assert.InRange(disparity.At<double>(0, 5), 3.49d, 3.51d);
    }

    [Fact]
    public void Match_should_reject_correspondence_without_projector_y_validation()
    {
        const int width = 10;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];
        byte[] noY = new byte[width];
        mainX[5] = 2.5d; mainValid[5] = 1;
        secondaryX[1] = 0d; secondaryValid[1] = 1;
        secondaryX[2] = 5d; secondaryValid[2] = 1;
        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, noY, 1);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, noY, 2
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main, secondary, 1, out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.Equal(0, diagnostics.MatchedPixels);
        Assert.Equal(1, diagnostics.ProjectorYRejected);
        Assert.True(double.IsNaN(disparity.At<double>(0, 5)));
    }
}
