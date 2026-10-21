using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Pure tab-load helpers: in-memory startup cleanup of empty notes and the merge-preserving reload.
/// </summary>
public class TabLoadTests
{
    private static NoteTab MakeTab(long id, string content = "x", bool pinned = false, int sortOrder = 0)
        => new() { Id = id, DesktopGuid = "test-guid", Content = content, Pinned = pinned, SortOrder = sortOrder };

    // ─── SplitEmptyStartupNotes ──────────────────────────────────────

    [Fact]
    public void SplitEmpty_DropsEmptyAndWhitespaceUnpinned_KeepsOrder()
    {
        var a = MakeTab(1, "keep");
        var b = MakeTab(2, "");
        var c = MakeTab(3, "  \r\n\t");
        var d = MakeTab(4, "also keep");

        var (keep, empty) = MainWindowViewModel.SplitEmptyStartupNotes([a, b, c, d]);

        keep.Should().Equal(a, d);
        empty.Should().Equal(b, c);
    }

    [Fact]
    public void SplitEmpty_PinnedEmptyNotes_AreKept()
    {
        var pinnedEmpty = MakeTab(1, "", pinned: true);
        var unpinnedEmpty = MakeTab(2, "");

        var (keep, empty) = MainWindowViewModel.SplitEmptyStartupNotes([pinnedEmpty, unpinnedEmpty]);

        keep.Should().Equal(pinnedEmpty);
        empty.Should().Equal(unpinnedEmpty);
    }

    [Fact]
    public void SplitEmpty_AllEmpty_KeepsFirstOne()
    {
        var a = MakeTab(1, "");
        var b = MakeTab(2, " ");

        var (keep, empty) = MainWindowViewModel.SplitEmptyStartupNotes([a, b]);

        keep.Should().Equal(a);
        empty.Should().Equal(b);
    }

    [Fact]
    public void SplitEmpty_NoNotes_ReturnsEmptyLists()
    {
        var (keep, empty) = MainWindowViewModel.SplitEmptyStartupNotes([]);

        keep.Should().BeEmpty();
        empty.Should().BeEmpty();
    }

    // ─── MergeReloadedTabs ───────────────────────────────────────────

    [Fact]
    public void Merge_ReusesInMemoryObject_ForExistingRow_AndAddsNewRows()
    {
        var live = MakeTab(1, "typed but not yet saved");
        var dbCopy = MakeTab(1, "stale");
        var migratedIn = MakeTab(2, "from another desktop", sortOrder: 1);

        var merged = MainWindowViewModel.MergeReloadedTabs([live], [dbCopy, migratedIn], new HashSet<long>());

        merged.Should().HaveCount(2);
        merged[0].Should().BeSameAs(live);
        merged[0].Content.Should().Be("typed but not yet saved");
        merged[1].Should().BeSameAs(migratedIn);
    }

    [Fact]
    public void Merge_TakesPinnedAndSortOrderFromDatabase()
    {
        var live = MakeTab(1, pinned: false, sortOrder: 5);
        var dbCopy = MakeTab(1, pinned: true, sortOrder: 0);

        var merged = MainWindowViewModel.MergeReloadedTabs([live], [dbCopy], new HashSet<long>());

        merged.Should().ContainSingle().Which.Should().BeSameAs(live);
        live.Pinned.Should().BeTrue();
        live.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Merge_DropsRowsGoneFromDatabase_AndSkipsExcludedIds()
    {
        var gone = MakeTab(1);
        var stays = MakeTab(2);
        var pendingDelete = MakeTab(3);

        var merged = MainWindowViewModel.MergeReloadedTabs(
            [gone, stays], [MakeTab(2), pendingDelete], new HashSet<long> { 3 });

        merged.Should().Equal(stays);
    }

    [Fact]
    public void Merge_KeepsUnpersistedTabs_InTheirZone()
    {
        var pinnedRow = MakeTab(1, pinned: true);
        var unpinnedRow = MakeTab(2);
        var unsavedPinned = MakeTab(-1, pinned: true);
        var unsavedUnpinned = MakeTab(-2);

        var merged = MainWindowViewModel.MergeReloadedTabs(
            [unsavedPinned, unsavedUnpinned], [pinnedRow, unpinnedRow], new HashSet<long>());

        merged.Should().Equal(pinnedRow, unsavedPinned, unpinnedRow, unsavedUnpinned);
    }
}
