using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class PanelStateTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    // ─── Panel Toggle State ──────────────────────────────────────────

    [Fact]
    public void IsPreferencesOpen_DefaultFalse()
    {
        var vm = CreateVm();
        vm.IsPreferencesOpen.Should().BeFalse();
    }

    [Fact]
    public void IsCleanupOpen_DefaultFalse()
    {
        var vm = CreateVm();
        vm.IsCleanupOpen.Should().BeFalse();
    }

    [Fact]
    public void IsRecoveryOpen_DefaultFalse()
    {
        var vm = CreateVm();
        vm.IsRecoveryOpen.Should().BeFalse();
    }

    [Fact]
    public void IsHelpOpen_DefaultFalse()
    {
        var vm = CreateVm();
        vm.IsHelpOpen.Should().BeFalse();
    }

    [Fact]
    public void IsPreferencesOpen_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.IsPreferencesOpen = true;

        monitor.Should().RaisePropertyChangeFor(x => x.IsPreferencesOpen);
    }

    [Fact]
    public void IsCleanupOpen_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.IsCleanupOpen = true;

        monitor.Should().RaisePropertyChangeFor(x => x.IsCleanupOpen);
    }

    [Fact]
    public void IsRecoveryOpen_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.IsRecoveryOpen = true;

        monitor.Should().RaisePropertyChangeFor(x => x.IsRecoveryOpen);
    }

    [Fact]
    public void IsHelpOpen_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.IsHelpOpen = true;

        monitor.Should().RaisePropertyChangeFor(x => x.IsHelpOpen);
    }

    [Fact]
    public void CloseAllSidePanels_ClosesAllThree()
    {
        var vm = CreateVm();
        vm.IsPreferencesOpen = true;
        vm.IsCleanupOpen = true;
        vm.IsRecoveryOpen = true;

        vm.CloseAllSidePanels();

        vm.IsPreferencesOpen.Should().BeFalse();
        vm.IsCleanupOpen.Should().BeFalse();
        vm.IsRecoveryOpen.Should().BeFalse();
    }

    [Fact]
    public void CloseAllSidePanels_DoesNotCloseHelp()
    {
        var vm = CreateVm();
        vm.IsHelpOpen = true;

        vm.CloseAllSidePanels();

        vm.IsHelpOpen.Should().BeTrue();
    }

    // ─── GetCleanupCutoffDate ────────────────────────────────────────

    [Fact]
    public void GetCleanupCutoffDate_ReturnsNull_WhenAgeLessThan1()
    {
        MainWindowViewModel.GetCleanupCutoffDate(0, 0, DateTime.Now).Should().BeNull();
        MainWindowViewModel.GetCleanupCutoffDate(-1, 0, DateTime.Now).Should().BeNull();
    }

    [Fact]
    public void GetCleanupCutoffDate_Days()
    {
        var now = new DateTime(2026, 3, 9, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(7, 0, now);

        cutoff.Should().Be(now - TimeSpan.FromDays(7));
    }

    [Fact]
    public void GetCleanupCutoffDate_Hours()
    {
        var now = new DateTime(2026, 3, 9, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(24, 1, now);

        cutoff.Should().Be(now - TimeSpan.FromHours(24));
    }

    [Fact]
    public void GetCleanupCutoffDate_Weeks()
    {
        var now = new DateTime(2026, 3, 9, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(2, 2, now);

        cutoff.Should().Be(now - TimeSpan.FromDays(14));
    }

    [Fact]
    public void GetCleanupCutoffDate_Months()
    {
        var now = new DateTime(2026, 3, 9, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(3, 3, now);

        cutoff.Should().Be(now - TimeSpan.FromDays(90));
    }

    [Fact]
    public void GetCleanupCutoffDate_UnknownUnit_DefaultsToDays()
    {
        var now = new DateTime(2026, 3, 9, 12, 0, 0);
        var cutoff = MainWindowViewModel.GetCleanupCutoffDate(5, 99, now);

        cutoff.Should().Be(now - TimeSpan.FromDays(5));
    }

    // ─── GetCleanupCandidates ────────────────────────────────────────

    [Fact]
    public void GetCleanupCandidates_ReturnsOldTabs()
    {
        var vm = CreateVm();
        var old = new NoteTab { Id = 1, UpdatedAt = new DateTime(2026, 1, 1) };
        var recent = new NoteTab { Id = 2, UpdatedAt = new DateTime(2026, 3, 8) };
        vm.Tabs.Add(old);
        vm.Tabs.Add(recent);

        var cutoff = new DateTime(2026, 3, 2); // 7 days before March 9
        var result = vm.GetCleanupCandidates(cutoff, includePinned: false);

        result.Should().ContainSingle().Which.Should().BeSameAs(old);
    }

    [Fact]
    public void GetCleanupCandidates_ExcludesPinned_ByDefault()
    {
        var vm = CreateVm();
        var pinnedOld = new NoteTab { Id = 1, UpdatedAt = new DateTime(2026, 1, 1), Pinned = true };
        var unpinnedOld = new NoteTab { Id = 2, UpdatedAt = new DateTime(2026, 1, 1) };
        vm.Tabs.Add(pinnedOld);
        vm.Tabs.Add(unpinnedOld);

        var cutoff = new DateTime(2026, 3, 2);
        var result = vm.GetCleanupCandidates(cutoff, includePinned: false);

        result.Should().ContainSingle().Which.Should().BeSameAs(unpinnedOld);
    }

    [Fact]
    public void GetCleanupCandidates_IncludesPinned_WhenRequested()
    {
        var vm = CreateVm();
        var pinnedOld = new NoteTab { Id = 1, UpdatedAt = new DateTime(2026, 1, 1), Pinned = true };
        var unpinnedOld = new NoteTab { Id = 2, UpdatedAt = new DateTime(2026, 1, 1) };
        vm.Tabs.Add(pinnedOld);
        vm.Tabs.Add(unpinnedOld);

        var cutoff = new DateTime(2026, 3, 2);
        var result = vm.GetCleanupCandidates(cutoff, includePinned: true);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetCleanupCandidates_ReturnsEmpty_WhenAllTabsRecent()
    {
        var vm = CreateVm();
        vm.Tabs.Add(new NoteTab { Id = 1, UpdatedAt = new DateTime(2026, 3, 8) });

        var cutoff = new DateTime(2026, 3, 2);
        var result = vm.GetCleanupCandidates(cutoff, includePinned: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupCandidates_ReturnsEmpty_WhenNoTabs()
    {
        var vm = CreateVm();
        var result = vm.GetCleanupCandidates(DateTime.Now, includePinned: true);
        result.Should().BeEmpty();
    }

    // ─── GetCleanupExcerpt ───────────────────────────────────────────

    [Fact]
    public void GetCleanupExcerpt_ReturnsEmpty_WhenNoContent()
    {
        var tab = new NoteTab { Content = "" };
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupExcerpt_ReturnsEmpty_WhenWhitespaceContent()
    {
        var tab = new NoteTab { Content = "   " };
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupExcerpt_ReturnsExcerpt_WhenTabHasName()
    {
        var tab = new NoteTab { Name = "My note", Content = "Some content here" };
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().Be("Some content here");
    }

    [Fact]
    public void GetCleanupExcerpt_TruncatesAt50Chars_WhenTabHasName()
    {
        var tab = new NoteTab
        {
            Name = "My note",
            Content = new string('x', 60)
        };
        var result = MainWindowViewModel.GetCleanupExcerpt(tab);
        result.Should().HaveLength(53); // 50 + "..."
        result.Should().EndWith("...");
    }

    [Fact]
    public void GetCleanupExcerpt_ReturnsEmpty_WhenNoName()
    {
        // No custom name — DisplayLabel already shows content preview
        var tab = new NoteTab { Content = "Some content here" };
        MainWindowViewModel.GetCleanupExcerpt(tab).Should().BeEmpty();
    }

    [Fact]
    public void GetCleanupExcerpt_ReplacesNewlines()
    {
        var tab = new NoteTab { Name = "My note", Content = "Line 1\nLine 2\rLine 3" };
        var result = MainWindowViewModel.GetCleanupExcerpt(tab);
        result.Should().NotContain("\n");
        result.Should().NotContain("\r");
    }
}
