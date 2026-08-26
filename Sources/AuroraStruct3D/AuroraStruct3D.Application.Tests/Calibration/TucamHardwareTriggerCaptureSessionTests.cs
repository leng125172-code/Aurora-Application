using System.Collections.Concurrent;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class TucamHardwareTriggerCaptureSessionTests
{
    [Fact]
    public async Task Capture_should_start_both_waiters_before_one_trigger()
    {
        ConcurrentQueue<string> events = new();
        FakeTriggeredCamera main = new(0, events, [1]);
        FakeTriggeredCamera secondary = new(1, events, [2]);

        await using TucamHardwareTriggerCaptureSession session =
            await TucamHardwareTriggerCaptureSession.StartAsync(
                new ITucamTriggeredCameraChannel[] { main, secondary },
                CancellationToken.None
            );

        IReadOnlyList<byte[]> frames = await session.CaptureAsync(
            _ =>
            {
                Assert.True(main.WaitEntered.Task.IsCompleted);
                Assert.True(secondary.WaitEntered.Task.IsCompleted);
                events.Enqueue("trigger");
                main.ReleaseFrame();
                secondary.ReleaseFrame();
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        Assert.Equal(new byte[] { 1 }, frames[0]);
        Assert.Equal(new byte[] { 2 }, frames[1]);
        string[] order = events.ToArray();
        Assert.True(Array.IndexOf(order, "start:0") < Array.IndexOf(order, "start:1"));
        Assert.True(Array.IndexOf(order, "wait:0") < Array.IndexOf(order, "trigger"));
        Assert.True(Array.IndexOf(order, "wait:1") < Array.IndexOf(order, "trigger"));
        Assert.Equal(1, order.Count(x => x == "trigger"));
    }

    [Fact]
    public async Task Trigger_failure_should_abort_both_waiters_and_stop_both_cameras()
    {
        ConcurrentQueue<string> events = new();
        FakeTriggeredCamera main = new(0, events, [1]);
        FakeTriggeredCamera secondary = new(1, events, [2]);
        await using TucamHardwareTriggerCaptureSession session =
            await TucamHardwareTriggerCaptureSession.StartAsync(
                new ITucamTriggeredCameraChannel[] { main, secondary },
                CancellationToken.None
            );

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                session.CaptureAsync(
                    _ => throw new InvalidOperationException("projector failed"),
                    CancellationToken.None
                )
        );

        Assert.Equal("projector failed", error.Message);
        Assert.Contains("stop:0", events);
        Assert.Contains("stop:1", events);
        Assert.True(main.WaitCompleted.Task.IsCompleted);
        Assert.True(secondary.WaitCompleted.Task.IsCompleted);
    }

    private sealed class FakeTriggeredCamera(
        int runtimeIndex,
        ConcurrentQueue<string> events,
        byte[] frame
    ) : ITucamTriggeredCameraChannel
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int RuntimeIndex { get; } = runtimeIndex;
        public TaskCompletionSource WaitEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource WaitCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task StartAsync()
        {
            events.Enqueue($"start:{RuntimeIndex}");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            events.Enqueue($"stop:{RuntimeIndex}");
            _release.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<byte[]> GrabAsync(CancellationToken cancellationToken)
        {
            events.Enqueue($"wait:{RuntimeIndex}");
            WaitEntered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            WaitCompleted.TrySetResult();
            return frame;
        }

        public void ReleaseFrame() => _release.TrySetResult();
    }
}
