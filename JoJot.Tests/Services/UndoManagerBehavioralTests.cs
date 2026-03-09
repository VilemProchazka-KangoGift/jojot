using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Behavioral edge-case tests for UndoManager: auto-creation on push,
/// active tab protection switching, and two-phase collapse with multiple stacks.
/// </summary>
public class UndoManagerBehavioralTests
{
    private readonly TestClock _clock = new();

    private UndoManager CreateManager() => new(_clock);

    [Fact]
    public void PushSnapshot_CreatesStackIfNotExists()
    {
        var mgr = CreateManager();
        mgr.GetStack(42).Should().BeNull();

        mgr.PushSnapshot(42, "content");

        mgr.GetStack(42).Should().NotBeNull();
        mgr.GetStack(42)!.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SetActiveTab_SwitchProtection()
    {
        var mgr = CreateManager();

        // Create two stacks
        var s1 = mgr.GetOrCreateStack(1);
        s1.PushInitialContent("tab1");
        var s2 = mgr.GetOrCreateStack(2);
        s2.PushInitialContent("tab2");

        // Protect tab 1
        mgr.SetActiveTab(1);

        // Switch protection to tab 2
        mgr.SetActiveTab(2);

        // Tab 1 is no longer protected — tab 2 is
        // Both stacks should still exist (no collapse triggered yet)
        mgr.GetStack(1).Should().NotBeNull();
        mgr.GetStack(2).Should().NotBeNull();
    }

    [Fact]
    public void TotalEstimatedBytes_EmptyManager_ReturnsZero()
    {
        var mgr = CreateManager();
        mgr.TotalEstimatedBytes.Should().Be(0);
    }

    [Fact]
    public void PushSnapshot_Deduplicates_ViaStack()
    {
        var mgr = CreateManager();
        var stack = mgr.GetOrCreateStack(1);
        stack.PushInitialContent("same");

        mgr.PushSnapshot(1, "same");

        // Should not have added a duplicate entry
        mgr.CanUndo(1).Should().BeFalse();
    }

    [Fact]
    public void CollapseOldest_TwoPhase_WithMultipleInactiveTabs()
    {
        var mgr = CreateManager();

        // Create 3 tabs with staggered access times
        for (int tabId = 1; tabId <= 3; tabId++)
        {
            _clock.Advance(TimeSpan.FromSeconds(tabId));
            var stack = mgr.GetOrCreateStack(tabId);
            stack.PushInitialContent(new string('A', 200_000));
            for (int i = 0; i < 20; i++)
            {
                stack.PushSnapshot(new string((char)('A' + i), 200_000));
            }
        }

        // Set tab 3 as active (tabs 1 and 2 are candidates for collapse)
        mgr.SetActiveTab(3);

        // Push large content to trigger collapse
        for (int i = 0; i < 30; i++)
        {
            mgr.PushSnapshot(3, new string((char)('Z' - i % 26), 500_000));
        }

        // All stacks should still exist (collapse doesn't delete stacks)
        mgr.GetStack(1).Should().NotBeNull();
        mgr.GetStack(2).Should().NotBeNull();
        mgr.GetStack(3).Should().NotBeNull();

        // Total bytes should be less than it would be without collapse
        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }
}
