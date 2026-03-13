using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Additional MainWindowViewModel coverage: MoveTab edge cases, RemoveMultiple,
/// RestoreTabs, cleanup, drag state, panel toggles, editor state edge cases.
/// </summary>
public class ViewModelCoverage2Tests
{
    private static MainWindowViewModel CreateVm() => new("test-desktop");

    private static NoteTab MakeTab(long id, string? name = null, string content = "", bool pinned = false, int sort = 0)
        => new() { Id = id, Name = name, Content = content, Pinned = pinned, SortOrder = sort };

    // ─── MoveTab edge cases ─────────────────────────────────────────

    [Fact]
    public void MoveTab_NegativeOldIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));
        vm.MoveTab(-1, 0).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_NewIndexBeyondCount_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));
        vm.MoveTab(0, 5).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_SameIndex_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));
        vm.MoveTab(0, 0).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_ForwardAdjustment_SameAfterShift_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sort: 0));
        vm.Tabs.Add(MakeTab(2, sort: 1));
        // Move index 0 to index 1: after removal shift, effective target = 0 = old, so false
        vm.MoveTab(0, 1).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_PinnedToUnpinnedZone_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, sort: 0));
        vm.Tabs.Add(MakeTab(2, pinned: false, sort: 1));
        vm.MoveTab(0, 2).Should().BeFalse();
    }

    [Fact]
    public void MoveTab_ValidForwardMove_ReturnsTrue()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sort: 0));
        vm.Tabs.Add(MakeTab(2, sort: 1));
        vm.Tabs.Add(MakeTab(3, sort: 2));
        vm.MoveTab(0, 2).Should().BeTrue();
        vm.Tabs[0].Id.Should().Be(2);
    }

    // ─── RemoveMultiple ─────────────────────────────────────────────

    [Fact]
    public void RemoveMultiple_SkipsPinned()
    {
        var vm = CreateVm();
        var pinned = MakeTab(1, pinned: true);
        var unpinned = MakeTab(2);
        vm.Tabs.Add(pinned);
        vm.Tabs.Add(unpinned);

        var (removed, _, _) = vm.RemoveMultiple([pinned, unpinned]);
        removed.Should().ContainSingle().Which.Id.Should().Be(2);
        vm.Tabs.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void RemoveMultiple_ReturnsFocusTarget_WhenActiveRemoved()
    {
        var vm = CreateVm();
        var t1 = MakeTab(1, content: "a");
        var t2 = MakeTab(2, content: "b");
        var t3 = MakeTab(3, content: "c");
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.Tabs.Add(t3);
        vm.ActiveTab = t2;

        var (_, _, focus) = vm.RemoveMultiple([t2]);
        focus.Should().NotBeNull();
    }

    [Fact]
    public void RemoveMultiple_EmptyCandidates_ReturnsEmpty()
    {
        var vm = CreateVm();
        var (removed, indexes, focus) = vm.RemoveMultiple([]);
        removed.Should().BeEmpty();
        indexes.Should().BeEmpty();
        focus.Should().BeNull();
    }

    // ─── RestoreTabs ────────────────────────────────────────────────

    [Fact]
    public void RestoreTabs_InsertsAtOriginalPositions()
    {
        var vm = CreateVm();
        var t1 = MakeTab(1);
        var t2 = MakeTab(2);
        var t3 = MakeTab(3);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.Tabs.Add(t3);

        vm.Tabs.Remove(t2);
        vm.RestoreTabs([t2], [1]);

        vm.Tabs[1].Id.Should().Be(2);
    }

    // ─── GetCleanupCutoffDate ───────────────────────────────────────

    [Fact]
    public void GetCleanupCutoffDate_Hours()
    {
        var now = new DateTime(2025, 6, 15, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(6, 1, now);
        cutoff.Should().Be(now.AddHours(-6));
    }

    [Fact]
    public void GetCleanupCutoffDate_Weeks()
    {
        var now = new DateTime(2025, 6, 15, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(2, 2, now);
        cutoff.Should().Be(now.AddDays(-14));
    }

    [Fact]
    public void GetCleanupCutoffDate_Months()
    {
        var now = new DateTime(2025, 6, 15, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(3, 3, now);
        cutoff.Should().Be(now.AddDays(-90));
    }

    [Fact]
    public void GetCleanupCutoffDate_InvalidUnit_DefaultsToDays()
    {
        var now = new DateTime(2025, 6, 15, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(5, 99, now);
        cutoff.Should().Be(now.AddDays(-5));
    }

    [Fact]
    public void GetCleanupCutoffDate_ZeroAge_ReturnsNull()
    {
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(0, 0, DateTime.Now);
        cutoff.Should().BeNull();
    }

    // ─── GetCleanupCandidates ───────────────────────────────────────

    [Fact]
    public void GetCleanupCandidates_IncludesPinned_WhenFlagSet()
    {
        var vm = CreateVm();
        var old = new DateTime(2020, 1, 1);
        var t1 = MakeTab(1, pinned: true); t1.UpdatedAt = old;
        var t2 = MakeTab(2); t2.UpdatedAt = old;
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);

        var candidates = vm.GetCleanupCandidates(new DateTime(2024, 1, 1), includePinned: true);
        candidates.Should().HaveCount(2);
    }

    [Fact]
    public void GetCleanupCandidates_ExcludesPinned_WhenFlagFalse()
    {
        var vm = CreateVm();
        var old = new DateTime(2020, 1, 1);
        var tp = MakeTab(1, pinned: true); tp.UpdatedAt = old;
        var tu = MakeTab(2); tu.UpdatedAt = old;
        vm.Tabs.Add(tp);
        vm.Tabs.Add(tu);

        var candidates = vm.GetCleanupCandidates(new DateTime(2024, 1, 1), includePinned: false);
        candidates.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Fact]
    public void GetCleanupCandidates_ExcludesRecentTabs()
    {
        var vm = CreateVm();
        var recent = MakeTab(1); recent.UpdatedAt = DateTime.Now;
        vm.Tabs.Add(recent);
        var candidates = vm.GetCleanupCandidates(new DateTime(2020, 1, 1), false);
        candidates.Should().BeEmpty();
    }

    // ─── GetCleanupExcerpt ──────────────────────────────────────────

    [Fact]
    public void GetCleanupExcerpt_EmptyContent_ReturnsEmpty()
    {
        var tab = MakeTab(1, name: "Named");
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupExcerpt_NoName_ReturnsEmpty()
    {
        var tab = MakeTab(1, content: "some content");
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupExcerpt_NamedTab_ReturnsContentPreview()
    {
        var tab = MakeTab(1, name: "My Note", content: "Short content");
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().Be("Short content");
    }

    [Fact]
    public void GetCleanupExcerpt_LongContent_Truncates()
    {
        var tab = MakeTab(1, name: "Named", content: new string('x', 100));
        var excerpt = MainWindowViewModel.GetCleanupExcerpt(tab);
        excerpt.Should().HaveLength(53); // 50 + "..."
        excerpt.Should().EndWith("...");
    }

    [Fact]
    public void GetCleanupExcerpt_ReplacesNewlines()
    {
        var tab = MakeTab(1, name: "Named", content: "line1\nline2\r\nline3");
        var excerpt = MainWindowViewModel.GetCleanupExcerpt(tab);
        excerpt.Should().NotContain("\n");
        excerpt.Should().NotContain("\r");
    }

    // ─── Drag state ─────────────────────────────────────────────────

    [Fact]
    public void EvaluateDrag_UpdateTarget_WhenDifferentFromCurrent()
    {
        var vm = CreateVm();
        vm.IsDragOverlayActive = true;
        vm.DragFromDesktopGuid = "origin";
        vm.DragToDesktopGuid = "target-1";

        vm.EvaluateDrag("target-2").Should().Be(MainWindowViewModel.DragAction.UpdateTarget);
    }

    [Fact]
    public void BeginDrag_SetsState()
    {
        var vm = CreateVm();
        vm.BeginDrag("from-guid", "to-guid", "Desktop 2");

        vm.IsDragOverlayActive.Should().BeTrue();
        vm.DragFromDesktopGuid.Should().Be("from-guid");
        vm.DragToDesktopGuid.Should().Be("to-guid");
        vm.DragToDesktopName.Should().Be("Desktop 2");
    }

    [Fact]
    public void UpdateDragTarget_ChangesTarget()
    {
        var vm = CreateVm();
        vm.BeginDrag("from", "to-1", "Desk 1");
        vm.UpdateDragTarget("to-2", "Desk 2");

        vm.DragToDesktopGuid.Should().Be("to-2");
        vm.DragToDesktopName.Should().Be("Desk 2");
    }

    [Fact]
    public void ResetDragState_ClearsAll()
    {
        var vm = CreateVm();
        vm.BeginDrag("from", "to", "name");
        vm.ResetDragState();

        vm.IsDragOverlayActive.Should().BeFalse();
        vm.DragFromDesktopGuid.Should().BeNull();
        vm.DragToDesktopGuid.Should().BeNull();
        vm.DragToDesktopName.Should().BeNull();
    }

    [Fact]
    public void IsMisplacedOnDesktop_True_WhenGuidsDiffer()
    {
        var vm = CreateVm();
        vm.IsMisplacedOnDesktop("different-guid").Should().BeTrue();
    }

    [Fact]
    public void IsMisplacedOnDesktop_False_WhenGuidsMatch()
    {
        var vm = CreateVm();
        vm.IsMisplacedOnDesktop("test-desktop").Should().BeFalse();
    }

    // ─── SaveEditorStateToTab edge cases ────────────────────────────

    [Fact]
    public void SaveEditorStateToTab_NoActiveTab_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.SaveEditorStateToTab("content", 0, 0).Should().BeFalse();
    }

    [Fact]
    public void SaveEditorStateToTab_SameContent_ReturnsFalse()
    {
        var vm = CreateVm();
        var tab = MakeTab(1, content: "same");
        vm.Tabs.Add(tab);
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("same", 5, 10).Should().BeFalse();
        tab.CursorPosition.Should().Be(5);
        tab.EditorScrollOffset.Should().Be(10);
    }

    [Fact]
    public void SaveEditorStateToTab_DifferentContent_UpdatesTimestamp()
    {
        var vm = CreateVm();
        var tab = MakeTab(1, content: "old");
        tab.UpdatedAt = new DateTime(2020, 1, 1);
        vm.Tabs.Add(tab);
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("new", 0, 0).Should().BeTrue();
        tab.Content.Should().Be("new");
        tab.UpdatedAt.Should().BeAfter(new DateTime(2020, 1, 1));
    }

    // ─── GetDefaultFilename ─────────────────────────────────────────

    [Fact]
    public void GetDefaultFilename_EmptyTab_UsesDateFallback()
    {
        var tab = MakeTab(1);
        var filename = MainWindowViewModel.GetDefaultFilename(tab);
        filename.Should().StartWith("JoJot note ");
        filename.Should().EndWith(".txt");
    }

    [Fact]
    public void GetDefaultFilename_ContentOnly_UsesPreview()
    {
        var tab = MakeTab(1, content: "My important note");
        var filename = MainWindowViewModel.GetDefaultFilename(tab);
        filename.Should().Be("My important note.txt");
    }

    [Fact]
    public void GetDefaultFilename_LongContent_Truncates()
    {
        var tab = MakeTab(1, content: new string('A', 60));
        var filename = MainWindowViewModel.GetDefaultFilename(tab);
        filename.Should().Be(new string('A', 45) + ".txt");
    }

    // ─── SanitizeFilename ───────────────────────────────────────────

    [Fact]
    public void SanitizeFilename_ReplacesIllegalChars()
    {
        var result = MainWindowViewModel.SanitizeFilename("file<>name");
        result.Should().Be("file__name");
    }

    [Fact]
    public void SanitizeFilename_TrimsTrailingDots()
    {
        var result = MainWindowViewModel.SanitizeFilename("name...");
        result.Should().Be("name");
    }

    [Fact]
    public void SanitizeFilename_AllInvalid_ReturnsFallback()
    {
        var result = MainWindowViewModel.SanitizeFilename("...");
        result.Should().Be("JoJot note");
    }

    // ─── InsertNewTab ───────────────────────────────────────────────

    [Fact]
    public void InsertNewTab_BeyondCount_AppendsToEnd()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));
        var newTab = MakeTab(2);

        vm.InsertNewTab(newTab, 999);
        vm.Tabs.Should().HaveCount(2);
        vm.Tabs[1].Id.Should().Be(2);
    }

    // ─── GetNewTabPosition ──────────────────────────────────────────

    [Fact]
    public void GetNewTabPosition_EmptyTabs_ReturnsIndexZero()
    {
        var vm = CreateVm();
        var (placeholder, index, sort) = vm.GetNewTabPosition();
        placeholder.Should().BeNull();
        index.Should().Be(0);
    }

    [Fact]
    public void GetNewTabPosition_ReusesPlaceholder()
    {
        var vm = CreateVm();
        var placeholder = MakeTab(1); // IsPlaceholder = true (no name, empty content)
        vm.Tabs.Add(placeholder);

        var (existing, _, _) = vm.GetNewTabPosition();
        existing.Should().BeSameAs(placeholder);
    }

    // ─── ReorderAfterPinToggle ──────────────────────────────────────

    [Fact]
    public void ReorderAfterPinToggle_PinnedTabsFirst()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, sort: 0));
        vm.Tabs.Add(MakeTab(2, pinned: true, sort: 1));

        vm.ReorderAfterPinToggle();

        vm.Tabs[0].Pinned.Should().BeTrue();
        vm.Tabs[1].Pinned.Should().BeFalse();
    }

    // ─── GetClonePosition ───────────────────────────────────────────

    [Fact]
    public void GetClonePosition_InsertsAfterSource()
    {
        var vm = CreateVm();
        var t1 = MakeTab(1, sort: 0);
        var t2 = MakeTab(2, sort: 1);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);

        var (idx, sort) = vm.GetClonePosition(t1);
        idx.Should().Be(1);
        sort.Should().Be(1);
        t2.SortOrder.Should().Be(2); // shifted
    }

    // ─── Panel property changes ─────────────────────────────────────

    [Fact]
    public void IsRestoringContent_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.IsRestoringContent = true;
        raised.Should().Contain(nameof(MainWindowViewModel.IsRestoringContent));
    }

    [Fact]
    public void IsMisplaced_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.IsMisplaced = true;
        raised.Should().Contain(nameof(MainWindowViewModel.IsMisplaced));
    }
}
