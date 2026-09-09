using Xunit;

namespace AuroraStruct3D.Calibration;

public class CalibPointCloudStateStoreTests
{
    [Fact]
    public void AddIncrementalPointCloud_ShouldRejectChunkBeyondMemoryLimit()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);

        IncrementalPointCloudAddResult first =
            store.AddIncrementalPointCloud(projectId, [1, 2, 3], 1);
        Assert.True(first.Added);
        store.MaximumAccumulatedPointCloudBytes = first.AccumulatedCompressedBytes;

        IncrementalPointCloudAddResult second =
            store.AddIncrementalPointCloud(projectId, [4, 5, 6], 1);

        Assert.Equal(IncrementalPointCloudAddStatus.StorageLimitExceeded, second.Status);
        Assert.Equal(1, store.GetTotalPointCount(projectId));
        Assert.Single(store.GetAccumulatedPointCloudChunks(projectId));
    }

    [Fact]
    public void AddIncrementalPointCloud_ShouldCompressAsciiPlyChunk()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);
        byte[] chunk = System.Text.Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("1.000000 2.000000 3.000000 10 20 30\n", 10_000))
        );

        IncrementalPointCloudAddResult result =
            store.AddIncrementalPointCloud(projectId, chunk, 10_000);

        Assert.True(result.Added);
        CompressedPointCloudChunk stored = Assert.Single(
            store.GetAccumulatedPointCloudChunks(projectId)
        );
        Assert.True(stored.CompressedBytes.Length < chunk.Length / 10);
        Assert.Equal(chunk.Length, stored.OriginalByteCount);
        Assert.Equal(10_000, stored.PointCount);
    }

    [Fact]
    public void ReleaseAccumulatedPointCloudChunks_ShouldReleaseBytesAfterPersisting()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);
        Assert.True(store.AddIncrementalPointCloud(projectId, [1, 2, 3], 1).Added);

        store.ReleaseAccumulatedPointCloudChunks(projectId);

        Assert.Empty(store.GetAccumulatedPointCloudChunks(projectId));
        Assert.Equal(0, store.GetOrCreate(projectId).AccumulatedPointCloudBytes);
    }

    [Fact]
    public void StartIncrementalMode_ShouldClearChunksFromPreviousFailedRun()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);
        Assert.True(store.AddIncrementalPointCloud(projectId, [1, 2, 3], 1).Added);

        PointCloudSessionState restarted = store.StartIncrementalMode(projectId);

        Assert.True(restarted.IsIncrementalMode);
        Assert.Equal(0, restarted.TotalPointCount);
        Assert.Equal(0, restarted.AccumulatedPointCloudBytes);
        Assert.Empty(restarted.AccumulatedPointCloudChunks);
    }

    [Fact]
    public async Task MergePlyFilesAsync_ShouldStreamCompressedChunksIntoValidPly()
    {
        CalibPointCloudStateStore store = new();
        Guid projectId = Guid.NewGuid();
        store.StartIncrementalMode(projectId);
        const string header = "ply\nformat ascii 1.0\nelement vertex 1\n"
            + "property float x\nproperty float y\nproperty float z\n"
            + "property uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n";
        byte[] first = System.Text.Encoding.ASCII.GetBytes(header + "1 2 3 4 5 6\n");
        byte[] second = System.Text.Encoding.ASCII.GetBytes(header + "7 8 9 10 11 12\n");
        Assert.True(store.AddIncrementalPointCloud(projectId, first, 1).Added);
        Assert.True(store.AddIncrementalPointCloud(projectId, second, 1).Added);
        using MemoryStream merged = new();

        await CalibPointCloudAppService.MergePlyFilesAsync(
            store.GetAccumulatedPointCloudChunks(projectId),
            merged
        );

        string result = System.Text.Encoding.ASCII.GetString(merged.ToArray());
        Assert.Contains("element vertex 2\n", result);
        Assert.EndsWith("1 2 3 4 5 6\n7 8 9 10 11 12\n", result);
    }
}
