using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class RelayCommandTests
{
    // ─── RelayCommand (sync, parameterless) ──────────────────────────

    [Fact]
    public void RelayCommand_Execute_CallsAction()
    {
        var called = false;
        var cmd = new RelayCommand(() => called = true);

        cmd.Execute(null);

        called.Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_CanExecute_ReturnsTrue_WhenNoFunc()
    {
        var cmd = new RelayCommand(() => { });

        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_CanExecute_DelegatesToFunc()
    {
        var canExec = false;
        var cmd = new RelayCommand(() => { }, () => canExec);

        cmd.CanExecute(null).Should().BeFalse();

        canExec = true;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        var fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_ThrowsOnNullAction()
    {
        var act = () => new RelayCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ─── RelayCommand<T> (sync, typed) ───────────────────────────────

    [Fact]
    public void RelayCommandT_Execute_PassesParameter()
    {
        string? received = null;
        var cmd = new RelayCommand<string>(s => received = s);

        cmd.Execute("hello");

        received.Should().Be("hello");
    }

    [Fact]
    public void RelayCommandT_CanExecute_DelegatesToFunc()
    {
        var cmd = new RelayCommand<int>(
            _ => { },
            n => n > 0
        );

        cmd.CanExecute(5).Should().BeTrue();
        cmd.CanExecute(-1).Should().BeFalse();
    }

    [Fact]
    public void RelayCommandT_CanExecute_ReturnsTrue_WhenNoFunc()
    {
        var cmd = new RelayCommand<string>(_ => { });

        cmd.CanExecute("any").Should().BeTrue();
    }

    // ─── AsyncRelayCommand (async, parameterless) ────────────────────

    [Fact]
    public async Task AsyncRelayCommand_ExecuteAsync_CallsFunc()
    {
        var called = false;
        var cmd = new AsyncRelayCommand(async () =>
        {
            await Task.Yield();
            called = true;
        });

        await cmd.ExecuteAsync();

        called.Should().BeTrue();
    }

    [Fact]
    public async Task AsyncRelayCommand_PreventsReentrance()
    {
        var enterCount = 0;
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(async () =>
        {
            Interlocked.Increment(ref enterCount);
            await tcs.Task;
        });

        // Start first execution
        var first = cmd.ExecuteAsync();
        cmd.CanExecute(null).Should().BeFalse();

        // Second call should be no-op
        await cmd.ExecuteAsync();
        enterCount.Should().Be(1);

        // Complete first
        tcs.SetResult();
        await first;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncRelayCommand_RaisesCanExecuteChanged_OnStartAndFinish()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(() => tcs.Task);
        var raiseCount = 0;
        cmd.CanExecuteChanged += (_, _) => raiseCount++;

        var task = cmd.ExecuteAsync();
        raiseCount.Should().Be(1); // raised on start

        tcs.SetResult();
        await task;
        raiseCount.Should().Be(2); // raised on finish
    }

    [Fact]
    public void AsyncRelayCommand_CanExecute_ReturnsTrue_WhenIdle()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);

        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AsyncRelayCommand_CanExecute_DelegatesToFunc()
    {
        var canExec = false;
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask, () => canExec);

        cmd.CanExecute(null).Should().BeFalse();

        canExec = true;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AsyncRelayCommand_ThrowsOnNullFunc()
    {
        var act = () => new AsyncRelayCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ─── AsyncRelayCommand<T> (async, typed) ─────────────────────────

    [Fact]
    public async Task AsyncRelayCommandT_ExecuteAsync_PassesParameter()
    {
        int? received = null;
        var cmd = new AsyncRelayCommand<int>(async n =>
        {
            await Task.Yield();
            received = n;
        });

        await cmd.ExecuteAsync(42);

        received.Should().Be(42);
    }

    [Fact]
    public async Task AsyncRelayCommandT_PreventsReentrance()
    {
        var enterCount = 0;
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand<string>(async _ =>
        {
            Interlocked.Increment(ref enterCount);
            await tcs.Task;
        });

        var first = cmd.ExecuteAsync("a");
        await cmd.ExecuteAsync("b"); // no-op
        enterCount.Should().Be(1);

        tcs.SetResult();
        await first;
    }

    [Fact]
    public void AsyncRelayCommandT_CanExecute_DelegatesToFunc()
    {
        var cmd = new AsyncRelayCommand<int>(
            _ => Task.CompletedTask,
            n => n > 0
        );

        cmd.CanExecute(5).Should().BeTrue();
        cmd.CanExecute(-1).Should().BeFalse();
    }
}
