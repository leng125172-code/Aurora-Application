using Xunit;

namespace AuroraStruct3D.Calibration;

public class GrayPhasePatternLayoutTests
{
    [Theory]
    [InlineData(10, 3, 4, 22)]
    [InlineData(10, 4, 4, 24)]
    [InlineData(16, 4, 4, 24)]
    public void Frame_count_should_include_gray_pairs_and_phase_frames(
        int periods, int phases, int expectedBits, int expectedTotal)
    {
        Assert.Equal(expectedBits, GrayPhasePatternLayout.GetGrayBitCount(periods));
        Assert.Equal(expectedTotal, GrayPhasePatternLayout.GetTotalFrameCount(periods, phases));
    }

    [Fact]
    public void Gray_pairs_should_be_complementary_and_phase_frames_should_be_grayscale()
    {
        byte[][] frames = GrayPhasePatternLayout.BuildFrames(128, 72, 10, 4, inverted: false);
        int grayFrames = GrayPhasePatternLayout.GetGrayBitCount(10) * 2;
        for (int frame = 0; frame < grayFrames; frame += 2)
        {
            Assert.Equal(frames[frame].Length, frames[frame + 1].Length);
            for (int i = 0; i < frames[frame].Length; i++)
                Assert.Equal(
                    GrayPhasePatternLayout.DarkLevel + GrayPhasePatternLayout.BrightLevel,
                    frames[frame][i] + frames[frame + 1][i]
                );
        }

        Assert.All(
            frames.SelectMany(frame => frame),
            value => Assert.InRange(
                value,
                GrayPhasePatternLayout.DarkLevel,
                GrayPhasePatternLayout.BrightLevel
            )
        );
        Assert.Contains(frames[grayFrames], value => value > 64 && value < 255);
    }

    [Fact]
    public void Inverted_layout_should_invert_every_pixel()
    {
        byte[][] normal = GrayPhasePatternLayout.BuildFrames(64, 32, 8, 4, inverted: false);
        byte[][] inverted = GrayPhasePatternLayout.BuildFrames(64, 32, 8, 4, inverted: true);
        Assert.Equal(normal.Length, inverted.Length);
        for (int frame = 0; frame < normal.Length; frame++)
            for (int i = 0; i < normal[frame].Length; i++)
                Assert.InRange(
                    normal[frame][i] + inverted[frame][i],
                    GrayPhasePatternLayout.DarkLevel + GrayPhasePatternLayout.BrightLevel - 1,
                    GrayPhasePatternLayout.DarkLevel + GrayPhasePatternLayout.BrightLevel + 1
                );
    }

    [Fact]
    public void Custom_gray_levels_should_bound_every_pattern_pixel()
    {
        byte[][] frames = GrayPhasePatternLayout.BuildFrames(
            64, 32, 8, 4, inverted: false, darkLevel: 32, brightLevel: 200);
        Assert.All(frames.SelectMany(x => x), value => Assert.InRange(value, (byte)32, (byte)200));
        Assert.Throws<ArgumentException>(() => GrayPhasePatternLayout.BuildFrames(
            64, 32, 8, 4, inverted: false, darkLevel: 200, brightLevel: 32));
    }
}
