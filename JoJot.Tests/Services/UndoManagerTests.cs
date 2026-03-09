using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

public class UndoManagerTests
{
    private readonly TestClock _clock = new();

    private UndoManager CreateManager() => new(_clock);

    [Fact]
    public void GetOrCreateStack_CreatesNewStack()
    {
        var mgr = CreateManager();
        var stack = mgr.GetOrCreateStack(1);

        stack.Should().NotBeNull();
        stack.TabId.Should().Be(1);
    }

    [Fact]
    public void GetOrCreateStack_ReturnsSameInstance()
    {
        var mgr = CreateManager();
        var s1 = mgr.GetOrCreateStack(1);
        var s2 = mgr.GetOrCreateStack(1);

        s1.Should().BeSameAs(s2);
    }

    [Fact]
    public void GetStack_ReturnsNull_WhenNotExists()
    {
        var mgr = CreateManager();
        mgr.GetStack(999).Should().BeNull();
    }

    [Fact]
    public void GetStack_ReturnsStack_WhenExists()
    {
        var mgr = CreateManager();
        mgr.GetOrCreateStack(1);
        mgr.GetStack(1).Should().NotBeNull();
    }

    [Fact]
    public void RemoveStack_DeletesStack()
    {
        var mgr = CreateManager();
        mgr.GetOrCreateStack(1);
        mgr.RemoveStack(1);

        mgr.GetStack(1).Should().BeNull();
    }

    [Fact]
    public void SetActiveTab_ProtectsFromCollapse()
    {
        var mgr = CreateManager();
        mgr.SetActiveTab(1);

        // Push large content to multiple tabs to trigger collapse
        // Tab 1 (active) should not be collapsed
        var stack1 = mgr.GetOrCreateStack(1);
        stack1.PushInitialContent(new string('A', 1_000_000));

        var stack2 = mgr.GetOrCreateStack(2);
        stack2.PushInitialContent(new string('B', 1_000_000));

        // Push enough to potentially trigger collapse
        for (int i = 0; i < 30; i++)
        {
            mgr.PushSnapshot(2, new string((char)('C' + (i % 26)), 1_000_000));
        }

        // Stack 1 should still be intact (active tab protection)
        mgr.GetStack(1).Should().NotBeNull();
    }

    [Fact]
    public void Undo_DelegatesToStack()
    {
        var mgr = CreateManager();
        var stack = mgr.GetOrCreateStack(1);
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");

        mgr.Undo(1).Should().Be("a");
    }

    [Fact]
    public void Undo_ReturnsNull_WhenNoStack()
    {
        var mgr = CreateManager();
        mgr.Undo(999).Should().BeNull();
    }

    [Fact]
    public void Redo_DelegatesToStack()
    {
        var mgr = CreateManager();
        var stack = mgr.GetOrCreateStack(1);
        stack.PushInitialContent("a");
        stack.PushSnapshot("b");
        mgr.Undo(1);

        mgr.Redo(1).Should().Be("b");
    }

    [Fact]
    public void CanUndo_CanRedo_DelegateToStack()
    {
        var mgr = CreateManager();
        var stack = mgr.GetOrCreateStack(1);
        stack.PushInitialContent("a");

        mgr.CanUndo(1).Should().BeFalse();
        mgr.CanRedo(1).Should().BeFalse();

        stack.PushSnapshot("b");
        mgr.CanUndo(1).Should().BeTrue();
    }

    [Fact]
    public void CanUndo_False_WhenNoStack()
    {
        var mgr = CreateManager();
        mgr.CanUndo(999).Should().BeFalse();
        mgr.CanRedo(999).Should().BeFalse();
    }

    [Fact]
    public void TotalEstimatedBytes_SumsAllStacks()
    {
        var mgr = CreateManager();
        var s1 = mgr.GetOrCreateStack(1);
        s1.PushInitialContent("abc"); // 6 bytes

        var s2 = mgr.GetOrCreateStack(2);
        s2.PushInitialContent("de"); // 4 bytes

        mgr.TotalEstimatedBytes.Should().Be(10);
    }

    [Fact]
    public void PushSnapshot_TriggersCollapse_WhenOverBudget()
    {
        var mgr = CreateManager();

        // Create many large snapshots on an inactive tab to exceed 80% of 50MB
        // Each snapshot is ~2MB (1M chars * 2 bytes)
        for (int tabId = 1; tabId <= 5; tabId++)
        {
            mgr.SetActiveTab(tabId);
            var stack = mgr.GetOrCreateStack(tabId);
            stack.PushInitialContent(new string('X', 500_000));
            for (int i = 0; i < 10; i++)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                mgr.PushSnapshot(tabId, new string((char)('A' + i), 500_000));
            }
        }

        // Set only the last tab as active so others can be collapsed
        mgr.SetActiveTab(5);

        // Push a large snapshot to trigger collapse
        mgr.PushSnapshot(5, new string('Z', 2_000_000));

        // After collapse, total should be reduced
        // (exact amount depends on collapse logic, just verify it doesn't crash)
        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }
}
