using System.Collections.Concurrent;
using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for the dedicated "JoJot-DB" worker thread that every database entry point hops onto
/// (Microsoft.Data.Sqlite's async APIs complete synchronously, so without the hop all DB work
/// would run on the caller's — in the app, the UI — thread).
/// </summary>
[Collection("Database")]
public sealed class DbWorkerThreadTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task RunOnDbThreadAsync_ReturnsResult_FromDbThread()
    {
        DatabaseCore.IsOnDbThread.Should().BeFalse("the test thread is not the DB thread");

        var (result, threadName, onDbThread) = await DatabaseCore.RunOnDbThreadAsync(() =>
            Task.FromResult((42, Thread.CurrentThread.Name, DatabaseCore.IsOnDbThread)));

        result.Should().Be(42);
        threadName.Should().Be(DatabaseCore.DbThreadName);
        onDbThread.Should().BeTrue();
    }

    [Fact]
    public async Task RunOnDbThreadAsync_PropagatesException()
    {
        var act = () => DatabaseCore.RunOnDbThreadAsync(
            () => Task.FromException(new InvalidOperationException("boom")));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task RunOnDbThreadAsync_Generic_PropagatesException_FromAsyncBody()
    {
        var act = () => DatabaseCore.RunOnDbThreadAsync<int>(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("async boom");
    }

    [Fact]
    public async Task RunOnDbThreadAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DatabaseCore.RunOnDbThreadAsync(() => Task.FromCanceled<int>(cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunOnDbThreadAsync_NestedCallFromDbThread_RunsInline_WithoutDeadlock()
    {
        var outer = DatabaseCore.RunOnDbThreadAsync(async () =>
        {
            int outerThread = Environment.CurrentManagedThreadId;
            int innerThread = await DatabaseCore.RunOnDbThreadAsync(
                () => Task.FromResult(Environment.CurrentManagedThreadId));
            return (outerThread, innerThread);
        });

        var (outerThreadId, innerThreadId) = await outer.WaitAsync(TimeSpan.FromSeconds(5));

        innerThreadId.Should().Be(outerThreadId, "a nested hop from the DB thread must run inline");
    }

    [Fact]
    public async Task RunOnDbThreadAsync_ExecutesInFifoOrder()
    {
        var order = new ConcurrentQueue<int>();
        var tasks = Enumerable.Range(0, 50)
            .Select(i => DatabaseCore.RunOnDbThreadAsync(() =>
            {
                order.Enqueue(i);
                return Task.CompletedTask;
            }))
            .ToList();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        order.Should().Equal(Enumerable.Range(0, 50));
    }

    [Fact]
    public async Task InsertNoteAsync_ExecutesOnDbThread()
    {
        // IsUiThread is probed by every CreateContext / AcquireWriteLockAsync, so it doubles as a
        // hook that reveals which thread the store's body actually ran on.
        var observed = new ConcurrentBag<string?>();
        DatabaseCore.IsUiThread = () =>
        {
            observed.Add(Thread.CurrentThread.Name);
            return false;
        };
        try
        {
            long id = await NoteStore.InsertNoteAsync("desktop-thread-test", null, "x", false, 0);
            id.Should().BePositive();
        }
        finally
        {
            DatabaseCore.IsUiThread = null;
        }

        observed.Should().NotBeEmpty();
        observed.Should().OnlyContain(name => name == DatabaseCore.DbThreadName);
    }

    [Fact]
    public async Task CloseAsync_ThenStoreCall_RestartsWorker()
    {
        await _db.DisposeAsync(); // closes the DB and stops the worker thread

        // A call after close (e.g. a late shutdown write) must not hang: the worker restarts lazily.
        var name = await DatabaseCore.RunOnDbThreadAsync(() => Task.FromResult(Thread.CurrentThread.Name))
            .WaitAsync(TimeSpan.FromSeconds(5));
        name.Should().Be(DatabaseCore.DbThreadName);

        _db = await TestDatabase.CreateAsync();
    }
}
