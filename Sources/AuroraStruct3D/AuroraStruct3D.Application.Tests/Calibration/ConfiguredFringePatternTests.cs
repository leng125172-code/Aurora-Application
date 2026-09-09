using System.Reflection;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class ConfiguredFringePatternTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void BuildFringeImagePixels_ShouldUseConfiguredCountForBothDirections(int imageCount)
    {
        DownloadFringePatternInputDto input = CreateInput(imageCount);

        byte[][] frames = InvokeBuild(input);

        int framesPerDirection = GrayPhasePatternLayout.GetFramesPerDirection(
            input.PeriodCount,
            imageCount
        );
        Assert.Equal(framesPerDirection * 2, frames.Length);
        Assert.All(
            frames.Take(framesPerDirection),
            frame => Assert.Equal(720, frame.Length)
        );
        Assert.All(
            frames.Skip(framesPerDirection),
            frame => Assert.Equal(1280, frame.Length)
        );
    }

    [Fact]
    public void BuildFringeImagePixels_ShouldGenerateUniformPhaseStepsAfterGrayFrames()
    {
        DownloadFringePatternInputDto input = CreateInput(imageCount: 4);

        byte[][] frames = InvokeBuild(input);

        int grayFrameCount = GrayPhasePatternLayout.GetGrayBitCount(input.PeriodCount) * 2;
        int framesPerDirection = GrayPhasePatternLayout.GetFramesPerDirection(
            input.PeriodCount,
            input.ImageCount
        );
        byte expectedCenter = (byte)((input.DarkLevel + input.BrightLevel) / 2);

        Assert.Equal(input.BrightLevel, frames[grayFrameCount][0]);
        Assert.Equal(expectedCenter, frames[grayFrameCount + 1][0]);
        Assert.Equal(input.DarkLevel, frames[grayFrameCount + 2][0]);
        Assert.Equal(expectedCenter, frames[grayFrameCount + 3][0]);

        int verticalPhaseOffset = framesPerDirection + grayFrameCount;
        Assert.Equal(input.BrightLevel, frames[verticalPhaseOffset][0]);
        Assert.Equal(expectedCenter, frames[verticalPhaseOffset + 1][0]);
        Assert.Equal(input.DarkLevel, frames[verticalPhaseOffset + 2][0]);
        Assert.Equal(expectedCenter, frames[verticalPhaseOffset + 3][0]);
    }

    [Fact]
    public void BuildFringeOrientationCommand_ShouldSplit48FramesIntoTwoBlocks()
    {
        Assert.Equal("MF 0 255 255 255 0\r\n", InvokeBuildMf(48, 24, 0));
        Assert.Equal("MF 1 0 0 0 0\r\n", InvokeBuildMf(48, 24, 1));
    }

    [Fact]
    public void BuildFringeOrientationCommand_ShouldHandle32And64FrameBoundaries()
    {
        Assert.Equal("MF 0 255 255 0 0\r\n", InvokeBuildMf(32, 16, 0));
        Assert.Equal("MF 0 255 255 255 255\r\n", InvokeBuildMf(64, 32, 0));
        Assert.Equal("MF 1 0 0 0 0\r\n", InvokeBuildMf(64, 32, 1));
    }

    private static DownloadFringePatternInputDto CreateInput(int imageCount) => new()
    {
        ProjectorId = Guid.NewGuid(),
        FringeMode = "horizontal",
        FringeType = "bw",
        WidthPixels = 1280,
        HeightPixels = 720,
        PeriodCount = 8,
        ImageCount = imageCount,
        PhaseShift = 2,
    };

    private static byte[][] InvokeBuild(DownloadFringePatternInputDto input)
    {
        MethodInfo method = typeof(ProjectorDeviceAppService).GetMethod(
            "BuildFringeImagePixels",
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException("未找到条纹生成方法");
        return (byte[][])(method.Invoke(null, [input])
            ?? throw new InvalidOperationException("条纹生成结果为空"));
    }

    private static string InvokeBuildMf(
        int imageCount,
        int horizontalFrameCount,
        int blockIndex)
    {
        MethodInfo method = typeof(DlpProjectorService).GetMethod(
            "BuildFringeOrientationCommand",
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException("未找到 MF 方向位图生成方法");
        return (string)(method.Invoke(null, [imageCount, horizontalFrameCount, blockIndex])
            ?? throw new InvalidOperationException("MF 方向位图命令为空"));
    }
}
