using System.Reflection;
using AuroraStruct3D.Cameras;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Cameras;

public class CameraScanMatchingTests
{
    [Fact]
    public void Index_Fallback_Should_Be_Disabled_When_Serial_Match_Exists()
    {
        MethodInfo method = GetCanUseIndexFallbackMethod();
        CameraDevice indexCamera = new(Guid.NewGuid(), "Camera #0", 0);
        CameraDevice matchedBySerial = new(Guid.NewGuid(), "Camera #1", 1);

        bool canFallback = (bool)method.Invoke(null, [matchedBySerial, indexCamera])!;

        Assert.False(canFallback);
    }

    [Fact]
    public void Index_Fallback_Should_Be_Disabled_When_Index_Record_Has_Serial()
    {
        MethodInfo method = GetCanUseIndexFallbackMethod();
        CameraDevice indexCamera = new(Guid.NewGuid(), "Camera #0", 0);
        indexCamera.UpdateDeviceSerialNumber("SN-EXISTING");

        bool canFallback = (bool)method.Invoke(null, [null, indexCamera])!;

        Assert.False(canFallback);
    }

    [Fact]
    public void Index_Fallback_Should_Be_Enabled_When_Index_Record_Has_No_Serial()
    {
        MethodInfo method = GetCanUseIndexFallbackMethod();
        CameraDevice indexCamera = new(Guid.NewGuid(), "Camera #0", 0);
        indexCamera.UpdateDeviceSerialNumber(null);

        bool canFallback = (bool)method.Invoke(null, [null, indexCamera])!;

        Assert.True(canFallback);
    }

    private static MethodInfo GetCanUseIndexFallbackMethod()
    {
        return typeof(CameraDeviceAppService).GetMethod(
                "CanUseIndexFallback",
                BindingFlags.NonPublic | BindingFlags.Static
            ) ?? throw new InvalidOperationException("CanUseIndexFallback method not found.");
    }
}
