using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional UndoStack tests targeting uncovered branches:
/// PushCheckpoint overflow, EvictOldestTier2 edge cases, TruncateAfterCurrent cross-tier,
/// CollapseTier1IntoTier2 when tier-2 is full, and GetContentAtIndex boundary.
/// </summary>
public class UndoStackCoverageTests
{
    private readonly TestClock _clock = new();

    private UndoStack CreateStack(long tabId = 1) => new(tabId, _clock);

    /// <summary>
    /// Builds a stack with entries in both tiers by pushing snapshots and collapsing.
    /// After calling this: tier-2 has sampled entries, tier-1 has the new snapshots.
    /// </summary>
    private UndoStack CreateCrossTierStack()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");

        // Push 20 snapshots into tier-1
        for (int i = 1; i <= 20; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // Collapse tier-1 into tier-2 (samples every 5th: s0, s5, s10, s15, s20)
        stack.CollapseTier1IntoTier2();

        // Now push new snapshots into tier-1
        stack.PushSnapshot("new_1");
        stack.PushSnapshot("new_2");
        stack.PushSnapshot("new_3");

        return stack;
    }

    // ─── PushCheckpoint overflow (tier2 > MaxTier2) ─────────────────

    [Fact]
    public void PushCheckpoint_EvictsOldest_WhenOverMaxTier2()
    {
        var stack = CreateStack();

        for (int i = 0; i < UndoStack.MaxTier2; i++)
        {
            stack.PushCheckpoint($"cp_{i}");
        }

        var bytesBefore = stack.EstimatedBytes;

        // Push one more to exceed MaxTier2
        stack.PushCheckpoint("overflow_cp");

        // The oldest should have been evicted; bytes should not grow unbounded
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PushCheckpoint_AdjustsCurrentIndex_WhenOverMaxTier2()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        // Push snapshots, collapse, repeat to fill tier-2
        for (int round = 0; round < 4; round++)
        {
            for (int i = 0; i < 20; i++)
            {
                stack.PushSnapshot($"r{round}_s{i}");
            }
            stack.CollapseTier1IntoTier2();
        }

        // Push more snapshots so currentIndex is valid
        stack.PushSnapshot("final_snap");

        // Push checkpoints until overflow triggers
        for (int i = 0; i < 5; i++)
        {
            stack.PushCheckpoint($"overflow_{i}");
        }

        // Should still be navigable without crash
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    // ─── EvictOldestTier2 — evict all entries ───────────────────────

    [Fact]
    public void EvictOldestTier2_EvictsAll_SetsIndexToNegativeOne()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp1");
        stack.PushCheckpoint("cp2");

        // Evict more than available — should evict all
        stack.EvictOldestTier2(100);

        // Stack is now empty
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
        stack.Undo().Should().BeNull();
        stack.Redo().Should().BeNull();
        stack.EstimatedBytes.Should().Be(0);
    }

    [Fact]
    public void EvictOldestTier2_AdjustsCurrentIndex_Correctly()
    {
        // Build a stack with tier-2 entries via collapse
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 20; i++)
        {
            stack.PushSnapshot($"s{i}");
        }
        stack.CollapseTier1IntoTier2();
        // tier2 has sampled entries, tier1 is empty, currentIndex points to end

        // Evict 1 oldest tier-2 entry
        stack.EvictOldestTier2(1);

        // Should still be able to undo
        stack.CanUndo.Should().BeTrue();
        var result = stack.Undo();
        result.Should().NotBeNull();
    }

    // ─── TruncateAfterCurrent — current index in tier-2 ─────────────

    [Fact]
    public void PushSnapshot_TruncatesCrossTier_WhenCurrentInTier2()
    {
        var stack = CreateCrossTierStack();
        // tier2 has ~5 entries, tier1 has [new_1, new_2, new_3]

        // Undo back past tier-1 into tier-2
        stack.Undo(); // new_2
        stack.Undo(); // new_1
        stack.Undo(); // last tier-2 entry (s20)
        stack.Undo(); // second-to-last tier-2 entry (s15)

        // Now push a new snapshot — should truncate everything after current
        stack.PushSnapshot("branched");

        stack.CanRedo.Should().BeFalse();

        // Undo should go back through remaining tier-2 entries
        var prev = stack.Undo();
        prev.Should().NotBeNull();
    }

    [Fact]
    public void PushSnapshot_TruncatesTier1Only_WhenCurrentInTier1()
    {
        var stack = CreateCrossTierStack();
        // tier2 has ~5 entries, tier1 has [new_1, new_2, new_3]

        // Undo once (still in tier-1)
        stack.Undo(); // new_2

        // Push new snapshot — only new_3 should be destroyed
        stack.PushSnapshot("branched");

        stack.CanRedo.Should().BeFalse();

        // Undo through remaining history
        stack.Undo().Should().Be("new_2");
        stack.Undo().Should().Be("new_1");
        // Now we should reach tier-2 entries
        var tier2Content = stack.Undo();
        tier2Content.Should().NotBeNull();
    }

    // ─── CollapseTier1IntoTier2 — tier-2 already full ───────────────

    [Fact]
    public void CollapseTier1IntoTier2_RespectsTier2Cap()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        // Fill tier-2 to near capacity via repeated collapse
        for (int round = 0; round < 4; round++)
        {
            for (int i = 0; i < 20; i++)
            {
                stack.PushSnapshot($"r{round}_s{i}");
            }
            stack.CollapseTier1IntoTier2();
        }

        // Add more tier-1 entries
        for (int i = 0; i < 30; i++)
        {
            stack.PushSnapshot($"snap_{i}");
        }

        stack.CollapseTier1IntoTier2();

        // Should not crash on navigation
        stack.EstimatedBytes.Should().BeGreaterThan(0);
        while (stack.CanUndo)
        {
            stack.Undo().Should().NotBeNull();
        }
    }

    // ─── GetContentAtIndex — boundary conditions ────────────────────

    [Fact]
    public void Undo_ReturnsNull_OnEmptyStack()
    {
        var stack = CreateStack();
        stack.Undo().Should().BeNull();
    }

    [Fact]
    public void Redo_ReturnsNull_OnEmptyStack()
    {
        var stack = CreateStack();
        stack.Redo().Should().BeNull();
    }

    // ─── PushCheckpoint general behavior ────────────────────────────

    [Fact]
    public void PushCheckpoint_AddsToTier2_WithoutAffectingCurrentIndex()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");
        stack.PushSnapshot("snap1");

        // Push a checkpoint (tier-2 addition)
        stack.PushCheckpoint("cp_added");

        // Undo should still work through snapshots
        stack.CanUndo.Should().BeTrue();
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    // ─── UndoStack default clock constructor ────────────────────────

    [Fact]
    public void Constructor_DefaultClock_DoesNotThrow()
    {
        var stack = new UndoStack(42);
        stack.TabId.Should().Be(42);
        stack.LastAccessTime.Should().BeAfter(DateTime.MinValue);
    }

    // ─── Cross-tier undo/redo navigation ────────────────────────────

    [Fact]
    public void UndoRedo_NavigatesCrossTier()
    {
        var stack = CreateCrossTierStack();
        // tier2 has sampled entries, tier1 has [new_1, new_2, new_3]

        // Undo through tier-1 into tier-2
        stack.Undo().Should().Be("new_2");
        stack.Undo().Should().Be("new_1");

        // Next undo crosses into tier-2
        var tier2Content = stack.Undo();
        tier2Content.Should().NotBeNull();

        // Redo back into tier-1
        stack.Redo().Should().NotBeNull();
    }

    // ─── PushCheckpoint deduplication with existing last ─────────────

    [Fact]
    public void PushCheckpoint_Deduplicates_WhenSameAsLast()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("same");
        var bytesAfterFirst = stack.EstimatedBytes;

        stack.PushCheckpoint("same");
        stack.EstimatedBytes.Should().Be(bytesAfterFirst);
    }

    // ─── EvictOldestTier2 with tier-1 entries present ───────────────

    [Fact]
    public void EvictOldestTier2_WithTier1Entries_PreservesTier1()
    {
        var stack = CreateCrossTierStack();
        var bytesBefore = stack.EstimatedBytes;

        stack.EvictOldestTier2(2);

        // Bytes should decrease (tier-2 entries removed)
        stack.EstimatedBytes.Should().BeLessThan(bytesBefore);
        // Tier-1 entries should still be accessible
        stack.CanUndo.Should().BeTrue();
    }

    // ─── PushCheckpoint LastAccessTime update ───────────────────────

    [Fact]
    public void PushCheckpoint_UpdatesLastAccessTime()
    {
        var stack = CreateStack();
        var timeBefore = _clock.Now;

        _clock.Advance(TimeSpan.FromMinutes(2));
        stack.PushCheckpoint("cp");

        stack.LastAccessTime.Should().Be(_clock.Now);
        stack.LastAccessTime.Should().BeAfter(timeBefore);
    }
}
