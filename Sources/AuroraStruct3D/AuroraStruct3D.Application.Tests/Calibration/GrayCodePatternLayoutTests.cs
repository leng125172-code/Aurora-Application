using Xunit;

namespace AuroraStruct3D.Calibration;

public class GrayCodePatternLayoutTests
{
    [Fact]
    public void BuildFrames_ShouldUseLegacyComplementaryLayout()
    {
        byte[][] frames = GrayCodePatternLayout.BuildFrames();

        Assert.Equal(8, GrayCodePatternLayout.HorizontalFrameCount);
        Assert.Equal(8, GrayCodePatternLayout.VerticalFrameCount);
        Assert.Equal(16, GrayCodePatternLayout.TotalFrameCount);
        Assert.Equal(16, frames.Length);

        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex += 2)
        {
            int expectedLength =
                frameIndex < GrayCodePatternLayout.HorizontalFrameCount
                    ? GrayCodePatternLayout.ProjectorHeight
                    : GrayCodePatternLayout.ProjectorWidth;
            Assert.Equal(expectedLength, frames[frameIndex].Length);
            Assert.Equal(expectedLength, frames[frameIndex + 1].Length);
            for (int pixelIndex = 0; pixelIndex < expectedLength; pixelIndex++)
            {
                Assert.Equal(
                    byte.MaxValue,
                    frames[frameIndex][pixelIndex] + frames[frameIndex + 1][pixelIndex]
                );
            }
        }
    }
}
