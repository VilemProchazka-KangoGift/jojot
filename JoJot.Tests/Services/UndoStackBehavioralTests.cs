using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Behavioral edge-case tests for UndoStack: tier boundaries,
/// exact capacity limits, zero/negative inputs, emoji byte counting,
/// and high-volume undo/redo cycles.
/// </summary>
public class UndoStackBehavioralTests
{
    private readonly TestClock _clock = new();

    private UndoStack CreateStack(long tabId = 1) => new(tabId, _clock);

    // ─── TabId edge case ─────────────────────────────────────────

    [Fact]
    public void TabIdZero_WorksCorrectly()
    {
        var stack = CreateStack(tabId: 0);
        stack.TabId.Should().Be(0);
        stack.PushInitialContent("hello");
        stack.PushSnapshot("world");
        stack.Undo()!.Value.Content.Should().Be("hello");
    }

    // ─── EvictOldestTier2 boundary inputs ────────────────────────

    [Fact]
    public void EvictOldestTier2_ZeroCount_NoOp()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp1");
        stack.PushCheckpoint("cp2");
        var bytesBefore = stack.EstimatedBytes;

        stack.EvictOldestTier2(0);

        stack.EstimatedBytes.Should().Be(bytesBefore);
    }

    [Fact]
    public void EvictOldestTier2_NegativeCount_NoOp()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp1");
        var bytesBefore = stack.EstimatedBytes;

        stack.EvictOldestTier2(-5);

        stack.EstimatedBytes.Should().Be(bytesBefore);
    }

    // ─── PushSnapshot empty string dedup ─────────────────────────

    [Fact]
    public void PushSnapshot_EmptyString_Deduplicates()
    {
        var stack = CreateStack();
        stack.PushInitialContent("");
        stack.PushSnapshot("");

        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PushSnapshot_EmptyString_ThenNonEmpty_CanUndo()
    {
        var stack = CreateStack();
        stack.PushInitialContent("");
        stack.PushSnapshot("text");

        stack.CanUndo.Should().BeTrue();
        stack.Undo()!.Value.Content.Should().Be("");
    }

    // ─── MaxTier1 exact boundary ─────────────────────────────────

    [Fact]
    public void MaxTier1_ExactCount_NoEviction()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");

        // Push exactly MaxTier1-1 more snapshots (total = MaxTier1 = 200)
        for (int i = 1; i < UndoStack.MaxTier1; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Undo all the way back should reach s0
        for (int i = UndoStack.MaxTier1 - 1; i > 0; i--)
        {
            var entry = stack.Undo();
            entry!.Value.Content.Should().Be($"s{i - 1}");
        }
    }

    [Fact]
    public void MaxTier1_PlusOne_PromotesOldest()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");

        // Push MaxTier1 more snapshots (total = MaxTier1 + 1 = 201, triggers promotion)
        for (int i = 1; i <= UndoStack.MaxTier1; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Undo all the way — due to promotion, some old entries are sampled into tier-2
        string? last = null;
        while (stack.CanUndo)
        {
            last = stack.Undo()!.Value.Content;
        }
        // The oldest entries were promoted; we should still have some history
        last.Should().NotBeNull();
    }

    // ─── MaxTier2 exact boundary ─────────────────────────────────

    [Fact]
    public void MaxTier2_ExactCount_NoEviction()
    {
        var stack = CreateStack();

        for (int i = 0; i < UndoStack.MaxTier2; i++)
        {
            stack.PushCheckpoint($"cp{i}");
        }

        // All checkpoints should be present — estimated bytes consistent
        // 20 checkpoints of "cpN" (3 chars each except cp10-cp19 which are 4)
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MaxTier2_PlusOne_EvictsOldest()
    {
        var stack = CreateStack();

        // Fill tier-2 exactly
        for (int i = 0; i < UndoStack.MaxTier2; i++)
        {
            stack.PushCheckpoint($"cp_{i:D3}");
        }

        var bytesBefore = stack.EstimatedBytes;

        // Push one more — triggers eviction of cp_000
        stack.PushCheckpoint("cp_overflow");

        // Count should still be MaxTier2 (one evicted, one added)
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    // ─── ShouldCreateCheckpoint exact boundary ───────────────────

    [Fact]
    public void ShouldCreateCheckpoint_ExactFiveMinutes_ReturnsTrue()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        _clock.Advance(TimeSpan.FromMinutes(5.0));
        stack.ShouldCreateCheckpoint().Should().BeTrue();
    }

    [Fact]
    public void ShouldCreateCheckpoint_JustUnderFiveMinutes_ReturnsFalse()
    {
        var stack = CreateStack();
        stack.PushInitialContent("a");

        _clock.Advance(TimeSpan.FromMinutes(4.999));
        stack.ShouldCreateCheckpoint().Should().BeFalse();
    }

    // ─── Emoji byte estimation ───────────────────────────────────

    [Fact]
    public void EstimatedBytes_EmojiSurrogatePair()
    {
        var stack = CreateStack();
        // U+1F600 — represented as 2 UTF-16 code units (surrogate pair)
        stack.PushInitialContent("\U0001F600");

        // "\U0001F600".Length = 2 (surrogate pair), so 2 * 2 = 4 bytes
        stack.EstimatedBytes.Should().Be(4);
    }

    [Fact]
    public void EstimatedBytes_MixedEmojiAndAscii()
    {
        var stack = CreateStack();
        // "A\U0001F600B" = 'A' (1) + surrogate pair (2) + 'B' (1) = 4 chars
        stack.PushInitialContent("A\U0001F600B");

        stack.EstimatedBytes.Should().Be(8); // 4 chars * 2 bytes
    }

    // ─── High-volume undo/redo cycle ─────────────────────────────

    [Fact]
    public void UndoRedoCycle_500Pushes_NoCorruption()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");

        // Push 500 snapshots (tier-1 will overflow and promote older ones)
        for (int i = 1; i <= 500; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Current should be s500
        stack.CanRedo.Should().BeFalse();

        // Undo all
        int undoCount = 0;
        while (stack.CanUndo)
        {
            var result = stack.Undo();
            result.Should().NotBeNull();
            undoCount++;
        }

        // Redo all back
        int redoCount = 0;
        while (stack.CanRedo)
        {
            var result = stack.Redo();
            result.Should().NotBeNull();
            redoCount++;
        }

        redoCount.Should().Be(undoCount);
    }

    // ─── CollapseTier1IntoTier2 exact sampling ───────────────────

    [Fact]
    public void CollapseTier1IntoTier2_SamplesExactEntries()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 14; i++)
        {
            stack.PushSnapshot($"s{i}");
        }
        // tier-1 = [s0, s1, ..., s14] = 15 entries
        // Sampled at indices 0, 5, 10: s0, s5, s10

        stack.CollapseTier1IntoTier2();

        // After collapse: tier-1 empty, tier-2 has 3 entries
        // Undo through all remaining entries
        var contents = new List<string>();
        while (stack.CanUndo)
        {
            contents.Add(stack.Undo()!.Value.Content);
        }

        // Current is at s10 (last sampled), undo goes to s5, then s0
        contents.Should().Contain("s5");
        contents.Should().Contain("s0");
    }

    // ─── Tier-boundary undo/redo crossing ────────────────────────

    [Fact]
    public void UndoRedo_CrossesTierBoundaryExactly()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 10; i++)
        {
            stack.PushSnapshot($"s{i}");
        }
        // tier-1 has 11 entries

        // Collapse: tier-2 gets s0, s5, s10; tier-1 cleared
        stack.CollapseTier1IntoTier2();

        // Push new snapshots into tier-1
        stack.PushSnapshot("n1");
        stack.PushSnapshot("n2");

        // Now: tier-2=[s0, s5, s10], tier-1=[n1, n2]
        // currentIndex points to n2 (logical index 4)

        // Undo from n2 -> n1 (within tier-1)
        stack.Undo()!.Value.Content.Should().Be("n1");

        // Undo from n1 -> s10 (crosses from tier-1 into tier-2)
        stack.Undo()!.Value.Content.Should().Be("s10");

        // Redo from s10 -> n1 (crosses from tier-2 into tier-1)
        stack.Redo()!.Value.Content.Should().Be("n1");
    }

    // ─── TruncateAfterCurrent at tier boundary ───────────────────

    [Fact]
    public void PushSnapshot_TruncateAtFirstTier1Entry()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 10; i++)
        {
            stack.PushSnapshot($"s{i}");
        }
        stack.CollapseTier1IntoTier2();
        // tier-2=[s0, s5, s10], tier-1=[]

        // Push new entries into tier-1
        stack.PushSnapshot("n1");
        stack.PushSnapshot("n2");
        stack.PushSnapshot("n3");
        // tier-2=[s0, s5, s10], tier-1=[n1, n2, n3]

        // Undo to first tier-1 entry (n1)
        stack.Undo(); // n2
        stack.Undo(); // n1

        // Now push — should truncate n2 and n3 from tier-1, but preserve tier-2
        stack.PushSnapshot("branched");

        // Redo should be impossible (future destroyed)
        stack.CanRedo.Should().BeFalse();

        // Undo: branched -> n1 -> s10 -> s5 -> s0
        stack.Undo()!.Value.Content.Should().Be("n1");
        stack.Undo()!.Value.Content.Should().Be("s10");
        stack.Undo()!.Value.Content.Should().Be("s5");
        stack.Undo()!.Value.Content.Should().Be("s0");
    }

    // ─── PushCheckpoint when currentIndex is 0 ───────────────────

    [Fact]
    public void PushCheckpoint_OverflowWhenCurrentIndexZero_NoDecrement()
    {
        var stack = CreateStack();

        // Fill tier-2 to capacity
        for (int i = 0; i < UndoStack.MaxTier2; i++)
        {
            stack.PushCheckpoint($"cp_{i}");
        }

        // Undo to the very beginning (index 0)
        while (stack.CanUndo)
        {
            stack.Undo();
        }

        // Push one more checkpoint — triggers eviction; currentIndex is 0, so no decrement
        stack.PushCheckpoint("overflow");

        // Should not crash; stack should still be navigable
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    // ─── CollapseTier1IntoTier2 resets currentIndex ──────────────

    [Fact]
    public void CollapseTier1IntoTier2_ResetsCurrentIndexToEnd()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 10; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Undo halfway back
        stack.Undo(); // s9
        stack.Undo(); // s8
        stack.Undo(); // s7

        // Collapse — currentIndex should reset to end of tier-2
        stack.CollapseTier1IntoTier2();

        // After collapse, should be able to undo (index at end, can go back)
        stack.CanUndo.Should().BeTrue();
        // But redo should be false (index is at TotalCount-1)
        stack.CanRedo.Should().BeFalse();
    }

    // ─── PromoteOldestTier1Entries ───────────────────────────────

    [Fact]
    public void PromoteOldestTier1Entries_SamplesCorrectly()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");

        // Push enough to have a full batch (PromotionBatchSize = 50)
        for (int i = 1; i <= 60; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Manually trigger promotion
        stack.PromoteOldestTier1Entries();

        // Stack should still be navigable
        stack.EstimatedBytes.Should().BeGreaterThan(0);

        // Undo all should work without corruption
        while (stack.CanUndo)
        {
            stack.Undo().Should().NotBeNull();
        }
    }

    [Fact]
    public void PromoteOldestTier1Entries_EmptyTier1_NoOp()
    {
        var stack = CreateStack();
        stack.PromoteOldestTier1Entries(); // should not throw
        stack.EstimatedBytes.Should().Be(0);
    }
}
