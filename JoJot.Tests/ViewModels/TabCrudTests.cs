using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class TabCrudTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    private static NoteTab MakeTab(long id, bool pinned = false, int sortOrder = 0, string content = "")
        => new() { Id = id, DesktopGuid = "test-guid", Pinned = pinned, SortOrder = sortOrder, Content = content };

    // ─── GetNewTabPosition ───────────────────────────────────────────

    [Fact]
    public void GetNewTabPosition_EmptyCollection_ReturnsIndex0()
    {
        var vm = CreateVm();

        var (placeholder, insertIndex, sortOrder) = vm.GetNewTabPosition();

        placeholder.Should().BeNull();
        insertIndex.Should().Be(0);
        sortOrder.Should().Be(-1); // DefaultIfEmpty(0).Min() - 1
    }

    [Fact]
    public void GetNewTabPosition_WithPinnedTabs_InsertsAfterPinned()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0, content: "a"));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 1, content: "b"));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2, content: "c"));

        var (placeholder, insertIndex, _) = vm.GetNewTabPosition();

        placeholder.Should().BeNull();
        insertIndex.Should().Be(2); // After 2 pinned tabs
    }

    [Fact]
    public void GetNewTabPosition_SortOrderIsMinUnpinnedMinus1()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: false, sortOrder: 5, content: "a"));
        vm.Tabs.Add(MakeTab(2, pinned: false, sortOrder: 10, content: "b"));

        var (_, _, sortOrder) = vm.GetNewTabPosition();

        sortOrder.Should().Be(4); // min(5, 10) - 1
    }

    [Fact]
    public void GetNewTabPosition_ReusesPlaceholder()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0));
        var placeholder = MakeTab(2, pinned: false, sortOrder: 1); // empty = placeholder
        vm.Tabs.Add(placeholder);

        var (existing, _, _) = vm.GetNewTabPosition();

        existing.Should().BeSameAs(placeholder);
    }

    [Fact]
    public void GetNewTabPosition_DoesNotReusePlaceholder_IfHasContent()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: false, sortOrder: 0, content: "stuff"));

        var (existing, _, _) = vm.GetNewTabPosition();

        existing.Should().BeNull();
    }

    // ─── InsertNewTab ────────────────────────────────────────────────

    [Fact]
    public void InsertNewTab_InsertsAtCorrectIndex()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1));

        var newTab = MakeTab(3, sortOrder: -1);
        vm.InsertNewTab(newTab, 1);

        vm.Tabs[1].Id.Should().Be(3);
        vm.Tabs.Should().HaveCount(3);
    }

    [Fact]
    public void InsertNewTab_AppendsWhenIndexBeyondEnd()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));

        var newTab = MakeTab(2);
        vm.InsertNewTab(newTab, 99);

        vm.Tabs.Should().HaveCount(2);
        vm.Tabs[^1].Id.Should().Be(2);
    }

    // ─── RemoveTab ───────────────────────────────────────────────────

    [Fact]
    public void RemoveTab_RemovesFromCollection()
    {
        var vm = CreateVm();
        var tab = MakeTab(1);
        vm.Tabs.Add(tab);
        vm.ActiveTab = tab;

        vm.RemoveTab(tab);

        vm.Tabs.Should().BeEmpty();
    }

    [Fact]
    public void RemoveTab_ReturnsFocusTarget_WhenActiveDeleted()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, sortOrder: 0, content: "a");
        var tab2 = MakeTab(2, sortOrder: 1, content: "b");
        var tab3 = MakeTab(3, sortOrder: 2, content: "c");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.Tabs.Add(tab3);
        vm.ActiveTab = tab2;

        var target = vm.RemoveTab(tab2);

        target.Should().BeSameAs(tab3); // Next tab after deleted position
    }

    [Fact]
    public void RemoveTab_ReturnsNull_WhenNotActive()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, content: "a");
        var tab2 = MakeTab(2, content: "b");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.ActiveTab = tab1;

        var target = vm.RemoveTab(tab2);

        target.Should().BeNull(); // No focus change needed
    }

    [Fact]
    public void RemoveTab_FocusCascade_LastTab_FallsBack()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, sortOrder: 0, content: "a");
        var tab2 = MakeTab(2, sortOrder: 1, content: "b");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.ActiveTab = tab2;

        var target = vm.RemoveTab(tab2);

        target.Should().BeSameAs(tab1); // Falls back to last visible
    }

    // ─── RemoveMultiple ──────────────────────────────────────────────

    [Fact]
    public void RemoveMultiple_SkipsPinnedTabs()
    {
        var vm = CreateVm();
        var pinned = MakeTab(1, pinned: true, content: "pin");
        var unpinned = MakeTab(2, pinned: false, content: "free");
        vm.Tabs.Add(pinned);
        vm.Tabs.Add(unpinned);

        var (removed, _, _) = vm.RemoveMultiple([pinned, unpinned]);

        removed.Should().HaveCount(1);
        removed[0].Id.Should().Be(2);
        vm.Tabs.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void RemoveMultiple_ReturnsFocusTarget_WhenActiveDeleted()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, sortOrder: 0, content: "a");
        var tab2 = MakeTab(2, sortOrder: 1, content: "b");
        var tab3 = MakeTab(3, sortOrder: 2, content: "c");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.Tabs.Add(tab3);
        vm.ActiveTab = tab2;

        var (_, _, focusTarget) = vm.RemoveMultiple([tab1, tab2]);

        focusTarget.Should().BeSameAs(tab3);
    }

    [Fact]
    public void RemoveMultiple_ReturnsNullFocus_WhenActiveNotDeleted()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, content: "a");
        var tab2 = MakeTab(2, content: "b");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.ActiveTab = tab1;

        var (_, _, focusTarget) = vm.RemoveMultiple([tab2]);

        focusTarget.Should().BeNull();
    }

    [Fact]
    public void RemoveMultiple_EmptyWhenAllPinned()
    {
        var vm = CreateVm();
        var tab = MakeTab(1, pinned: true, content: "pin");
        vm.Tabs.Add(tab);

        var (removed, _, _) = vm.RemoveMultiple([tab]);

        removed.Should().BeEmpty();
    }

    // ─── RestoreTabs ─────────────────────────────────────────────────

    [Fact]
    public void RestoreTabs_InsertsAtOriginalPositions()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, content: "a");
        var tab3 = MakeTab(3, content: "c");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab3);

        var tab2 = MakeTab(2, content: "b");
        vm.RestoreTabs([tab2], [1]);

        vm.Tabs.Should().HaveCount(3);
        vm.Tabs[1].Id.Should().Be(2);
    }

    [Fact]
    public void RestoreTabs_ClampsIndex_WhenBeyondEnd()
    {
        var vm = CreateVm();
        var tab = MakeTab(1, content: "a");
        vm.RestoreTabs([tab], [99]);

        vm.Tabs.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void RestoreTabs_MultipleTabs_InAscendingOrder()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, content: "a");
        var tab2 = MakeTab(2, content: "b");

        vm.RestoreTabs([tab2, tab1], [1, 0]); // Reversed order — should sort by index

        vm.Tabs[0].Id.Should().Be(1);
        vm.Tabs[1].Id.Should().Be(2);
    }

    // ─── GetFocusCascadeTarget ───────────────────────────────────────

    [Fact]
    public void FocusCascade_SelectsNextTab()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0, content: "a"));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1, content: "b"));
        vm.Tabs.Add(MakeTab(3, sortOrder: 2, content: "c"));

        var target = vm.GetFocusCascadeTarget(1); // Deleted index 1

        target!.Id.Should().Be(2); // Tab at index 1 after removal
    }

    [Fact]
    public void FocusCascade_FallsToLast_WhenDeletedAtEnd()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0, content: "a"));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1, content: "b"));

        var target = vm.GetFocusCascadeTarget(2); // Beyond last index

        target!.Id.Should().Be(2); // Last tab
    }

    [Fact]
    public void FocusCascade_ReturnsNull_WhenEmpty()
    {
        var vm = CreateVm();

        var target = vm.GetFocusCascadeTarget(0);

        target.Should().BeNull();
    }

    [Fact]
    public void FocusCascade_RespectsSearchFilter()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sortOrder: 0, content: "alpha"));
        vm.Tabs.Add(MakeTab(2, sortOrder: 1, content: "beta"));
        vm.Tabs.Add(MakeTab(3, sortOrder: 2, content: "alpha gamma"));
        vm.SearchText = "alpha";

        var target = vm.GetFocusCascadeTarget(0);

        target!.Id.Should().Be(1); // First visible match
    }

    // ─── ReorderAfterPinToggle ───────────────────────────────────────

    [Fact]
    public void ReorderAfterPinToggle_PinnedSortToTop()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: false, sortOrder: 0, content: "a"));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 1, content: "b"));
        vm.Tabs.Add(MakeTab(3, pinned: false, sortOrder: 2, content: "c"));

        vm.ReorderAfterPinToggle();

        vm.Tabs[0].Id.Should().Be(2); // Pinned first
        vm.Tabs[1].Id.Should().Be(1);
        vm.Tabs[2].Id.Should().Be(3);
    }

    [Fact]
    public void ReorderAfterPinToggle_ReassignsSortOrders()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: false, sortOrder: 5, content: "a"));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 10, content: "b"));

        vm.ReorderAfterPinToggle();

        vm.Tabs[0].SortOrder.Should().Be(0);
        vm.Tabs[1].SortOrder.Should().Be(1);
    }

    // ─── GetClonePosition ────────────────────────────────────────────

    [Fact]
    public void GetClonePosition_InsertsAfterSource()
    {
        var vm = CreateVm();
        var source = MakeTab(1, sortOrder: 0, content: "a");
        vm.Tabs.Add(source);
        vm.Tabs.Add(MakeTab(2, sortOrder: 1, content: "b"));

        var (insertIndex, sortOrder) = vm.GetClonePosition(source);

        insertIndex.Should().Be(1);
        sortOrder.Should().Be(1);
    }

    [Fact]
    public void GetClonePosition_ShiftsSubsequentSortOrders()
    {
        var vm = CreateVm();
        var source = MakeTab(1, sortOrder: 0, content: "a");
        var next = MakeTab(2, sortOrder: 1, content: "b");
        vm.Tabs.Add(source);
        vm.Tabs.Add(next);

        vm.GetClonePosition(source);

        next.SortOrder.Should().Be(2); // Shifted from 1 to 2
    }

    [Fact]
    public void GetClonePosition_AtEnd_AppendsCorrectly()
    {
        var vm = CreateVm();
        var source = MakeTab(1, sortOrder: 0, content: "a");
        vm.Tabs.Add(source);

        var (insertIndex, sortOrder) = vm.GetClonePosition(source);

        insertIndex.Should().Be(1); // After only tab
        sortOrder.Should().Be(1);
    }
}
