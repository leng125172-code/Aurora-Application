using Xunit;

namespace AuroraStruct3D.Calibration;

public class GrayCodePatternLayoutTests
{
    [Fact]
    public void BuildFrames_ShouldUseCurrentSixteenFrameLayout()
    {
        byte[][] frames = GrayCodePatternLayout.BuildFrames();

        Assert.Equal([6, 12, 24, 48], GrayCodePatternLayout.HorizontalStripeWidths);
        Assert.Equal([8, 16, 32, 64], GrayCodePatternLayout.VerticalStripeWidths);
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

        Assert.Equal("横条纹 6px 原图", GrayCodePatternLayout.GetFrameLabel(0));
        Assert.Equal("横条纹 48px 互补", GrayCodePatternLayout.GetFrameLabel(7));
        Assert.Equal("竖条纹 8px 原图", GrayCodePatternLayout.GetFrameLabel(8));
        Assert.Equal("竖条纹 64px 互补", GrayCodePatternLayout.GetFrameLabel(15));
    }
}
