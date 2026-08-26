using Xunit;

namespace AuroraStruct3D.Calibration;

public class CalibPointCloudStateStoreTests
{
    [Fact]
    public void AddIncrementalPointCloud_ShouldRejectChunkBeyondMemoryLimit()
    {
        CalibPointCloudStateStore store = new()
        {
            MaximumAccumulatedPointCloudBytes = 5,
        };
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);

        Assert.True(store.AddIncrementalPointCloud(projectId, [1, 2, 3], 1));
        Assert.False(store.AddIncrementalPointCloud(projectId, [4, 5, 6], 1));
        Assert.Equal(1, store.GetTotalPointCount(projectId));
        Assert.Single(store.GetAccumulatedPointCloudChunks(projectId));
    }

    [Fact]
    public void ReleaseAccumulatedPointCloudChunks_ShouldReleaseBytesAfterPersisting()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);
        Assert.True(store.AddIncrementalPointCloud(projectId, [1, 2, 3], 1));

        store.ReleaseAccumulatedPointCloudChunks(projectId);

        Assert.Empty(store.GetAccumulatedPointCloudChunks(projectId));
        Assert.Equal(0, store.GetOrCreate(projectId).AccumulatedPointCloudBytes);
    }
}
