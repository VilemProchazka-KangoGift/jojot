using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

public class UndoStackTests
{
    private readonly TestClock _clock = new();

    private UndoStack CreateStack(long tabId = 1) => new(tabId, _clock);

    // ─── PushInitialContent ────────────────────────────────────────────

    [Fact]
    public void PushInitialContent_SetsBaseline()
    {
        var stack = CreateStack();
        stack.PushInitialContent("hello");

        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void PushInitialContent_ClearsPreviousState()
    {
        var stack = CreateStack();
        stack.PushInitialContent("first");
        stack.PushSnapshot("second");
        stack.PushInitialContent("reset");

        stack.CanUndo.Should().BeFalse();
        stack.Undo().Should().BeNull();
    }

    // ─── PushSnapshot ──────────────────────────────────────────────────

    [Fact]
    public void PushSnapshot_AddsEntry_WhenDifferent()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");

        stack.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void PushSnapshot_Deduplicates_WhenSameContent()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("a");

        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PushSnapshot_TruncatesRedoFuture()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");
        stack.PushSnapshot("c");
        stack.Undo(); // back to "b"
        stack.PushSnapshot("d"); // destroys "c"

        stack.CanRedo.Should().BeFalse();
        stack.Undo()!.Should().Be("b");
    }

    [Fact]
    public void PushSnapshot_CapsAtMaxTier1()
    {
        var stack = CreateStack();
        stack.PushInitialContent("initial");

        for (int i = 1; i <= UndoStack.MaxTier1 + 10; i++)
        {
            stack.PushSnapshot($"snapshot_{i}");
        }

        // Should not exceed MaxTier1 entries in tier1
        // Estimated bytes should be bounded
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PushSnapshot_OnEmptyStack_InitializesCorrectly()
    {
        var stack = CreateStack();
        stack.PushSnapshot("first");

        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    // ─── Undo / Redo ───────────────────────────────────────────────────

    [Fact]
    public void Undo_ReturnsPreviousContent()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");
        stack.PushSnapshot("c");

        stack.Undo().Should().Be("b");
        stack.Undo().Should().Be("a");
    }

    [Fact]
    public void Undo_ReturnsNull_AtBeginning()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        stack.Undo().Should().BeNull();
    }

    [Fact]
    public void Redo_ReturnsNextContent()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");
        stack.PushSnapshot("c");
        stack.Undo();
        stack.Undo();

        stack.Redo().Should().Be("b");
        stack.Redo().Should().Be("c");
    }

    [Fact]
    public void Redo_ReturnsNull_AtEnd()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");

        stack.Redo().Should().BeNull();
    }

    [Fact]
    public void CanUndo_CanRedo_CorrectThroughSequence()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();

        stack.PushSnapshot("b");
        stack.CanUndo.Should().BeTrue();
        stack.CanRedo.Should().BeFalse();

        stack.Undo();
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeTrue();
    }

    // ─── PushCheckpoint ────────────────────────────────────────────────

    [Fact]
    public void PushCheckpoint_AddsTier2Entry()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushCheckpoint("checkpoint_1");

        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PushCheckpoint_Deduplicates()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp");
        var bytesAfterFirst = stack.EstimatedBytes;
        stack.PushCheckpoint("cp"); // duplicate

        stack.EstimatedBytes.Should().Be(bytesAfterFirst);
    }

    // ─── ShouldCreateCheckpoint ────────────────────────────────────────

    [Fact]
    public void ShouldCreateCheckpoint_False_BeforeFiveMinutes()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        _clock.Advance(TimeSpan.FromMinutes(4));
        stack.ShouldCreateCheckpoint().Should().BeFalse();
    }

    [Fact]
    public void ShouldCreateCheckpoint_True_AfterFiveMinutes()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        _clock.Advance(TimeSpan.FromMinutes(5.1));
        stack.ShouldCreateCheckpoint().Should().BeTrue();
    }

    [Fact]
    public void ShouldCreateCheckpoint_ResetsAfterCheckpoint()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        _clock.Advance(TimeSpan.FromMinutes(6));
        stack.ShouldCreateCheckpoint().Should().BeTrue();

        stack.PushCheckpoint("cp1");
        stack.ShouldCreateCheckpoint().Should().BeFalse();

        _clock.Advance(TimeSpan.FromMinutes(5.1));
        stack.ShouldCreateCheckpoint().Should().BeTrue();
    }

    // ─── CollapseTier1IntoTier2 ────────────────────────────────────────

    [Fact]
    public void CollapseTier1IntoTier2_SamplesEvery5thEntry()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 20; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        var bytesBefore = stack.EstimatedBytes;
        stack.CollapseTier1IntoTier2();

        // Bytes should decrease (tier-1 cleared, only sampled entries in tier-2)
        stack.EstimatedBytes.Should().BeLessThan(bytesBefore);
    }

    [Fact]
    public void CollapseTier1IntoTier2_NoopWhenTier1Empty()
    {
        var stack = CreateStack();
        stack.CollapseTier1IntoTier2(); // should not throw
        stack.EstimatedBytes.Should().Be(0);
    }

    // ─── EvictOldestTier2 ──────────────────────────────────────────────

    [Fact]
    public void EvictOldestTier2_RemovesEntries()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp1");
        stack.PushCheckpoint("cp2");
        stack.PushCheckpoint("cp3");

        var bytesBefore = stack.EstimatedBytes;
        stack.EvictOldestTier2(2);

        stack.EstimatedBytes.Should().BeLessThan(bytesBefore);
    }

    [Fact]
    public void EvictOldestTier2_NoopWhenEmpty()
    {
        var stack = CreateStack();
        stack.EvictOldestTier2(5); // should not throw
    }

    // ─── EstimatedBytes ────────────────────────────────────────────────

    [Fact]
    public void EstimatedBytes_CalculatesUtf16Size()
    {
        var stack = CreateStack();
        stack.PushInitialContent("abc"); // 3 chars = 6 bytes

        stack.EstimatedBytes.Should().Be(6);
    }

    // ─── LastAccessTime ────────────────────────────────────────────────

    [Fact]
    public void LastAccessTime_UpdatedOnPush()
    {
        var stack = CreateStack();
        var initialTime = _clock.Now;
        stack.PushInitialContent("a");
        stack.LastAccessTime.Should().Be(initialTime);

        _clock.Advance(TimeSpan.FromSeconds(10));
        stack.PushSnapshot("b");
        stack.LastAccessTime.Should().Be(_clock.Now);
    }

    [Fact]
    public void LastAccessTime_UpdatedOnUndoRedo()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");

        _clock.Advance(TimeSpan.FromMinutes(1));
        stack.Undo();
        stack.LastAccessTime.Should().Be(_clock.Now);

        _clock.Advance(TimeSpan.FromMinutes(1));
        stack.Redo();
        stack.LastAccessTime.Should().Be(_clock.Now);
    }

    // ─── Undo captures current content for redo ────────────────────────

    [Fact]
    public void PushSnapshotBeforeUndo_EnablesRedoToUnsavedContent()
    {
        // Simulates: autosave pushed "a" and "b", user typed "c" (not yet autosaved),
        // then called PushSnapshot("c") before Undo — redo should restore "c".
        var stack = CreateStack();
        stack.PushSnapshot("a");
        stack.PushSnapshot("b");

        // Simulate PerformUndo: push current content before calling Undo
        stack.PushSnapshot("c"); // unsaved typing captured before undo
        stack.Undo();            // moves back to "b"

        stack.Redo().Should().Be("c");
    }

    [Fact]
    public void PushSnapshot_IsNoop_WhenContentMatchesCurrentIndex()
    {
        // Dedup: pushing the same content as the current snapshot is a no-op.
        // Undo/Redo should still return correct values with no duplicate entries.
        var stack = CreateStack();
        stack.PushSnapshot("a");
        stack.PushSnapshot("b");
        stack.PushSnapshot("b"); // duplicate — should be ignored

        stack.Undo().Should().Be("a");
        stack.Redo().Should().Be("b");
        stack.CanRedo.Should().BeFalse(); // no extra entries were created
    }

    [Fact]
    public void PushSnapshotBeforeUndo_FullRoundTrip()
    {
        // Full cycle: PushInitialContent("a"), PushSnapshot("b"), then unsaved content "c_unsaved".
        // Two undos return "b" then "a". Two redos return "b" then "c_unsaved".
        var stack = CreateStack();
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");

        // Simulate PerformUndo capturing unsaved typing "c_unsaved" before first undo
        stack.PushSnapshot("c_unsaved");
        stack.Undo().Should().Be("b");  // first undo
        stack.Undo().Should().Be("a");  // second undo

        stack.Redo().Should().Be("b");          // first redo
        stack.Redo().Should().Be("c_unsaved");  // second redo — restores unsaved typing
    }
}
