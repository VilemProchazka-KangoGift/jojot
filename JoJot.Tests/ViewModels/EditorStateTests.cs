using JoJot.Models;
using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class EditorStateTests
{
    private static MainWindowViewModel CreateVm() => new("test-guid");

    // ─── SaveEditorStateToTab ────────────────────────────────────────

    [Fact]
    public void SaveEditorState_ReturnsFalse_WhenNoActiveTab()
    {
        var vm = CreateVm();

        vm.SaveEditorStateToTab("text", 0, 0).Should().BeFalse();
    }

    [Fact]
    public void SaveEditorState_SavesContent()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1, Content = "old" };
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("new content", 5, 100);

        tab.Content.Should().Be("new content");
    }

    [Fact]
    public void SaveEditorState_SavesCursorAndScroll()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1, Content = "old" };
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("new", 42, 200);

        tab.CursorPosition.Should().Be(42);
        tab.EditorScrollOffset.Should().Be(200);
    }

    [Fact]
    public void SaveEditorState_UpdatesTimestamp_WhenContentChanged()
    {
        var vm = CreateVm();
        var tab = new NoteTab { Id = 1, Content = "old", UpdatedAt = new DateTime(2020, 1, 1) };
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("new", 0, 0);

        tab.UpdatedAt.Should().BeAfter(new DateTime(2020, 1, 1));
    }

    [Fact]
    public void SaveEditorState_DoesNotUpdateTimestamp_WhenContentSame()
    {
        var vm = CreateVm();
        var originalTime = new DateTime(2020, 1, 1);
        var tab = new NoteTab { Id = 1, Content = "same", UpdatedAt = originalTime };
        vm.ActiveTab = tab;

        vm.SaveEditorStateToTab("same", 0, 0);

        tab.UpdatedAt.Should().Be(originalTime);
    }

    [Fact]
    public void SaveEditorState_ReturnsTrue_WhenContentChanged()
    {
        var vm = CreateVm();
        vm.ActiveTab = new NoteTab { Id = 1, Content = "old" };

        vm.SaveEditorStateToTab("new", 0, 0).Should().BeTrue();
    }

    [Fact]
    public void SaveEditorState_ReturnsFalse_WhenContentSame()
    {
        var vm = CreateVm();
        vm.ActiveTab = new NoteTab { Id = 1, Content = "same" };

        vm.SaveEditorStateToTab("same", 0, 0).Should().BeFalse();
    }

    // ─── IsRestoringContent ──────────────────────────────────────────

    [Fact]
    public void IsRestoringContent_DefaultsFalse()
    {
        var vm = CreateVm();

        vm.IsRestoringContent.Should().BeFalse();
    }

    [Fact]
    public void IsRestoringContent_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.IsRestoringContent = true;

        raised.Should().Contain(nameof(MainWindowViewModel.IsRestoringContent));
    }

    // ─── GetDefaultFilename ──────────────────────────────────────────

    [Fact]
    public void GetDefaultFilename_UsesTabName()
    {
        var tab = new NoteTab { Name = "My Note", Content = "stuff" };

        var filename = MainWindowViewModel.GetDefaultFilename(tab);

        filename.Should().Be("My Note.txt");
    }

    [Fact]
    public void GetDefaultFilename_FallsToContentPreview()
    {
        var tab = new NoteTab { Content = "Short content" };

        var filename = MainWindowViewModel.GetDefaultFilename(tab);

        filename.Should().Be("Short content.txt");
    }

    [Fact]
    public void GetDefaultFilename_TruncatesLongContent()
    {
        var tab = new NoteTab { Content = new string('a', 50) };

        var filename = MainWindowViewModel.GetDefaultFilename(tab);

        filename.Should().Be(new string('a', 45) + ".txt");
    }

    [Fact]
    public void GetDefaultFilename_FallsToDateFormat()
    {
        var tab = new NoteTab();

        var filename = MainWindowViewModel.GetDefaultFilename(tab);

        filename.Should().StartWith("JoJot note ");
        filename.Should().EndWith(".txt");
    }

    // ─── SanitizeFilename ────────────────────────────────────────────

    [Fact]
    public void SanitizeFilename_RemovesIllegalChars()
    {
        var result = MainWindowViewModel.SanitizeFilename("file/name:test");

        result.Should().Be("file_name_test");
    }

    [Fact]
    public void SanitizeFilename_TrimsTrailingDotsAndSpaces()
    {
        var result = MainWindowViewModel.SanitizeFilename("test...");

        result.Should().Be("test");
    }

    [Fact]
    public void SanitizeFilename_FallbackForEmpty()
    {
        var result = MainWindowViewModel.SanitizeFilename("...");

        result.Should().Be("JoJot note");
    }
}
