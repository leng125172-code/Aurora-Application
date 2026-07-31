using AuroraStruct3D.Calibration;
using AuroraStruct3D.Calibration.Dtos;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class CalibScanBlobKeyTests
{
    private static readonly Guid ProjectId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CameraId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void BuildScanBlobKey_SeparatesCameraRoleRoundAndFrame()
    {
        string key = CalibScanAppService.BuildScanBlobKey(
            ProjectId,
            CameraId,
            12,
            7,
            CalibScanCameraRole.Secondary
        );

        Assert.Equal(
            $"{ProjectId}/{CameraId}/scan/secondary_r0012_f007.jpg",
            key
        );
    }

    [Fact]
    public void BuildTextureBlobKey_DoesNotOverlapFringeFrameNames()
    {
        string textureKey = CalibScanAppService.BuildTextureBlobKey(
            ProjectId,
            CameraId,
            12
        );
        string fringeKey = CalibScanAppService.BuildScanBlobKey(
            ProjectId,
            CameraId,
            12,
            0,
            CalibScanCameraRole.Main
        );

        Assert.Equal($"{ProjectId}/{CameraId}/scan/main_r0012_texture.jpg", textureKey);
        Assert.NotEqual(fringeKey, textureKey);
    }
}
