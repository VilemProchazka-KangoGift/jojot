using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Undo memory limits: the per-tab byte cap enforced by <see cref="UndoStack"/>, the
/// "collapse only when something can be freed" rule and the byte accounting of
/// <see cref="UndoManager"/>.
/// </summary>
public class UndoMemoryTests
{
    private readonly TestClock _clock = new();

    private static string Text(char c, int chars) => new(c, chars); // 2 bytes per char

    // ─── Per-tab cap (UndoStack) ────────────────────────────────────

    [Fact]
    public void DefaultCap_Is16MB()
    {
        new UndoStack(1, _clock).MaxBytes.Should().Be(16L * 1024 * 1024);
        UndoStack.DefaultMaxBytes.Should().Be(16L * 1024 * 1024);
    }

    [Fact]
    public void PushSnapshot_OverCap_DropsOldestEntries()
    {
        var stack = new UndoStack(1, _clock) { MaxBytes = 100 };
        stack.PushInitialContent(Text('a', 20)); // 40 bytes
        stack.PushSnapshot(Text('b', 20));       // 80
        stack.PushSnapshot(Text('c', 20));       // 120 → oldest ('a') dropped

        stack.EstimatedBytes.Should().Be(80);
        stack.TotalCount.Should().Be(2);
        stack.Undo()!.Value.Content.Should().Be(Text('b', 20));
        stack.CanUndo.Should().BeFalse("the oldest entry was dropped to honor the cap");
        stack.Redo()!.Value.Content.Should().Be(Text('c', 20));
    }

    [Fact]
    public void PushSnapshot_OverCap_NeverDropsCurrentEntry()
    {
        var stack = new UndoStack(1, _clock) { MaxBytes = 10 };
        stack.PushInitialContent(Text('x', 100));
        stack.PushSnapshot(Text('y', 100)); // a single entry above the cap is still kept

        stack.TotalCount.Should().Be(1);
        stack.EstimatedBytes.Should().Be(200);
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void PushSnapshot_OverCap_DropsTier2BeforeTier1()
    {
        var stack = new UndoStack(1, _clock);
        stack.PushInitialContent(Text('a', 100));
        foreach (char c in "bcdef")
            stack.PushSnapshot(Text(c, 100));
        stack.CollapseTier1IntoTier2();            // tier-2 = [a, f] (every 5th), tier-1 empty
        stack.MaxBytes = 600;

        stack.PushSnapshot(Text('g', 100));        // 600 — at the cap, nothing dropped
        stack.TotalCount.Should().Be(3);

        stack.PushSnapshot(Text('h', 100));        // 800 → the logically oldest (tier-2 'a') goes

        stack.EstimatedBytes.Should().Be(600);
        var history = new List<string>();
        while (stack.Undo() is { } entry) history.Add(entry.Content);
        history.Should().Equal(Text('g', 100), Text('f', 100));
    }

    [Fact]
    public void Manager_AppliesPerTabCap_ToStacksItCreates()
    {
        var mgr = new UndoManager(_clock, budgetBytes: long.MaxValue / 2, perTabMaxBytes: 100);

        mgr.PushInitialContent(1, Text('a', 20));
        mgr.PushSnapshot(1, Text('b', 20));
        mgr.PushSnapshot(1, Text('c', 20));

        mgr.GetStack(1)!.MaxBytes.Should().Be(100);
        mgr.TotalEstimatedBytes.Should().Be(80);
        mgr.CachedTotalBytes.Should().Be(80, "the cap's drops must flow into the cached total");
    }

    // ─── Collapse only when it can free something ───────────────────

    [Fact]
    public void Collapse_NotStarted_WhenOnlyActiveTier1HistoryExists()
    {
        // Budget 1000 bytes → collapse threshold 800. The active tab alone exceeds it, but its
        // fine-grained history is exempt and it has no tier-2 entries: nothing can be freed.
        var mgr = new UndoManager(_clock, budgetBytes: 1000, perTabMaxBytes: long.MaxValue);
        mgr.SetActiveTab(1);
        mgr.PushInitialContent(1, Text('a', 100));
        for (int i = 0; i < 10; i++)
            mgr.PushSnapshot(1, Text((char)('b' + i), 100));

        mgr.CachedTotalBytes.Should().BeGreaterThan(800);
        mgr.CollapsesStarted.Should().Be(0, "a collapse that can free nothing must not be started on every push");
    }

    [Fact]
    public async Task Collapse_Started_WhenInactiveStackCanBeFreed()
    {
        var mgr = new UndoManager(_clock, budgetBytes: 1000, perTabMaxBytes: long.MaxValue);

        // Inactive tab 2 with several fine-grained entries (collapsible).
        mgr.SetActiveTab(2);
        mgr.PushInitialContent(2, Text('a', 50));
        for (int i = 0; i < 6; i++)
            mgr.PushSnapshot(2, Text((char)('b' + i), 50)); // 7 × 100 = 700 bytes

        mgr.SetActiveTab(1);
        mgr.PushInitialContent(1, Text('x', 100)); // +200 → 900 > 800

        mgr.CollapsesStarted.Should().Be(1);
        await mgr.LastCollapse;

        mgr.CachedTotalBytes.Should().BeLessThanOrEqualTo(600);
        mgr.CachedTotalBytes.Should().Be(mgr.TotalEstimatedBytes);
        mgr.CanUndo(1).Should().BeFalse();
        mgr.GetStack(1)!.EstimatedBytes.Should().Be(200, "the active tab's tier-1 history is never collapsed");
    }

    [Fact]
    public async Task Collapse_EvictsActiveTabTier2_BeforeCurrent_KeepsCurrentEntry()
    {
        var mgr = new UndoManager(_clock, budgetBytes: 1000, perTabMaxBytes: long.MaxValue);
        mgr.SetActiveTab(1);
        mgr.PushInitialContent(1, Text('a', 50));
        foreach (char c in "bcdef")
            mgr.PushSnapshot(1, Text(c, 50));            // 6 × 100 = 600

        // Give the active stack tier-2 entries: tier-2 = [a, f] (200 bytes), tier-1 empty. Done on
        // the stack directly, so the manager's cached total drifts high (still 600) — the
        // collapse must re-sync it.
        mgr.GetStack(1)!.CollapseTier1IntoTier2();

        mgr.PushSnapshot(1, Text('g', 50));              // real 300
        mgr.CollapsesStarted.Should().Be(0);

        mgr.PushSnapshot(1, Text('h', 300));             // real 900, over the 800 threshold

        mgr.CollapsesStarted.Should().Be(1);
        await mgr.LastCollapse;

        mgr.TotalEstimatedBytes.Should().Be(700, "the active tab's two tier-2 entries (200 bytes) were evicted");
        mgr.CachedTotalBytes.Should().Be(mgr.TotalEstimatedBytes, "collapse re-syncs a drifted cache");

        // The current entry and the fine-grained history survive.
        mgr.Undo(1)!.Value.Content.Should().Be(Text('g', 50));
        mgr.CanUndo(1).Should().BeFalse();
        mgr.Redo(1)!.Value.Content.Should().Be(Text('h', 300));
    }

    // ─── Byte accounting ────────────────────────────────────────────

    [Fact]
    public void ManagerPushes_KeepCachedTotalInSync()
    {
        var mgr = new UndoManager(_clock);

        mgr.EnsureInitialContent(1, "hello");          // seeds the empty stack
        mgr.EnsureInitialContent(1, "ignored");        // single entry → re-seeds (no history yet)
        mgr.PushSnapshot(1, "hello world");
        mgr.EnsureInitialContent(1, "no-op now");      // has history → untouched
        mgr.PushCheckpoint(1, "checkpoint");
        mgr.PushInitialContent(2, "other");
        mgr.PushCheckpoint(2, "other cp");

        mgr.CachedTotalBytes.Should().Be(mgr.TotalEstimatedBytes);

        mgr.RemoveStack(2);
        mgr.CachedTotalBytes.Should().Be(mgr.TotalEstimatedBytes);
    }

    [Fact]
    public void EnsureInitialContent_DoesNotResetExistingHistory()
    {
        var mgr = new UndoManager(_clock);
        mgr.EnsureInitialContent(1, "a");
        mgr.PushSnapshot(1, "ab");

        mgr.EnsureInitialContent(1, "zzz");

        mgr.Undo(1)!.Value.Content.Should().Be("a");
    }

    [Fact]
    public void IsCheckpointDue_FollowsFiveMinuteRule()
    {
        var mgr = new UndoManager(_clock);
        mgr.IsCheckpointDue(1).Should().BeFalse("no stack yet");

        mgr.PushInitialContent(1, "a");
        mgr.IsCheckpointDue(1).Should().BeFalse();

        _clock.Advance(TimeSpan.FromMinutes(5));
        mgr.IsCheckpointDue(1).Should().BeTrue();

        mgr.PushCheckpoint(1, "a2");
        mgr.IsCheckpointDue(1).Should().BeFalse();
    }
}
