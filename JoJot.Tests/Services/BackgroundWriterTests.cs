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
}
