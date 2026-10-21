using System.IO;
using System.Text.RegularExpressions;

namespace JoJot.Tests.Views;

/// <summary>
/// Regression guard for a data-loss bug: the tab context menu's Pin/Clone/Save/Save As items did
/// <c>_activeTab = tab; SelectTabByNote(tab);</c>. TabList_SelectionChanged saves the OUTGOING
/// editor text into <c>_activeTab</c>, so the right-clicked note was overwritten with the
/// previously active note's text and autosave persisted it. The behavior lives in MainWindow
/// code-behind (ListBox selection + editor), which cannot be constructed in a unit test, so this
/// guards the source instead: the context menu must switch tabs only through SelectTabByNote.
/// </summary>
public partial class ContextMenuActiveTabGuardTests
{
    [GeneratedRegex(@"\b_activeTab\s*=(?!=)")]
    private static partial Regex ActiveTabAssignment();

    private static string FindSource(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate {relativePath} above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void ContextMenu_NeverAssignsActiveTabDirectly()
    {
        var source = File.ReadAllText(FindSource(Path.Combine("JoJot", "Views", "MainWindow.ContextMenu.cs")));

        var code = string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));

        ActiveTabAssignment().IsMatch(code).Should().BeFalse(
            "assigning _activeTab before SelectTabByNote makes SelectionChanged save the outgoing editor text into the target tab");
        code.Should().Contain("SelectTabByNote(tab)");
    }
}
