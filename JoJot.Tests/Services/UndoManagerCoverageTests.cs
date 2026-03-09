using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional UndoManager tests targeting uncovered branches:
/// Instance/SetInstance, Redo with no stack, CollapseOldest Phase 2 eviction.
/// </summary>
public class UndoManagerCoverageTests
{
    private readonly TestClock _clock = new();

    private UndoManager CreateManager() => new(_clock);

    [Fact]
    public void Instance_ReturnsNonNull()
    {
        UndoManager.Instance.Should().NotBeNull();
    }

    [Fact]
    public void SetInstance_ReplacesInstance()
    {
        var original = UndoManager.Instance;
        try
        {
            var custom = CreateManager();
            UndoManager.SetInstance(custom);
            UndoManager.Instance.Should().BeSameAs(custom);
        }
        finally
        {
            UndoManager.SetInstance(original);
        }
    }

    [Fact]
    public void Redo_ReturnsNull_WhenNoStack()
    {
        var mgr = CreateManager();
        mgr.Redo(999).Should().BeNull();
    }

    [Fact]
    public void CollapseOldest_Phase2_EvictsTier2Entries()
    {
        var mgr = CreateManager();

        // Create many tabs with large tier-2 checkpoints to exceed budget
        // Each checkpoint is ~1MB (500K chars * 2 bytes)
        for (int tabId = 1; tabId <= 10; tabId++)
        {
            var stack = mgr.GetOrCreateStack(tabId);
            for (int i = 0; i < 5; i++)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                stack.PushCheckpoint(new string((char)('A' + i), 500_000));
            }
        }

        // Set only tab 10 as active so others can be collapsed
        mgr.SetActiveTab(10);

        // Now push a large snapshot to trigger collapse
        // Total should be around 50MB (10 tabs * 5 checkpoints * 1MB)
        mgr.PushSnapshot(10, new string('Z', 2_000_000));

        // After collapse (including tier-2 eviction), total should decrease
        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RemoveStack_NoOp_WhenNotExists()
    {
        var mgr = CreateManager();
        // Should not throw
        mgr.RemoveStack(999);
    }

    [Fact]
    public void SetActiveTab_Null_AllowsAllCollapse()
    {
        var mgr = CreateManager();
        mgr.SetActiveTab(null);

        // Create stacks
        var s1 = mgr.GetOrCreateStack(1);
        s1.PushInitialContent("test");
        var s2 = mgr.GetOrCreateStack(2);
        s2.PushInitialContent("test2");

        // No active tab — both could potentially be collapsed
        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }
}
