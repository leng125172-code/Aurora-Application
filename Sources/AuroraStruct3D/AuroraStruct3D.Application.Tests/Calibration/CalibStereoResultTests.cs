using AuroraStruct3D.Calibration;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class CalibStereoResultTests
{
    [Fact]
    public void Restore_ClearsSoftDeleteAuditState()
    {
        CalibStereoResult result = CreateResult();
        result.IsDeleted = true;
        result.DeleterId = Guid.NewGuid();
        result.DeletionTime = DateTime.UtcNow;

        CalibStereoResult restored = result.Restore();

        Assert.Same(result, restored);
        Assert.False(result.IsDeleted);
        Assert.Null(result.DeleterId);
        Assert.Null(result.DeletionTime);
    }

    private static CalibStereoResult CreateResult()
    {
        return new CalibStereoResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.1,
            "[]",
            "[]",
            "[]",
            "[]",
            "[]",
            "[]",
            "[]",
            "[]",
            1920,
            1200,
            "map1x",
            "map1y",
            "map2x",
            "map2y"
        );
    }
}
