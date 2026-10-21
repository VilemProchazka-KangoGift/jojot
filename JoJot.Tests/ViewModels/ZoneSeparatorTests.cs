using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// <see cref="MainWindowViewModel.UpdateZoneSeparators"/>: the pinned/unpinned divider flag is
/// recomputed over the collection without a view refresh, writing only the flags that change.
/// </summary>
public class ZoneSeparatorTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    private static NoteTab MakeTab(long id, bool pinned = false, string content = "")
        => new() { Id = id, DesktopGuid = "test-guid", Pinned = pinned, Content = content };

    private static bool[] Flags(MainWindowViewModel vm) =>
        vm.Tabs.Select(t => t.ShowUnpinnedSeparator).ToArray();

    [Fact]
    public void NoPinnedTabs_NoSeparator()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1));
        vm.Tabs.Add(MakeTab(2));

        vm.UpdateZoneSeparators().Should().Be(0);

        Flags(vm).Should().Equal(false, false);
    }

    [Fact]
    public void OnlyPinnedTabs_NoSeparator()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true));
        vm.Tabs.Add(MakeTab(2, pinned: true));

        vm.UpdateZoneSeparators();

        Flags(vm).Should().Equal(false, false);
    }

    [Fact]
    public void PinnedAndUnpinned_FlagsOnlyFirstUnpinned()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true));
        vm.Tabs.Add(MakeTab(2));
        vm.Tabs.Add(MakeTab(3));

        vm.UpdateZoneSeparators().Should().Be(1);

        Flags(vm).Should().Equal(false, true, false);
    }

    [Fact]
    public void SecondCall_ChangesNothing_AndRaisesNoPropertyChanged()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true));
        vm.Tabs.Add(MakeTab(2));
        vm.UpdateZoneSeparators();

        var raised = new List<string?>();
        foreach (var tab in vm.Tabs)
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.UpdateZoneSeparators().Should().Be(0);

        raised.Should().BeEmpty("unchanged flags must not be re-written (no binding churn)");
    }

    [Fact]
    public void InsertBeforeFirstUnpinned_MovesFlagToNewTab()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true));
        vm.Tabs.Add(MakeTab(2));
        vm.UpdateZoneSeparators();

        var newTab = MakeTab(3);
        vm.InsertNewTab(newTab, 1); // new tabs go right after the pinned zone

        vm.UpdateZoneSeparators().Should().Be(2, "the old first-unpinned loses the flag, the new one gains it");

        newTab.ShowUnpinnedSeparator.Should().BeTrue();
        vm.Tabs[2].ShowUnpinnedSeparator.Should().BeFalse();
    }

    [Fact]
    public void Search_FlagsFirstMatchingUnpinned()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, content: "apple pie"));
        vm.Tabs.Add(MakeTab(2, content: "banana"));
        vm.Tabs.Add(MakeTab(3, content: "apple tart"));
        vm.SearchText = "apple";

        vm.UpdateZoneSeparators();

        Flags(vm).Should().Equal(false, false, true);
    }

    [Fact]
    public void Search_NoMatchingPinned_NoSeparator()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTab(1, pinned: true, content: "cherry"));
        vm.Tabs.Add(MakeTab(2, content: "apple"));
        vm.SearchText = "apple";

        vm.UpdateZoneSeparators();

        Flags(vm).Should().Equal(false, false);
    }

    [Fact]
    public void ClearsStaleFlag_WhenPinnedZoneDisappears()
    {
        var vm = CreateVm();
        var pinned = MakeTab(1, pinned: true);
        vm.Tabs.Add(pinned);
        vm.Tabs.Add(MakeTab(2));
        vm.UpdateZoneSeparators();

        vm.Tabs.Remove(pinned);

        vm.UpdateZoneSeparators().Should().Be(1);
        Flags(vm).Should().Equal(false);
    }
}
