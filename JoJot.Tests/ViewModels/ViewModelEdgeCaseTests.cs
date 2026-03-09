using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Edge case and boundary tests for MainWindowViewModel logical gaps.
/// </summary>
public class ViewModelEdgeCaseTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    private static NoteTab MakeTab(long id, bool pinned = false, int sortOrder = 0, string content = "")
        => new() { Id = id, DesktopGuid = "test-guid", Pinned = pinned, SortOrder = sortOrder, Content = content };

    // ─── FilteredTabs ──────────────────────────────────────────────

    [Fact]
    public void FilteredTabs_AllFilteredOut_ReturnsEmpty()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, content: "alpha"));
        vm.Tabs.Add(MakeTab(2, content: "beta"));
        vm.SearchText = "zzz_no_match";

        vm.FilteredTabs.Should().BeEmpty();
    }

    [Fact]
    public void FilteredTabs_TabRemovalDuringSearch_UpdatesFiltered()
    {
        var vm = CreateVm();
        var tab1 = MakeTab(1, content: "alpha");
        var tab2 = MakeTab(2, content: "alpha beta");
        vm.Tabs.Add(tab1);
        vm.Tabs.Add(tab2);
        vm.SearchText = "alpha";

        vm.FilteredTabs.Should().HaveCount(2);

        vm.Tabs.Remove(tab1);
        vm.FilteredTabs.Should().HaveCount(1);
    }

    // ─── MoveTab ───────────────────────────────────────────────────

    [Fact]
    public void MoveTab_SingleItem_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, content: "only"));

        vm.MoveTab(0, 1).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_PinBoundary_PinnedToUnpinned_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, content: "pin"));
        vm.Tabs.Add(MakeTab(2, pinned: false, content: "unpin"));

        // Try moving pinned tab to unpinned zone
        vm.MoveTab(0, 2).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_NegativeOldIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, content: "a"));

        vm.MoveTab(-1, 0).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_NegativeNewIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, content: "a"));

        vm.MoveTab(0, -1).Should().BeFalse();
    }

    // ─── RemoveMultiple non-contiguous ─────────────────────────────

    [Fact]
    public void RemoveMultiple_NonContiguous_RemovesCorrectTabs()
    {
        var vm = CreateVm();
        var tabs = Enumerable.Range(1, 6).Select(i => MakeTab(i, content: $"tab{i}")).ToArray();
        foreach (var t in tabs) vm.Tabs.Add(t);
        vm.ActiveTab = tabs[2]; // tab3

        var (removed, _, focus) = vm.RemoveMultiple([tabs[0], tabs[2], tabs[4]]);

        removed.Should().HaveCount(3);
        vm.Tabs.Should().HaveCount(3);
        vm.Tabs.Select(t => t.Id).Should().BeEquivalentTo([2, 4, 6]);
        focus.Should().NotBeNull(); // Active was removed
    }

    [Fact]
    public void RemoveMultiple_AllUnpinned_PinnedRemain()
    {
        var vm = CreateVm();
        var pinned = MakeTab(1, pinned: true, content: "pin");
        var u1 = MakeTab(2, content: "u1");
        var u2 = MakeTab(3, content: "u2");
        vm.Tabs.Add(pinned);
        vm.Tabs.Add(u1);
        vm.Tabs.Add(u2);
        vm.ActiveTab = u1;

        var (removed, _, _) = vm.RemoveMultiple([pinned, u1, u2]);

        removed.Should().HaveCount(2); // Pinned skipped
        vm.Tabs.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    // ─── RestoreTabs scattered indexes ─────────────────────────────

    [Fact]
    public void RestoreTabs_ScatteredIndexes_InsertedInOrder()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(99, content: "existing"));

        var t1 = MakeTab(1, content: "a");
        var t2 = MakeTab(2, content: "b");
        var t3 = MakeTab(3, content: "c");

        vm.RestoreTabs([t3, t1, t2], [10, 0, 2]);

        // Should sort by index: 0, 2, 10
        vm.Tabs[0].Id.Should().Be(1);   // index 0
        vm.Tabs.Should().HaveCount(4);
    }

    [Fact]
    public void RestoreTabs_AllBeyondEnd_AppendedAtEnd()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, content: "first"));

        var t2 = MakeTab(2, content: "b");
        var t3 = MakeTab(3, content: "c");

        vm.RestoreTabs([t2, t3], [100, 200]);

        vm.Tabs.Should().HaveCount(3);
        vm.Tabs[^2].Id.Should().Be(2);
        vm.Tabs[^1].Id.Should().Be(3);
    }

    // ─── GetCleanupCandidates exact boundary ───────────────────────

    [Fact]
    public void GetCleanupCandidates_ExactCutoff_NotIncluded()
    {
        var vm = CreateVm();
        var cutoff = new DateTime(2025, 6, 15, 12, 0, 0);
        var tab = MakeTab(1, content: "old");
        tab.UpdatedAt = cutoff; // Exactly at cutoff
        vm.Tabs.Add(tab);

        var candidates = vm.GetCleanupCandidates(cutoff, includePinned: false);
        candidates.Should().BeEmpty(); // < not <=
    }

    [Fact]
    public void GetCleanupCandidates_OneSecondBefore_Included()
    {
        var vm = CreateVm();
        var cutoff = new DateTime(2025, 6, 15, 12, 0, 0);
        var tab = MakeTab(1, content: "old");
        tab.UpdatedAt = cutoff.AddSeconds(-1);
        vm.Tabs.Add(tab);

        var candidates = vm.GetCleanupCandidates(cutoff, includePinned: false);
        candidates.Should().HaveCount(1);
    }

    // ─── SaveEditorStateToTab ──────────────────────────────────────

    [Fact]
    public void SaveEditorState_NullActiveTab_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.ActiveTab = null;

        vm.SaveEditorStateToTab("content", 0, 0).Should().BeFalse();
    }

    [Fact]
    public void SaveEditorState_NegativeCursor_Persists()
    {
        var vm = CreateVm();
        var tab = MakeTab(1, content: "hello");
        vm.Tabs.Add(tab);
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("hello", -5, 0);

        tab.CursorPosition.Should().Be(-5);
    }

    // ─── SanitizeFilename ──────────────────────────────────────────

    [Fact]
    public void SanitizeFilename_Unicode_Preserved()
    {
        MainWindowViewModel.SanitizeFilename("日本語テスト").Should().Be("日本語テスト");
    }

    [Fact]
    public void SanitizeFilename_AllIllegal_ReplacedWithUnderscore()
    {
        var result = MainWindowViewModel.SanitizeFilename("<>:\"|?*");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().Contain("_");
    }

    [Fact]
    public void SanitizeFilename_AllSpaces_FallsBackToDefault()
    {
        MainWindowViewModel.SanitizeFilename("   ").Should().Be("JoJot note");
    }

    [Fact]
    public void SanitizeFilename_TrailingDots_Trimmed()
    {
        MainWindowViewModel.SanitizeFilename("file...").Should().Be("file");
    }

    // ─── GetNewTabPosition only pinned tabs ────────────────────────

    [Fact]
    public void GetNewTabPosition_OnlyPinnedTabs_InsertsAtPinnedCount()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0, content: "pin1"));
        vm.Tabs.Add(MakeTab(2, pinned: true, sortOrder: 1, content: "pin2"));

        var (placeholder, insertIndex, sortOrder) = vm.GetNewTabPosition();

        placeholder.Should().BeNull();
        insertIndex.Should().Be(2); // After all pinned
        sortOrder.Should().Be(-1);  // DefaultIfEmpty(0).Min() - 1
    }

    // ─── ReorderAfterPinToggle alternating ─────────────────────────

    [Fact]
    public void ReorderAfterPinToggle_Alternating_SortsCorrectly()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sortOrder: 0, content: "p1"));
        vm.Tabs.Add(MakeTab(2, pinned: false, sortOrder: 1, content: "u1"));
        vm.Tabs.Add(MakeTab(3, pinned: true, sortOrder: 2, content: "p2"));
        vm.Tabs.Add(MakeTab(4, pinned: false, sortOrder: 3, content: "u2"));

        vm.ReorderAfterPinToggle();

        // Pinned first, then unpinned, each group sorted by original sort_order
        vm.Tabs[0].Pinned.Should().BeTrue();
        vm.Tabs[1].Pinned.Should().BeTrue();
        vm.Tabs[2].Pinned.Should().BeFalse();
        vm.Tabs[3].Pinned.Should().BeFalse();
    }

    // ─── ActiveTab set to tab not in collection ────────────────────

    [Fact]
    public void ActiveTab_SetToTabNotInCollection_Allowed()
    {
        var vm = CreateVm();
        var orphan = MakeTab(999, content: "orphan");

        vm.ActiveTab = orphan;

        vm.ActiveTab.Should().BeSameAs(orphan);
    }
}
