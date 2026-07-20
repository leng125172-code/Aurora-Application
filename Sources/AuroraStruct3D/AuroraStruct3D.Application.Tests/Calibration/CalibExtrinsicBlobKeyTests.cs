using AuroraStruct3D.Calibration;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

/// <summary>
/// 外参样本 BLOB Key 帧序号解析的单元测试。
/// 校验 <see cref="CalibExtrinsicSampler.ExtractFrameIndexFromBlobKey"/> 对合法/非法 Key 的行为，
/// 该解析结果决定投影棋盘格多帧样本的排序，边界处理错误会导致样本错配。
/// </summary>
public class CalibExtrinsicBlobKeyTests
{
    [Theory]
    // BuildBlobKey 生成的真实格式：{proj}/{cam}/extrinsic/{yyyyMMddHHmmss_fff}_f{000}.jpg
    [InlineData("p/c/extrinsic/20260717120000_123_f003.jpg", 3)]
    [InlineData("p/c/extrinsic/20260717120000_123_f000.jpg", 0)]
    [InlineData("p/c/extrinsic/20260717120000_123_f125.jpg", 125)]
    [InlineData("simple_f7.jpg", 7)]
    public void ExtractFrameIndex_ValidKey_ReturnsIndex(string blobKey, int expected)
    {
        int? index = CalibExtrinsicSampler.ExtractFrameIndexFromBlobKey(blobKey);

        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("p/c/intrinsic/20260717120000_123.jpg")] // 无 _f 帧后缀
    [InlineData("p/c/extrinsic/frame_fXY.jpg")] // 非数字帧号
    [InlineData("p/c/extrinsic/frame_f.jpg")] // 帧号为空
    [InlineData("no-suffix-at-all")] // 无 .jpg
    public void ExtractFrameIndex_InvalidKey_ReturnsNull(string blobKey)
    {
        int? index = CalibExtrinsicSampler.ExtractFrameIndexFromBlobKey(blobKey);

        Assert.Null(index);
    }

    [Fact]
    public void ExtractFrameIndex_UsesLastUnderscoreF_WhenMultiplePresent()
    {
        // 仅最后一个 _f 后缀视为帧号，避免路径中间的 _f 干扰
        int? index = CalibExtrinsicSampler.ExtractFrameIndexFromBlobKey("dir_f0/scene_f042.jpg");

        Assert.Equal(42, index);
    }
}
