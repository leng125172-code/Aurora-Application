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

        Assert.Equal(imageCount * 2, frames.Length);
        Assert.All(frames.Take(imageCount), frame => Assert.Equal(720, frame.Length));
        Assert.All(frames.Skip(imageCount), frame => Assert.Equal(1280, frame.Length));
    }

    [Fact]
    public void BuildFringeImagePixels_ShouldApplyConfiguredPhaseShift()
    {
        DownloadFringePatternInputDto input = CreateInput(imageCount: 4);

        byte[][] frames = InvokeBuild(input);

        Assert.Equal(frames[0][2], frames[1][0]);
        Assert.Equal(frames[4][2], frames[5][0]);
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
