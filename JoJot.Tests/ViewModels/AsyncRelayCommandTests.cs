using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Extended tests for AsyncRelayCommand and AsyncRelayCommand{T}
/// covering exception handling, Execute(object?), and edge cases.
/// </summary>
public class AsyncRelayCommandTests
{
    // ─── AsyncRelayCommand (non-generic) ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ResetsIsExecuting_OnException()
    {
        var cmd = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));

        var act = () => cmd.ExecuteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Should be re-executable after failure
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RaisesCanExecuteChanged_EvenOnException()
    {
        var cmd = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));
        var raiseCount = 0;
        cmd.CanExecuteChanged += (_, _) => raiseCount++;

        try { await cmd.ExecuteAsync(); } catch { }

        // Should fire on start and on finish (in finally)
        raiseCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_FireAndForget_CallsAction()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(() => { tcs.SetResult(); return Task.CompletedTask; });

        cmd.Execute(null);

        // Wait for the async void to complete
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(tcs.Task);
    }

    [Fact]
    public async Task CanExecute_CombinesIsExecutingAndPredicate()
    {
        var canExec = true;
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(() => tcs.Task, () => canExec);

        cmd.CanExecute(null).Should().BeTrue();

        canExec = false;
        cmd.CanExecute(null).Should().BeFalse();

        canExec = true;
        var task = cmd.ExecuteAsync();
        cmd.CanExecute(null).Should().BeFalse(); // _isExecuting blocks

        tcs.SetResult();
        await task;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        var fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void RaiseCanExecuteChanged_NoSubscribers_DoesNotThrow()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        cmd.RaiseCanExecuteChanged(); // Should not throw
    }

    // ─── AsyncRelayCommand<T> ─────────────────────────────────────────

    [Fact]
    public void AsyncRelayCommandT_ThrowsOnNullFunc()
    {
        var act = () => new AsyncRelayCommand<string>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AsyncRelayCommandT_ExecuteAsync_ResetsOnException()
    {
        var cmd = new AsyncRelayCommand<int>(_ => throw new InvalidOperationException("boom"));

        var act = () => cmd.ExecuteAsync(42);
        await act.Should().ThrowAsync<InvalidOperationException>();

        cmd.CanExecute(42).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncRelayCommandT_RaisesCanExecuteChanged_OnStartAndFinish()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand<string>(_ => tcs.Task);
        var raiseCount = 0;
        cmd.CanExecuteChanged += (_, _) => raiseCount++;

        var task = cmd.ExecuteAsync("test");
        raiseCount.Should().Be(1);

        tcs.SetResult();
        await task;
        raiseCount.Should().Be(2);
    }

    [Fact]
    public void AsyncRelayCommandT_CanExecute_ReturnsTrue_WhenIdle_NoPredicate()
    {
        var cmd = new AsyncRelayCommand<int>(_ => Task.CompletedTask);
        cmd.CanExecute(0).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncRelayCommandT_CanExecute_False_WhileExecuting()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand<int>(_ => tcs.Task);

        var task = cmd.ExecuteAsync(1);
        cmd.CanExecute(1).Should().BeFalse();

        tcs.SetResult();
        await task;
        cmd.CanExecute(1).Should().BeTrue();
    }

    [Fact]
    public void AsyncRelayCommandT_RaiseCanExecuteChanged_NoSubscribers_DoesNotThrow()
    {
        var cmd = new AsyncRelayCommand<string>(_ => Task.CompletedTask);
        cmd.RaiseCanExecuteChanged();
    }

    [Fact]
    public async Task AsyncRelayCommandT_PassesNullParameter()
    {
        string? received = "not null";
        var cmd = new AsyncRelayCommand<string?>(async s =>
        {
            await Task.Yield();
            received = s;
        });

        await cmd.ExecuteAsync(null);

        received.Should().BeNull();
    }

    [Fact]
    public async Task AsyncRelayCommandT_Execute_CastsParameter()
    {
        var tcs = new TaskCompletionSource();
        int? received = null;
        var cmd = new AsyncRelayCommand<int>(async n =>
        {
            received = n;
            tcs.SetResult();
            await Task.CompletedTask;
        });

        cmd.Execute(42);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(tcs.Task);
        received.Should().Be(42);
    }
}
