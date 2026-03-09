using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateVm(string desktopGuid = "test-guid") => new(desktopGuid);

    // ─── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsDesktopGuid()
    {
        var vm = CreateVm("abc-123");

        vm.DesktopGuid.Should().Be("abc-123");
    }

    [Fact]
    public void Constructor_TabsEmpty()
    {
        var vm = CreateVm();

        vm.Tabs.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ActiveTabNull()
    {
        var vm = CreateVm();

        vm.ActiveTab.Should().BeNull();
    }

    // ─── ActiveTab ───────────────────────────────────────────────────

    [Fact]
    public void ActiveTab_Change_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1 };
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.ActiveTab = tab;

        raised.Should().Contain(nameof(MainWindowViewModel.ActiveTab));
    }

    [Fact]
    public void ActiveTab_SameValue_DoesNotRaise()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1 };
        vm.ActiveTab = tab;

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.ActiveTab = tab;

        raised.Should().NotContain(nameof(MainWindowViewModel.ActiveTab));
    }

    // ─── SearchText ──────────────────────────────────────────────────

    [Fact]
    public void SearchText_Change_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SearchText = "hello";

        raised.Should().Contain(nameof(MainWindowViewModel.SearchText));
    }

    [Fact]
    public void SearchText_Change_RaisesFilteredTabs()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SearchText = "hello";

        raised.Should().Contain(nameof(MainWindowViewModel.FilteredTabs));
    }

    // ─── FilteredTabs ────────────────────────────────────────────────

    [Fact]
    public void FilteredTabs_ReturnsAllTabs_WhenSearchEmpty()
    {
        var vm = CreateVm();
        vm.Tabs.Add(new NoteTab { Id = 1, Content = "alpha" });
        vm.Tabs.Add(new NoteTab { Id = 2, Content = "beta" });

        vm.FilteredTabs.Should().HaveCount(2);
    }

    [Fact]
    public void FilteredTabs_FiltersOnContent()
    {
        var vm = CreateVm();
        vm.Tabs.Add(new NoteTab { Id = 1, Content = "alpha" });
        vm.Tabs.Add(new NoteTab { Id = 2, Content = "beta" });

        vm.SearchText = "alpha";

        vm.FilteredTabs.Should().HaveCount(1);
        vm.FilteredTabs[0].Id.Should().Be(1);
    }

    [Fact]
    public void FilteredTabs_FiltersOnDisplayLabel()
    {
        var vm = CreateVm();
        vm.Tabs.Add(new NoteTab { Id = 1, Name = "Shopping List" });
        vm.Tabs.Add(new NoteTab { Id = 2, Name = "Work Notes" });

        vm.SearchText = "shop";

        vm.FilteredTabs.Should().HaveCount(1);
        vm.FilteredTabs[0].Id.Should().Be(1);
    }

    [Fact]
    public void FilteredTabs_CaseInsensitive()
    {
        var vm = CreateVm();
        vm.Tabs.Add(new NoteTab { Id = 1, Content = "Hello World" });

        vm.SearchText = "hello";

        vm.FilteredTabs.Should().HaveCount(1);
    }

    [Fact]
    public void FilteredTabs_RecomputesOnTabAdd()
    {
        var vm = CreateVm();
        vm.SearchText = "alpha";
        vm.FilteredTabs.Should().BeEmpty();

        vm.Tabs.Add(new NoteTab { Id = 1, Content = "alpha" });

        vm.FilteredTabs.Should().HaveCount(1);
    }

    [Fact]
    public void FilteredTabs_RecomputesOnTabRemove()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1, Content = "alpha" };
        vm.Tabs.Add(tab);
        vm.SearchText = "alpha";
        vm.FilteredTabs.Should().HaveCount(1);

        vm.Tabs.Remove(tab);

        vm.FilteredTabs.Should().BeEmpty();
    }

    [Fact]
    public void FilteredTabs_RaisesPropertyChanged_OnTabCollectionChange()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.Tabs.Add(new NoteTab { Id = 1 });

        raised.Should().Contain(nameof(MainWindowViewModel.FilteredTabs));
    }

    // ─── MatchesSearch ───────────────────────────────────────────────

    [Fact]
    public void MatchesSearch_ReturnsTrue_WhenSearchEmpty()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Content = "anything" };

        vm.MatchesSearch(tab).Should().BeTrue();
    }

    [Fact]
    public void MatchesSearch_MatchesContent()
    {
        var vm = CreateVm();
        vm.SearchText = "world";
        var tab = new NoteTab { Content = "Hello World" };

        vm.MatchesSearch(tab).Should().BeTrue();
    }

    [Fact]
    public void MatchesSearch_MatchesName()
    {
        var vm = CreateVm();
        vm.SearchText = "shop";
        var tab = new NoteTab { Name = "Shopping" };

        vm.MatchesSearch(tab).Should().BeTrue();
    }

    [Fact]
    public void MatchesSearch_ReturnsFalse_WhenNoMatch()
    {
        var vm = CreateVm();
        vm.SearchText = "xyz";
        var tab = new NoteTab { Name = "Shopping", Content = "milk eggs" };

        vm.MatchesSearch(tab).Should().BeFalse();
    }

    // ─── WindowTitle ─────────────────────────────────────────────────

    [Fact]
    public void WindowTitle_WithDesktopName()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo("Work", null);

        vm.WindowTitle.Should().Be("JoJot \u2014 Work");
    }

    [Fact]
    public void WindowTitle_WithDesktopIndex()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo(null, 0);

        vm.WindowTitle.Should().Be("JoJot \u2014 Desktop 1");
    }

    [Fact]
    public void WindowTitle_WithDesktopIndex_TwoIsThree()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo(null, 2);

        vm.WindowTitle.Should().Be("JoJot \u2014 Desktop 3");
    }

    [Fact]
    public void WindowTitle_Fallback()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo(null, null);

        vm.WindowTitle.Should().Be("JoJot");
    }

    [Fact]
    public void WindowTitle_NameTakesPrecedenceOverIndex()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo("Gaming", 5);

        vm.WindowTitle.Should().Be("JoJot \u2014 Gaming");
    }

    [Fact]
    public void WindowTitle_EmptyName_FallsToIndex()
    {
        var vm = CreateVm();
        vm.UpdateDesktopInfo("", 1);

        vm.WindowTitle.Should().Be("JoJot \u2014 Desktop 2");
    }

    [Fact]
    public void UpdateDesktopInfo_RaisesWindowTitleChanged()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.UpdateDesktopInfo("Test", null);

        raised.Should().Contain(nameof(MainWindowViewModel.WindowTitle));
    }

    // ─── FormatWindowTitle (static) ──────────────────────────────────

    [Fact]
    public void FormatWindowTitle_UsesEmDash()
    {
        var title = MainWindowViewModel.FormatWindowTitle("Dev", null);

        title.Should().Contain("\u2014"); // em-dash
    }
}
