using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>Tests for the background persistence tracker.</summary>
public sealed class BackgroundWriterTests
{
    public BackgroundWriterTests() => LogService.InitializeNoop();

    [Fact]
    public async Task Run_TracksPendingWork_UntilCompletion()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        BackgroundWriter.Run(() => gate.Task, "test-op");
        BackgroundWriter.PendingCount.Should().BeGreaterThan(0);

        gate.SetResult();
        (await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        BackgroundWriter.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task Run_RaisesWriteFailed_AndStillCompletes_OnException()
    {
        string? failedOp = null;
        Action<string> handler = name => failedOp = name;
        BackgroundWriter.WriteFailed += handler;
        try
        {
            BackgroundWriter.Run(() => Task.FromException(new InvalidOperationException("boom")), "failing-op");

            (await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
            failedOp.Should().Be("failing-op");
        }
        finally
        {
            BackgroundWriter.WriteFailed -= handler;
        }
    }

    [Fact]
    public async Task Drain_ReturnsFalse_OnTimeout()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BackgroundWriter.Run(() => never.Task, "stuck-op");

        (await BackgroundWriter.DrainAsync(TimeSpan.FromMilliseconds(100))).Should().BeFalse();

        never.SetResult(); // clean up for other tests
        await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Run_ReturnsBeforeDbHopCompletes_AndOperationRunsOnAnotherThread()
    {
        int callerThreadId = Environment.CurrentManagedThreadId;
        int observedThreadId = -1;
        bool completed = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        BackgroundWriter.Run(async () =>
        {
            await DatabaseCore.RunOnDbThreadAsync(async () =>
            {
                observedThreadId = Environment.CurrentManagedThreadId;
                entered.SetResult();
                await gate.Task;
            }).ConfigureAwait(false);
            completed = true;
        }, "db-hop-op");

        try
        {
            // Run returned while the operation is still parked on the DB thread.
            completed.Should().BeFalse("Run must not wait for the database work");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            completed.Should().BeFalse();
        }
        finally
        {
            gate.TrySetResult();
        }

        (await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        completed.Should().BeTrue();
        observedThreadId.Should().NotBe(callerThreadId, "the DB work runs on the dedicated DB thread");
    }

    [Fact]
    public async Task Run_OperationAwaitsDoNotCaptureCallerSynchronizationContext()
    {
        var context = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            // No ConfigureAwait(false) on purpose: BackgroundWriter must still keep the
            // continuation off the caller's (UI) context so a sync-blocked UI thread can't deadlock it.
            BackgroundWriter.Run(async () => await DatabaseCore.RunOnDbThreadAsync(() => Task.Delay(10)), "no-ctx-op");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        (await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        context.PostCount.Should().Be(0);
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Post(d, state);
        }
    }
}
