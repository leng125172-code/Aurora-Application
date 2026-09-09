using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class GrayPhaseStructuredLightDecoderTests
{
    [Fact]
    public void Decode_should_recover_projector_coordinates_with_eight_phase_steps()
    {
        const int width = 32;
        const int height = 24;
        const int periodCount = 4;
        const int phaseCount = 8;
        byte[][] patterns = GrayPhasePatternLayout.BuildFrames(
            width, height, periodCount, phaseCount, inverted: false
        );
        int framesPerDirection = GrayPhasePatternLayout.GetFramesPerDirection(
            periodCount, phaseCount
        );
        List<byte[]> images = [];
        for (int frame = 0; frame < patterns.Length; frame++)
        {
            using Mat image = new(height, width, MatType.CV_8UC1);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte value = frame < framesPerDirection
                    ? patterns[frame][y]
                    : patterns[frame][x];
                image.Set(y, x, value);
            }
            Cv2.ImEncode(".bmp", image, out byte[] bytes);
            images.Add(bytes);
        }

        using Mat mapX = new(height, width, MatType.CV_32FC1);
        using Mat mapY = new(height, width, MatType.CV_32FC1);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            mapX.Set(y, x, (float)x);
            mapY.Set(y, x, (float)y);
        }

        GrayPhaseDecodeResult result = GrayPhaseStructuredLightDecoder.Decode(
            images,
            periodCount,
            phaseCount,
            width,
            height,
            inverted: false,
            mapX,
            mapY,
            CancellationToken.None
        );

        int index = 11 * width + 13;
        Assert.Equal(1, result.Valid[index]);
        Assert.Equal(1, result.ProjectorYValid[index]);
        Assert.InRange(result.ProjectorX[index], 12.9d, 13.1d);
        Assert.InRange(result.ProjectorY[index], 10.9d, 11.1d);
    }

    [Fact]
    public void Decode_should_use_phase_to_recover_one_low_contrast_gray_pixel()
    {
        const int width = 32;
        const int height = 24;
        const int periodCount = 4;
        const int phaseCount = 8;
        const int targetX = 13;
        const int targetY = 11;
        byte[][] patterns = GrayPhasePatternLayout.BuildFrames(
            width, height, periodCount, phaseCount, inverted: false
        );
        int framesPerDirection = GrayPhasePatternLayout.GetFramesPerDirection(
            periodCount, phaseCount
        );
        List<byte[]> images = [];
        for (int frame = 0; frame < patterns.Length; frame++)
        {
            using Mat image = new(height, width, MatType.CV_8UC1);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte value = frame < framesPerDirection
                    ? patterns[frame][y]
                    : patterns[frame][x];
                if ((frame == framesPerDirection || frame == framesPerDirection + 1)
                    && x == targetX && y == targetY)
                    value = 128;
                image.Set(y, x, value);
            }
            Cv2.ImEncode(".bmp", image, out byte[] bytes);
            images.Add(bytes);
        }

        using Mat mapX = new(height, width, MatType.CV_32FC1);
        using Mat mapY = new(height, width, MatType.CV_32FC1);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            mapX.Set(y, x, (float)x);
            mapY.Set(y, x, (float)y);
        }

        GrayPhaseDecodeResult result = GrayPhaseStructuredLightDecoder.Decode(
            images, periodCount, phaseCount, width, height, false,
            mapX, mapY, CancellationToken.None
        );

        int index = targetY * width + targetX;
        Assert.Equal(1, result.Valid[index]);
        Assert.InRange(result.ProjectorX[index], targetX - 0.1d, targetX + 0.1d);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics.GrayRecoveredPixels >= 1);
    }

    [Fact]
    public void Match_should_adapt_to_projector_sampling_scale()
    {
        const int width = 20;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];

        for (int x = 0; x < width; x++)
        {
            mainX[x] = x;
            mainY[x] = 10d;
            mainValid[x] = 1;
            secondaryX[x] = x + 3d;
            secondaryY[x] = 10d;
            secondaryValid[x] = 1;
        }

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, 1);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, 2
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main,
            secondary,
            disparitySign: 1,
            principalPointDifference: 0d,
            out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.True(diagnostics.MatchedPixels > 0);
        Assert.InRange(disparity.At<double>(0, 10), 2.99d, 3.01d);
    }

    [Fact]
    public void Match_should_recover_subpixel_secondary_y_from_both_projector_coordinates()
    {
        const int width = 20;
        const int height = 5;
        int pixels = width * height;
        double[] mainX = new double[pixels], mainY = new double[pixels];
        double[] secondaryX = new double[pixels], secondaryY = new double[pixels];
        byte[] mainValid = Enumerable.Repeat((byte)1, pixels).ToArray();
        byte[] secondaryValid = Enumerable.Repeat((byte)1, pixels).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            mainX[index] = x;
            mainY[index] = y;
            secondaryX[index] = x + 3d;
            secondaryY[index] = y - 0.5d;
        }
        GrayPhaseDecodeResult main = new(
            width, height, mainX, mainY, mainValid, mainValid, pixels
        );
        GrayPhaseDecodeResult secondary = new(
            width, height, secondaryX, secondaryY,
            secondaryValid, secondaryValid, pixels
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main, secondary, 1, 0d,
            out double[] secondaryYCoordinates,
            out GrayPhaseMatchDiagnostics diagnostics
        );

        int target = 2 * width + 10;
        Assert.True(diagnostics.MatchedPixels > 0);
        Assert.InRange(disparity.At<double>(2, 10), 2.99d, 3.01d);
        Assert.InRange(secondaryYCoordinates[target], 2.499d, 2.501d);
    }

    [Fact]
    public void Match_should_preserve_correspondence_without_projector_y_validation()
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
            main, secondary, 1, 0d, out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.Equal(1, diagnostics.MatchedPixels);
        Assert.Equal(0, diagnostics.ProjectorYRejected);
        Assert.InRange(disparity.At<double>(0, 5), 3.49d, 3.51d);
    }

    [Fact]
    public void Match_should_preserve_one_way_detail_when_reverse_match_is_missing()
    {
        const int width = 10;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];
        mainX[5] = 2.5d;
        mainY[5] = 10d;
        mainValid[5] = 1;
        secondaryX[1] = 0d;
        secondaryY[1] = 10d;
        secondaryValid[1] = 1;
        secondaryX[2] = 5d;
        secondaryY[2] = 10d;
        secondaryValid[2] = 1;

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, 1);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, 2
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main, secondary, 1, 0d, out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.Equal(1, diagnostics.MatchedPixels);
        Assert.Equal(0, diagnostics.BidirectionalRejected);
        Assert.InRange(disparity.At<double>(0, 5), 3.49d, 3.51d);
    }

    [Fact]
    public void Match_should_validate_geometric_disparity_when_principal_points_differ()
    {
        const int width = 20;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];

        for (int x = 0; x < width; x++)
        {
            mainX[x] = x;
            mainY[x] = 10d;
            mainValid[x] = 1;
            secondaryX[x] = x - 4d;
            secondaryY[x] = 10d;
            secondaryValid[x] = 1;
        }

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, 1);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, 2
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main,
            secondary,
            disparitySign: 1,
            principalPointDifference: -1584.499d,
            out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.True(diagnostics.MatchedPixels > 0);
        Assert.Equal(0, diagnostics.DisparityRejected);
        Assert.InRange(disparity.At<double>(0, 10), -4.01d, -3.99d);
    }

    [Fact]
    public void Match_should_apply_bidirectional_check_for_negative_disparity_direction()
    {
        const int width = 20;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];
        for (int x = 0; x < width; x++)
        {
            mainX[x] = x;
            mainY[x] = 10d;
            mainValid[x] = 1;
            secondaryX[x] = x - 4d;
            secondaryY[x] = 10d;
            secondaryValid[x] = 1;
        }

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, width);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, width
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main, secondary, -1, 0d, out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.True(diagnostics.MatchedPixels > 0);
        Assert.Equal(0, diagnostics.BidirectionalRejected);
        Assert.InRange(disparity.At<double>(0, 10), -4.01d, -3.99d);
    }

    [Fact]
    public void Match_should_reject_non_reciprocal_period_alias()
    {
        const int width = 24;
        double[] mainX = new double[width], mainY = new double[width];
        double[] secondaryX = new double[width], secondaryY = new double[width];
        byte[] mainValid = new byte[width], secondaryValid = new byte[width];
        for (int x = 0; x < width; x++)
        {
            mainX[x] = x;
            mainY[x] = 10d;
            mainValid[x] = 1;
            secondaryX[x] = x + 3d;
            secondaryY[x] = 10d;
            secondaryValid[x] = 1;
        }
        // 模拟一个竖条区域把投影周期号解到了前一个位置。
        mainX[12] = 9.1d;

        GrayPhaseDecodeResult main = new(width, 1, mainX, mainY, mainValid, mainValid, width);
        GrayPhaseDecodeResult secondary = new(
            width, 1, secondaryX, secondaryY, secondaryValid, secondaryValid, width
        );

        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            main, secondary, 1, 0d, out GrayPhaseMatchDiagnostics diagnostics
        );

        Assert.True(double.IsNaN(disparity.At<double>(0, 12)));
        Assert.True(diagnostics.BidirectionalRejected >= 1);
        Assert.InRange(disparity.At<double>(0, 11), 2.99d, 3.01d);
    }

    [Fact]
    public void Coordinate_hole_fill_should_restore_an_enclosed_single_pixel_hole()
    {
        const int width = 5;
        const int height = 5;
        double[] coordinates = new double[width * height];
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            coordinates[y * width + x] = x;
        valid[2 * width + 2] = 0;

        int filled = GrayPhaseStructuredLightDecoder.FillSmallCoordinateHoles(
            coordinates, valid, width, height
        );

        Assert.Equal(1, filled);
        Assert.Equal(1, valid[2 * width + 2]);
        Assert.Equal(2d, coordinates[2 * width + 2]);
    }

    [Fact]
    public void Coordinate_hole_fill_should_not_extend_an_open_invalid_region()
    {
        const int width = 5;
        const int height = 5;
        double[] coordinates = new double[width * height];
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            coordinates[y * width + x] = x;
        valid[2 * width + 2] = 0;
        valid[2 * width + 1] = 0;
        valid[1 * width + 2] = 0;

        int filled = GrayPhaseStructuredLightDecoder.FillSmallCoordinateHoles(
            coordinates, valid, width, height
        );

        Assert.Equal(0, filled);
        Assert.Equal(0, valid[2 * width + 2]);
    }

    [Fact]
    public void Coordinate_hole_fill_should_preserve_a_coordinate_discontinuity()
    {
        const int width = 5;
        const int height = 5;
        double[] coordinates = new double[width * height];
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            coordinates[y * width + x] = x < 2 ? 0d : 10d;
        valid[2 * width + 2] = 0;

        int filled = GrayPhaseStructuredLightDecoder.FillSmallCoordinateHoles(
            coordinates, valid, width, height
        );

        Assert.Equal(0, filled);
        Assert.Equal(0, valid[2 * width + 2]);
    }

    [Fact]
    public void Phase_guided_recovery_should_restore_an_isolated_gray_failure()
    {
        const int width = 5;
        const int height = 5;
        const int projectorSize = 40;
        const int periodCount = 4;
        double[] coordinates = new double[width * height];
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] grayValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] phaseValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        double[] phaseFractions = new double[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            coordinates[y * width + x] = 10d + x + y * 0.1d;

        int index = 2 * width + 2;
        valid[index] = 0;
        grayValid[index] = 0;
        phaseFractions[index] = 0.22d;

        int recovered = GrayPhaseStructuredLightDecoder.RecoverGrayInvalidCoordinates(
            coordinates, valid, grayValid, phaseFractions, phaseValid,
            width, height, projectorSize, periodCount
        );

        Assert.Equal(1, recovered);
        Assert.Equal(1, valid[index]);
        Assert.InRange(coordinates[index], 12.199d, 12.201d);
    }

    [Fact]
    public void Phase_guided_recovery_should_reject_an_inconsistent_phase_candidate()
    {
        const int width = 5;
        const int height = 5;
        double[] coordinates = Enumerable.Repeat(12d, width * height).ToArray();
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] grayValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] phaseValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        double[] phaseFractions = new double[width * height];
        int index = 2 * width + 2;
        valid[index] = 0;
        grayValid[index] = 0;
        phaseFractions[index] = 0.6d;

        int recovered = GrayPhaseStructuredLightDecoder.RecoverGrayInvalidCoordinates(
            coordinates, valid, grayValid, phaseFractions, phaseValid,
            width, height, projectorSize: 40, periodCount: 4
        );

        Assert.Equal(0, recovered);
        Assert.Equal(0, valid[index]);
    }

    [Fact]
    public void Phase_guided_recovery_should_not_cross_a_coordinate_discontinuity()
    {
        const int width = 5;
        const int height = 5;
        double[] coordinates = new double[width * height];
        byte[] valid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] grayValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        byte[] phaseValid = Enumerable.Repeat((byte)1, width * height).ToArray();
        double[] phaseFractions = new double[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            coordinates[y * width + x] = x < 2 ? 4d : 18d;
        int index = 2 * width + 2;
        valid[index] = 0;
        grayValid[index] = 0;
        phaseFractions[index] = 0.8d;

        int recovered = GrayPhaseStructuredLightDecoder.RecoverGrayInvalidCoordinates(
            coordinates, valid, grayValid, phaseFractions, phaseValid,
            width, height, projectorSize: 40, periodCount: 4
        );

        Assert.Equal(0, recovered);
        Assert.Equal(0, valid[index]);
    }

    [Fact]
    public void Phase_fit_should_preserve_phase_with_clipped_highlights()
    {
        const int phaseCount = 8;
        const double expectedPhase = 0.7d;
        float[][] samples = new float[phaseCount][];
        for (int frame = 0; frame < phaseCount; frame++)
        {
            double angle = 2d * Math.PI * frame / phaseCount;
            samples[frame] =
            [
                (float)Math.Min(255d, 190d + 100d * Math.Cos(angle + expectedPhase)),
            ];
        }

        bool fitted = GrayPhaseStructuredLightDecoder.TryFitPhase(
            samples,
            0,
            out double fittedCos,
            out double fittedSin,
            out double modulation,
            out double residual
        );
        double recoveredPhase = Math.Atan2(-fittedSin, fittedCos);
        if (recoveredPhase < 0d) recoveredPhase += 2d * Math.PI;

        Assert.True(fitted);
        Assert.InRange(recoveredPhase, expectedPhase - 0.02d, expectedPhase + 0.02d);
        Assert.True(modulation > 40d);
        Assert.True(double.IsFinite(residual));
    }

    [Fact]
    public void Phase_fit_should_not_create_periodic_bias_from_gamma_and_clipping()
    {
        const int phaseCount = 8;
        double maximumError = 0d;
        for (int step = 0; step < 360; step++)
        {
            double expectedPhase = 2d * Math.PI * step / 360d;
            float[][] samples = new float[phaseCount][];
            for (int frame = 0; frame < phaseCount; frame++)
            {
                double angle = 2d * Math.PI * frame / phaseCount;
                double projected = 24d + 196d
                    * (1d + Math.Cos(angle + expectedPhase)) / 2d;
                double observed = Math.Min(
                    255d,
                    255d * Math.Pow(projected / 255d, 0.5d) * 1.25d
                );
                samples[frame] = [(float)observed];
            }

            bool fitted = GrayPhaseStructuredLightDecoder.TryFitPhase(
                samples,
                0,
                out double fittedCos,
                out double fittedSin,
                out _,
                out _
            );
            Assert.True(fitted);
            double recoveredPhase = Math.Atan2(-fittedSin, fittedCos);
            double error = Math.Abs(
                Math.Atan2(
                    Math.Sin(recoveredPhase - expectedPhase),
                    Math.Cos(recoveredPhase - expectedPhase)
                )
            );
            maximumError = Math.Max(maximumError, error);
        }

        Assert.InRange(maximumError, 0d, 0.01d);
    }

    [Fact]
    public void Phase_fit_should_downweight_one_unsaturated_transient_outlier()
    {
        const int phaseCount = 8;
        const double expectedPhase = 1.1d;
        float[][] samples = new float[phaseCount][];
        for (int frame = 0; frame < phaseCount; frame++)
        {
            double angle = 2d * Math.PI * frame / phaseCount;
            samples[frame] = [(float)(110d + 55d * Math.Cos(angle + expectedPhase))];
        }
        samples[2][0] += 70f;

        bool fitted = GrayPhaseStructuredLightDecoder.TryFitPhase(
            samples,
            0,
            out double fittedCos,
            out double fittedSin,
            out double modulation,
            out double residual
        );
        double recoveredPhase = Math.Atan2(-fittedSin, fittedCos);
        if (recoveredPhase < 0d) recoveredPhase += 2d * Math.PI;

        Assert.True(fitted);
        Assert.InRange(recoveredPhase, expectedPhase - 0.035d, expectedPhase + 0.035d);
        Assert.InRange(modulation, 52d, 58d);
        Assert.InRange(residual, 0d, 12d);
    }

    [Fact]
    public void Phase_fit_should_reject_when_more_than_half_the_frames_are_saturated()
    {
        float[][] samples =
        [
            [255f], [255f], [255f], [255f], [255f], [80f], [100f], [120f],
        ];

        bool fitted = GrayPhaseStructuredLightDecoder.TryFitPhase(
            samples,
            0,
            out _,
            out _,
            out _,
            out _
        );

        Assert.False(fitted);
    }

    [Fact]
    public void Narrow_vertical_disparity_filter_should_remove_periodic_depth_sheet()
    {
        const int width = 25;
        const int height = 33;
        double[] disparities = Enumerable.Repeat(40d, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 10; x <= 12; x++)
            disparities[y * width + x] = 30d;

        long removed = GrayPhaseStructuredLightDecoder.RemoveNarrowVerticalDisparityOutliers(
            disparities, width, height, disparitySign: 1, principalPointDifference: 0d
        );

        Assert.Equal(99, removed);
        Assert.True(double.IsNaN(disparities[10]));
        Assert.True(double.IsNaN(disparities[width + 12]));
        Assert.Equal(40d, disparities[9]);
        Assert.Equal(40d, disparities[13]);
    }

    [Fact]
    public void Narrow_vertical_disparity_filter_should_preserve_short_far_detail()
    {
        const int width = 25;
        const int height = 3;
        double[] disparities = Enumerable.Repeat(40d, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 10; x <= 12; x++)
            disparities[y * width + x] = 30d;

        long removed = GrayPhaseStructuredLightDecoder.RemoveNarrowVerticalDisparityOutliers(
            disparities, width, height, disparitySign: 1, principalPointDifference: 0d
        );

        Assert.Equal(0, removed);
        Assert.Equal(30d, disparities[10]);
    }

    [Fact]
    public void Narrow_vertical_disparity_filter_should_preserve_near_detail()
    {
        const int width = 25;
        const int height = 9;
        double[] disparities = Enumerable.Repeat(40d, width * height).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 10; x <= 12; x++)
            disparities[y * width + x] = 50d;

        long removed = GrayPhaseStructuredLightDecoder.RemoveNarrowVerticalDisparityOutliers(
            disparities, width, height, disparitySign: 1, principalPointDifference: 0d
        );

        Assert.Equal(0, removed);
        Assert.Equal(50d, disparities[10]);
    }

    [Fact]
    public void Narrow_vertical_disparity_filter_should_preserve_real_depth_edge()
    {
        const int width = 25;
        double[] disparities = new double[width];
        for (int x = 0; x < width; x++)
            disparities[x] = x < 12 ? 40d : 30d;

        long removed = GrayPhaseStructuredLightDecoder.RemoveNarrowVerticalDisparityOutliers(
            disparities, width, 1, disparitySign: 1, principalPointDifference: 0d
        );

        Assert.Equal(0, removed);
        Assert.Equal(40d, disparities[11]);
        Assert.Equal(30d, disparities[12]);
    }
}
