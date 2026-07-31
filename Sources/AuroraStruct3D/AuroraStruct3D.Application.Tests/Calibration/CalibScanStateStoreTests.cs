using AuroraStruct3D.Calibration;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class CalibScanStateStoreTests
{
    [Fact]
    public async Task StopAsync_WaitsForScanLoopCleanup()
    {
        CalibScanStateStore store = new();
        Guid projectId = Guid.NewGuid();
        TaskCompletionSource loopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cleanupCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        store.Start(projectId, CalibScanMode.TwoCamera1Light, totalFrameCount: 8);
        store.StartScanLoop(
            projectId,
            async cancellationToken =>
            {
                loopStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    await Task.Delay(20);
                    cleanupCompleted.SetResult();
                }
            }
        );

        await loopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        CalibScanSessionState? stopped = await store.StopAsync(projectId);

        Assert.True(cleanupCompleted.Task.IsCompleted);
        Assert.NotNull(stopped);
        Assert.False(stopped.IsRunning);
        Assert.Equal(CalibScanRunState.Idle, stopped.State);
    }
}
