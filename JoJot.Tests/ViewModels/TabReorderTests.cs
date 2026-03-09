using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class TabReorderTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    private static NoteTab MakeTab(long id, bool pinned = false, int sortOrder = 0, string content = "x")
        => new() { Id = id, DesktopGuid = "test-guid", Pinned = pinned, SortOrder = sortOrder, Content = content };

    // ─── Basic MoveTab ───────────────────────────────────────────────

    [Fact]
    public void MoveTab_ForwardMove_ReordersCorrectly()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, sortOrder: 2));

        var result = vm.MoveTab(0, 2); // Move first to after second

        result.Should().BeTrue();
        vm.Tabs[0].Id.Should().Be(2);
        vm.Tabs[1].Id.Should().Be(1);
        vm.Tabs[2].Id.Should().Be(3);
    }

    [Fact]
    public void MoveTab_BackwardMove_ReordersCorrectly()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, sortOrder: 2));

        var result = vm.MoveTab(2, 0); // Move last to first

        result.Should().BeTrue();
        vm.Tabs[0].Id.Should().Be(3);
        vm.Tabs[1].Id.Should().Be(1);
        vm.Tabs[2].Id.Should().Be(2);
    }

    [Fact]
    public void MoveTab_ReassignsSortOrders()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 10));
        vm.Tabs.Add(MakeTab(2, sortOrder: 20));
        vm.Tabs.Add(MakeTab(3, sortOrder: 30));

        vm.MoveTab(2, 0);

        vm.Tabs[0].SortOrder.Should().Be(0);
        vm.Tabs[1].SortOrder.Should().Be(1);
        vm.Tabs[2].SortOrder.Should().Be(2);
    }

    // ─── No-op cases ─────────────────────────────────────────────────

    [Fact]
    public void MoveTab_SamePosition_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0));

        var result = vm.MoveTab(0, 0);

        result.Should().BeFalse();
    }

    [Fact]
    public void MoveTab_InvalidOldIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));

        vm.MoveTab(-1, 0).Should().BeFalse();
        vm.MoveTab(5, 0).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_InvalidNewIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));

        vm.MoveTab(0, -1).Should().BeFalse();
        vm.MoveTab(0, 5).Should().BeFalse();
    }

    // ─── Pin-zone enforcement ────────────────────────────────────────

    [Fact]
    public void MoveTab_PinnedIntoPinnedZone_Allowed()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2));

        var result = vm.MoveTab(0, 2); // Move first pinned after second pinned

        result.Should().BeTrue();
        vm.Tabs[0].Id.Should().Be(2);
        vm.Tabs[1].Id.Should().Be(1);
    }

    [Fact]
    public void MoveTab_PinnedIntoUnpinnedZone_Rejected()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, pinned: false, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2));

        var result = vm.MoveTab(0, 3); // Try to move pinned after last unpinned

        result.Should().BeFalse();
        vm.Tabs[0].Id.Should().Be(1); // Unchanged
    }

    [Fact]
    public void MoveTab_UnpinnedIntoPinnedZone_Rejected()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2));

        var result = vm.MoveTab(2, 0); // Try to move unpinned before first pinned

        result.Should().BeFalse();
        vm.Tabs[2].Id.Should().Be(3); // Unchanged
    }

    [Fact]
    public void MoveTab_UnpinnedWithinUnpinnedZone_Allowed()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, pinned: false, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2));
        vm.Tabs.Add(MakeTab(4, pinned: false, sortOrder: 3));

        var result = vm.MoveTab(1, 3); // Move first unpinned after second unpinned

        result.Should().BeTrue();
        vm.Tabs[1].Id.Should().Be(3);
        vm.Tabs[2].Id.Should().Be(2);
    }

    // ─── Forward adjustment ──────────────────────────────────────────

    [Fact]
    public void MoveTab_ForwardAdjustment_CompensatesForRemoval()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1));
        vm.Tabs.Add(MakeTab(3, sortOrder: 2));

        // Moving index 0 to index 3 (after last) should place it at end
        vm.MoveTab(0, 3);

        vm.Tabs[2].Id.Should().Be(1); // Moved to end
    }
}
