using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using JoJot.Models;
using JoJot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(JoJot.AvaloniaExperiment.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ExperimentApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task EditSwitchCloseAndReopenPersistTheSelectedNotes()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        var add = Find<Button>(window, "AddButton");
        var first = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(JoJot.Resources.Strings.Date_NewNote, first.DisplayLabel);
        Assert.Equal(first.DisplayLabel, global::Avalonia.Automation.AutomationProperties.GetHelpText(editor));
        Click(add);
        Assert.Single(list.Items);
        var searchInput = Find<TextBox>(window, "SearchBox");
        searchInput.Focus();
        Assert.True(searchInput.IsFocused);
        Press(window, searchInput, Key.T, PhysicalKey.T, RawInputModifiers.Control);
        await WaitForAsync(() => editor.IsFocused, "new tab editor focus");

        editor.Text = "Debounced <first> & note";
        await WaitForAsync(() => ReadContent(fixture.Directory, first.Id) == editor.Text, "debounced database write");
        editor.CaretIndex = 3;
        Press(window, editor, Key.T, PhysicalKey.T, RawInputModifiers.Control);
        await WaitForAsync(() => list.ItemCount == 2 && editor.IsEffectivelyEnabled, "second note created");
        var second = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = "Saved by switching tabs";
        list.SelectedItem = first;
        await WaitForAsync(() => editor.Text == "Debounced <first> & note" && add.IsEnabled, "first note selected");
        Assert.Equal("Saved by switching tabs", ReadContent(fixture.Directory, second.Id));
        Assert.Equal(3, editor.CaretIndex);

        Press(window, editor, Key.F2, PhysicalKey.F2);
        await WaitForAsync(() => window.GetVisualDescendants().OfType<TextBox>().Any(box => box.Name == "InlineRenameBox"),
            "inline rename box");
        var rename = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "InlineRenameBox");
        rename.Text = "Renamed note";
        Press(window, rename, Key.Enter, PhysicalKey.Enter);
        await WaitForAsync(() => first.Name == "Renamed note" && add.IsEnabled, "rename saved");
        Press(window, editor, Key.P, PhysicalKey.P, RawInputModifiers.Control);
        await WaitForAsync(() => first.Pinned && add.IsEnabled, "pin saved");

        var search = Find<TextBox>(window, "SearchBox");
        search.Text = "switching";
        await WaitForAsync(() => list.ItemCount == 1 && add.IsEnabled, "content filtered");
        Assert.Same(second, Assert.Single(list.Items));
        Assert.Null(list.SelectedItem);
        Assert.Equal("Debounced <first> & note", editor.Text);
        search.Text = "";
        await WaitForAsync(() => list.ItemCount == 2, "filter cleared");
        list.SelectedItem = first;
        await WaitForAsync(() => editor.Text == "Debounced <first> & note" && add.IsEnabled, "pinned note reselected");
        await PreferenceStore.SetPreferenceAsync("theme", "dark");

        editor.Text = "Preserved during a deferred reload";
        await window.ReloadNotesAsync();
        Assert.Same(first, list.SelectedItem);
        Assert.Equal("Preserved during a deferred reload", editor.Text);
        Assert.Equal(editor.Text, ReadContent(fixture.Directory, first.Id));
        editor.Text = "Saved by immediate close";
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "window closed");
        Assert.Equal("Saved by immediate close", ReadContent(fixture.Directory, first.Id));

        window = fixture.Open();
        await window.Ready;
        editor = Find<TextBox>(window, "Editor");
        list = Find<ListBox>(window, "NoteList");
        first = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal("Renamed note", first.Name);
        Assert.True(first.Pinned);
        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.Equal("Saved by immediate close", editor.Text);

        list.SelectedItem = Assert.Single(list.Items.OfType<NoteTab>(), note => note.Id == second.Id);
        await WaitForAsync(() => editor.Text == "Saved by switching tabs" && editor.IsEffectivelyEnabled, "second note selected after reopen");
        Press(window, editor, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        await WaitForAsync(() => list.ItemCount == 1 && editor.IsEffectivelyEnabled, "note soft-deleted");
        Assert.Equal("Saved by switching tabs", ReadContent(fixture.Directory, second.Id));
        Click(Find<Button>(window, "UndoToastButton"));
        Assert.Equal(2, list.ItemCount);
        Assert.Equal(second.Id, Assert.IsType<NoteTab>(list.SelectedItem).Id);

        Press(window, editor, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        await WaitForAsync(() => list.ItemCount == 1 && editor.IsEffectivelyEnabled, "note soft-deleted again");
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "reopened window closed");
        Assert.Null(ReadContent(fixture.Directory, second.Id));
    }

    [AvaloniaFact]
    public async Task FindReplaceClipboardAndPerNoteHistoryUseEditorShortcuts()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        var first = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Focus();
        window.KeyTextInput("alpha Alpha alphabet");
        Assert.Equal("alpha Alpha alphabet", editor.Text);
        Press(window, editor, Key.C, PhysicalKey.C, RawInputModifiers.Control);
        string? copied = null;
        if (window.Clipboard is { } clipboard)
            copied = await clipboard.TryGetTextAsync();
        Assert.Equal("alpha Alpha alphabet", copied);

        Press(window, editor, Key.K, PhysicalKey.K, RawInputModifiers.Control);
        await WaitForAsync(() => list.ItemCount == 2 && editor.IsEffectivelyEnabled, "clone selected");
        Assert.Equal("alpha Alpha alphabet", editor.Text);
        var clone = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.NotEqual(first.Id, clone.Id);

        Press(window, editor, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        var findHost = Find<Border>(window, "FindHost");
        Assert.True(findHost.IsVisible);
        var query = Assert.Single(findHost.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "FindQuery");
        var replacement = Assert.Single(findHost.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "FindReplacement");
        query.Text = "alpha";
        replacement.Text = "beta";
        foreach (var checkbox in findHost.GetVisualDescendants().OfType<CheckBox>())
            checkbox.IsChecked = true;
        await WaitForAsync(() => editor.SelectedText == "alpha", "case-sensitive whole-word match");
        Click(Assert.Single(findHost.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, JoJot.Resources.Strings.Find_ReplaceAll)));
        Assert.Equal("beta Alpha alphabet", editor.Text);
        Press(window, query, Key.Z, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("beta Alpha alphabet", editor.Text);
        Press(window, replacement, Key.Y, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal("beta Alpha alphabet", editor.Text);
        Press(window, query, Key.Escape, PhysicalKey.Escape);
        Assert.False(findHost.IsVisible);

        Click(Find<Button>(window, "UndoToastButton"));
        Assert.Equal("alpha Alpha alphabet", editor.Text);
        Press(window, editor, Key.Y, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal("beta Alpha alphabet", editor.Text);

        Press(window, editor, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        query.Text = "beta";
        replacement.Text = "gamma";
        Click(Assert.Single(findHost.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, JoJot.Resources.Strings.Find_ReplaceAll)));
        Assert.Equal("gamma Alpha alphabet", editor.Text);
        list.SelectedItem = first;
        await WaitForAsync(() => editor.Text == "alpha Alpha alphabet" && editor.IsEffectivelyEnabled, "original selected");
        Click(Find<Button>(window, "UndoToastButton"));
        Assert.Equal("alpha Alpha alphabet", editor.Text);
        Press(window, editor, Key.Escape, PhysicalKey.Escape);
        Press(window, editor, Key.Z, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("", editor.Text);
        list.SelectedItem = clone;
        await WaitForAsync(() => editor.Text == "gamma Alpha alphabet" && editor.IsEffectivelyEnabled, "clone history isolated");
    }

    [AvaloniaFact]
    public async Task ReopeningWindowPreservesUndoForAnEditImmediatelyBeforeClose()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        editor.Text = "B";
        var undo = Find<Button>(window, "UndoButton");
        await WaitForAsync(() => undo.IsEnabled, "edit B captured");
        await window.FlushWindowAsync();
        editor.Text = "C";
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "window closed immediately after C");

        window = fixture.Open();
        await window.Ready;
        editor = Find<TextBox>(window, "Editor");
        Assert.Equal("C", editor.Text);
        Press(window, editor, Key.Z, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("B", editor.Text);
        Press(window, editor, Key.Y, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal("C", editor.Text);
    }

    [AvaloniaFact]
    public async Task FileImportSavesExplicitlyAndBulkDeleteSkipsPinnedNotes()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var source = Path.Combine(fixture.Directory, "source.txt");
        var target = Path.Combine(fixture.Directory, "saved.txt");
        await File.WriteAllTextAsync(source, "source data");
        await window.ImportPathsAsync([source]);
        var list = Find<ListBox>(window, "NoteList");
        var editor = Find<TextBox>(window, "Editor");
        var imported = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(source, imported.FilePath);
        Press(window, editor, Key.F2, PhysicalKey.F2, RawInputModifiers.None);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "InlineRenameBox");
        Assert.Equal("Cannot rename opened files.", Find<TextBlock>(window, "ToastText").Text);
        Assert.Equal("source.txt", imported.Name);
        Assert.False(imported.HasUnsavedFileChanges);
        editor.Text = "explicitly saved";
        await WaitForAsync(() => imported.HasUnsavedFileChanges, "unsaved external file");
        await window.FlushWindowAsync();
        Assert.True(imported.HasUnsavedFileChanges);
        Assert.Equal("source data", await File.ReadAllTextAsync(source));
        Press(window, editor, Key.S, PhysicalKey.S, RawInputModifiers.Control);
        await WaitForAsync(() => editor.IsEffectivelyEnabled && File.ReadAllText(source) == "explicitly saved", "file-backed save");
        Assert.False(imported.HasUnsavedFileChanges);
        await window.SaveNoteToPathCoreAsync(imported, target);
        Assert.False(imported.HasUnsavedFileChanges);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("explicitly saved"), await File.ReadAllBytesAsync(target));
        Assert.Equal(target, imported.FilePath);
        Assert.Equal("saved.txt", imported.Name);

        Press(window, editor, Key.P, PhysicalKey.P, RawInputModifiers.Control);
        await WaitForAsync(() => imported.Pinned && editor.IsEffectivelyEnabled, "file-backed note pinned");
        var unpinned = Assert.Single(list.Items.OfType<NoteTab>(), note => !note.Pinned);
        await window.MoveTabAsync(imported, 2);
        Assert.Same(imported, list.Items[0]);
        await window.DeleteNotesWithUndoAsync([imported, unpinned]);
        Assert.Same(imported, Assert.Single(list.Items));
        Click(Find<Button>(window, "UndoToastButton"));
        Assert.Contains(unpinned, list.Items);
    }

    [AvaloniaFact]
    public async Task ImportsTextWithAnyNameAndPreservesBomWhileRejectingBinaryTargets()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var readme = Path.Combine(fixture.Directory, "README");
        var config = Path.Combine(fixture.Directory, "settings.toml");
        var binary = Path.Combine(fixture.Directory, "binary.txt");
        var utf16 = new System.Text.UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        await File.WriteAllTextAsync(readme, "Text without an extension");
        await File.WriteAllTextAsync(config, "name = 'žluťoučký'", utf16);
        byte[] binaryBytes = [0x50, 0x4b, 0x03, 0x04, 0x00];
        await File.WriteAllBytesAsync(binary, binaryBytes);

        await window.ImportPathsAsync([config, readme, binary]);
        var list = Find<ListBox>(window, "NoteList");
        Assert.Contains(list.Items.OfType<NoteTab>(), note => note.FilePath == readme);
        Assert.DoesNotContain(list.Items.OfType<NoteTab>(), note => note.FilePath == binary);
        var selected = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(config, selected.FilePath);
        var editor = Find<TextBox>(window, "Editor");
        Assert.Equal("name = 'žluťoučký'", editor.Text);
        editor.Text = "name = 'Příliš'";
        await window.FlushWindowAsync();
        Press(window, editor, Key.S, PhysicalKey.S, RawInputModifiers.Control);
        var expected = utf16.GetPreamble().Concat(utf16.GetBytes("name = 'Příliš'")).ToArray();
        await WaitForAsync(() => editor.IsEffectivelyEnabled && File.ReadAllBytes(config).SequenceEqual(expected),
            "UTF-16 BOM and content preserved");
        await Assert.ThrowsAsync<InvalidDataException>(() => window.SaveNoteToPathCoreAsync(selected, binary));
        Assert.Equal(binaryBytes, await File.ReadAllBytesAsync(binary));
        Assert.Equal(config, selected.FilePath);
    }

    [AvaloniaFact]
    public async Task LegacyEncodingIsDetectedAndSurvivesAnAsciiSaveAndReopen()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var central = Path.Combine(fixture.Directory, "central");
        var western = Path.Combine(fixture.Directory, "western.ini");
        await File.WriteAllBytesAsync(central, [0xe1]);
        await File.WriteAllBytesAsync(western, [0x9c, 0xff]);
        Assert.Equal(1250, (await TextNoteFiles.ReadAsync(central)).Encoding.CodePage);
        Assert.Equal(1252, (await TextNoteFiles.ReadAsync(western)).Encoding.CodePage);

        await window.ImportPathsAsync([western]);
        var editor = Find<TextBox>(window, "Editor");
        Assert.Equal("œÿ", editor.Text);
        editor.Text = "ascii";
        await window.FlushWindowAsync();
        Press(window, editor, Key.S, PhysicalKey.S, RawInputModifiers.Control);
        await WaitForAsync(() => editor.IsEffectivelyEnabled && File.ReadAllText(western) == "ascii", "ASCII file saved");
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "legacy note window closed");

        window = fixture.Open();
        await window.Ready;
        editor = Find<TextBox>(window, "Editor");
        Assert.Equal("ascii", editor.Text);
        editor.Text = "œ";
        await window.FlushWindowAsync();
        Press(window, editor, Key.S, PhysicalKey.S, RawInputModifiers.Control);
        await WaitForAsync(() => editor.IsEffectivelyEnabled && File.ReadAllBytes(western).SequenceEqual(new byte[] { 0x9c }),
            "remembered Windows-1252 restored");
        var note = Assert.IsType<NoteTab>(Find<ListBox>(window, "NoteList").SelectedItem);
        note.Content = "😀";
        await Assert.ThrowsAsync<System.Text.EncoderFallbackException>(() => window.SaveNoteToPathCoreAsync(note, western));
        Assert.Equal(new byte[] { 0x9c }, await File.ReadAllBytesAsync(western));
    }

    [AvaloniaFact]
    public async Task CorrectLegacyEncodingPreservesDraftAndNeverWritesSourceFile()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var path = Path.Combine(fixture.Directory, "legacy");
        byte[] bytes = [0x6e, 0x61, 0xef, 0x76, 0x65];
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await window.ImportPathsAsync([path]);
        var note = Assert.IsType<NoteTab>(Find<ListBox>(window, "NoteList").SelectedItem);
        var editor = Find<TextBox>(window, "Editor");
        var misread = editor.Text;
        Assert.NotEqual("naïve", misread);
        editor.Text = "unsaved draft";
        await window.ReopenWithLegacyEncodingAsync(note, 1252);
        Assert.Equal("unsaved draft", editor.Text);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

        editor.Text = misread;
        await window.ReopenWithLegacyEncodingAsync(note, 1252);
        Assert.Equal("naïve", editor.Text);
        Assert.False(note.HasUnsavedFileChanges);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "corrected file-backed window closed");

        window = fixture.Open();
        await window.Ready;
        note = Assert.IsType<NoteTab>(Find<ListBox>(window, "NoteList").SelectedItem);
        Assert.Equal("naïve", note.Content);
        await window.ReopenWithLegacyEncodingAsync(note, 1250);
        Assert.Equal(misread, note.Content);
    }

    [AvaloniaFact]
    public async Task FileBackedDraftStillShowsUnsavedAfterReopenAndClearsWhenContentMatches()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var path = Path.Combine(fixture.Directory, "reopen.txt");
        const string saved = "longer saved content";
        await File.WriteAllTextAsync(path, saved);
        await window.ImportPathsAsync([path]);
        var editor = Find<TextBox>(window, "Editor");
        editor.Text = "draft";
        await window.FlushWindowAsync();
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "file-backed window closed");

        window = fixture.Open();
        await window.Ready;
        editor = Find<TextBox>(window, "Editor");
        var imported = Assert.IsType<NoteTab>(Find<ListBox>(window, "NoteList").SelectedItem);
        Assert.Equal("draft", editor.Text);
        await WaitForAsync(() => imported.HasUnsavedFileChanges, "reopened draft differs from file");
        Assert.Equal(saved, await File.ReadAllTextAsync(path));
        editor.Text = saved;
        await WaitForAsync(() => !imported.HasUnsavedFileChanges, "restoring original text clears the file marker");
    }

    [AvaloniaFact]
    public async Task StartupPreferenceSelectsWelcomeThenBlankOnlyForPrimaryStartup()
    {
        await using var fixture = new WindowFixture();
        var setup = fixture.Open();
        await setup.Ready;
        await PreferenceStore.SetPreferenceAsync("startup_note", "new");
        await NoteStore.DeleteNotesAsync(Find<ListBox>(setup, "NoteList").Items.OfType<NoteTab>().Select(note => note.Id).ToArray());
        setup.Close();
        await WaitForAsync(() => !setup.IsVisible, "empty fixture closed");
        await DatabaseCore.OpenAsync(Path.Combine(fixture.Directory, "jojot.db"));
        await DatabaseCore.ExecuteNonQueryAsync("DELETE FROM app_state;");
        await DatabaseCore.CloseAsync();

        var first = fixture.Open(applyStartupPreference: true);
        await first.Ready;
        var welcome = Assert.IsType<NoteTab>(Find<ListBox>(first, "NoteList").SelectedItem);
        Assert.Equal(JoJot.Resources.Strings.Welcome_Title, welcome.Name);
        Assert.Equal(JoJot.Resources.Strings.Welcome_Content, Find<TextBox>(first, "Editor").Text);
        first.Close();
        await WaitForAsync(() => !first.IsVisible, "first primary closed");

        var second = fixture.Open(applyStartupPreference: true);
        await second.Ready;
        var editor = Find<TextBox>(second, "Editor");
        var list = Find<ListBox>(second, "NoteList");
        var newNote = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.NotEqual(welcome.Id, newNote.Id);
        Assert.Equal("", editor.Text);
        Assert.True(editor.IsFocused);
        editor.Text = "Blank note edited";
        await second.FlushWindowAsync();
        Assert.Equal(2, list.ItemCount);
        second.Close();
        await WaitForAsync(() => !second.IsVisible, "second primary closed");

        var resident = fixture.Open();
        await resident.Ready;
        var residentNotes = Find<ListBox>(resident, "NoteList");
        Assert.Equal(2, residentNotes.ItemCount);
        await NoteStore.DeleteNotesAsync(residentNotes.Items.OfType<NoteTab>().Select(note => note.Id).ToArray());
        resident.Close();
        await WaitForAsync(() => !resident.IsVisible, "resident reopened window closed");

        var emptied = fixture.Open(applyStartupPreference: true);
        await emptied.Ready;
        Assert.True(Assert.IsType<NoteTab>(Find<ListBox>(emptied, "NoteList").SelectedItem).IsPlaceholder);
    }

    [AvaloniaFact]
    public async Task LastNoteRestoresSelectedTabFromPreviousWindow()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open(applyStartupPreference: true);
        await window.Ready;
        var list = Find<ListBox>(window, "NoteList");
        var editor = Find<TextBox>(window, "Editor");
        var welcome = Assert.IsType<NoteTab>(list.SelectedItem);
        window.RequestNewTab("Other note");
        await WaitForAsync(() => editor.Text == "Other note" && editor.IsEffectivelyEnabled, "other note created");
        list.SelectedItem = welcome;
        await WaitForAsync(() => editor.Text == welcome.Content && editor.IsEffectivelyEnabled, "older note selected");
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "primary window closed");

        window = fixture.Open(applyStartupPreference: true);
        await window.Ready;
        list = Find<ListBox>(window, "NoteList");
        Assert.Equal(2, list.ItemCount);
        Assert.Equal(welcome.Id, Assert.IsType<NoteTab>(list.SelectedItem).Id);
    }

    [AvaloniaFact]
    public async Task SearchAndToolbarStayAboveTheirContentBesideThinSidebarDivider()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var list = Find<ListBox>(window, "NoteList");
        var editor = Find<TextBox>(window, "Editor");
        var toolbar = Find<Border>(window, "EditorToolbar");
        var splitter = Find<GridSplitter>(window, "TabSplitter");
        var search = Find<TextBox>(window, "SearchBox");
        var searchBottom = search.TranslatePoint(new Point(0, search.Bounds.Height), window)!.Value.Y;
        Assert.True(searchBottom + 8 <= list.TranslatePoint(default, window)!.Value.Y);
        Assert.Equal(toolbar.Bounds.Height, editor.TranslatePoint(default, window)!.Value.Y);
        Assert.Equal(4, splitter.Bounds.Width);
        var line = Find<Border>(window, "TabDividerLine");
        Assert.Equal(1, line.Bounds.Width);
        Assert.False(line.IsHitTestVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewTabScrollsToTopAndReusedPlaceholderClampsAtEnd(bool reuse)
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        var initial = Assert.IsType<NoteTab>(list.SelectedItem);
        if (!reuse)
            editor.Text = "Existing note";

        for (var index = 0; index < 30; index++)
            await NoteStore.InsertNoteAsync(window.DesktopGuid, $"Pinned {index}", "", true, index);
        if (!reuse)
        {
            for (var index = 0; index < 30; index++)
                await NoteStore.InsertNoteAsync(window.DesktopGuid, $"Other {index}", "", false, index + 1);
        }
        await window.ReloadNotesAsync();

        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        window.RequestNewTab();
        await WaitForAsync(() =>
        {
            if (!editor.IsFocused || scroll.Offset.Y <= 0 || list.SelectedItem is not NoteTab note ||
                list.ContainerFromItem(note) is not ListBoxItem row || row.TranslatePoint(default, viewport) is not { } position)
                return false;
            return reuse
                ? Math.Abs(scroll.Offset.Y - (scroll.Extent.Height - scroll.Viewport.Height)) <= 1 &&
                  position.Y + row.Bounds.Height <= scroll.Viewport.Height + 1
                : Math.Abs(position.Y) <= 1;
        }, "new tab aligned as close to the sidebar top as possible");

        var created = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(reuse, created.Id == initial.Id);
        Assert.Equal(30, list.SelectedIndex);
        var scrollbar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollBar");
        Assert.True(scrollbar.IsVisible);
        Assert.InRange(scrollbar.Bounds.Width, 1, 10);
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(created));
        var close = row.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("closeAction"));
        Assert.True(close.IsVisible);
        var closeRight = close.TranslatePoint(new Point(close.Bounds.Width, 0), scroll);
        var scrollbarLeft = scrollbar.TranslatePoint(default, scroll);
        Assert.NotNull(closeRight);
        Assert.NotNull(scrollbarLeft);
        Assert.True(closeRight.Value.X <= scrollbarLeft.Value.X);
    }

    [AvaloniaFact]
    public async Task PointerDragReordersTabsInsideTheirZoneAndPersistsOrder()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var list = Find<ListBox>(window, "NoteList");
        var editor = Find<TextBox>(window, "Editor");
        var first = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = "First tab";
        Press(window, editor, Key.T, PhysicalKey.T, RawInputModifiers.Control);
        await WaitForAsync(() => list.ItemCount == 2 && editor.IsEffectivelyEnabled, "two tabs");
        var second = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = "Second tab";
        await WaitForAsync(() => list.ContainerFromItem(first) is ListBoxItem { Bounds.Height: > 0 } &&
            list.ContainerFromItem(second) is ListBoxItem { Bounds.Height: > 0 }, "two visible tab containers");
        var from = Assert.IsType<ListBoxItem>(list.ContainerFromItem(second));
        var to = Assert.IsType<ListBoxItem>(list.ContainerFromItem(first));
        var start = from.TranslatePoint(new Point(12, from.Bounds.Height / 2), window)!.Value;
        var end = to.TranslatePoint(new Point(12, to.Bounds.Height - 3), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await WaitForAsync(() => ReferenceEquals(list.Items[0], first) && editor.IsEffectivelyEnabled, "pointer reorder");
        var persisted = await NoteStore.GetNotesForDesktopAsync(window.DesktopGuid);
        Assert.Equal(new[] { first.Id, second.Id }, persisted.Select(note => note.Id));
    }
    [AvaloniaFact]
    public async Task QueuedNewTabRetainsOptionalContentThroughStartup()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        window.RequestNewTab("Created by an early IPC request");
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        await WaitForAsync(() => editor.IsEffectivelyEnabled && editor.Text == "Created by an early IPC request",
            "queued tab content");
        var note = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(editor.Text, ReadContent(fixture.Directory, note.Id));
    }
    [AvaloniaFact]
    public async Task TabSwitchRestoresTheEditorsCaretAndVerticalScroll()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var list = Find<ListBox>(window, "NoteList");
        var editor = Find<TextBox>(window, "Editor");
        var first = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"Line {index}"));
        var caret = editor.Text.IndexOf("Line 25", StringComparison.Ordinal);
        editor.CaretIndex = caret;
        ScrollViewer? scroll = null;
        await WaitForAsync(() => (scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()) is
            { Extent.Height: > 1000 }, "scrollable editor layout");
        Assert.True(editor.Focus());
        scroll!.Offset = new Vector(0, 190);
        Assert.Equal(190, scroll.Offset.Y);

        window.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, null);
        window.KeyRelease(Key.T, RawInputModifiers.Control, PhysicalKey.T, null);
        await WaitForAsync(() => list.ItemCount == 2 && editor.IsEffectivelyEnabled, "second tab created");
        list.SelectedItem = first;
        await WaitForAsync(() => editor.IsEffectivelyEnabled && ReferenceEquals(list.SelectedItem, first), "original tab reselected");
        Assert.Equal(190, first.EditorScrollOffset);
        Assert.Equal(caret, editor.CaretIndex);
        await WaitForAsync(() => scroll.Offset.Y == 190, "restored vertical scroll");
    }
    [AvaloniaFact]
    public async Task FailedReloadSurfacesTheStorageErrorAndRetainsTheCurrentEditor()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        var note = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = "Keep this note when the desktop reload fails";
        await window.FlushWindowAsync();

        // Simulate a storage read failure using the real disposable SQLite fixture.
        await DatabaseCore.ExecuteNonQueryAsync("ALTER TABLE notes RENAME TO notes_unavailable;");
        try
        {
            var error = await Assert.ThrowsAsync<SqliteException>(window.ReloadNotesAsync);
            Assert.Contains("no such table", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Same(note, list.SelectedItem);
            Assert.Equal("Keep this note when the desktop reload fails", editor.Text);
            Assert.True(editor.IsEffectivelyEnabled);
        }
        finally
        {
            await DatabaseCore.ExecuteNonQueryAsync("ALTER TABLE notes_unavailable RENAME TO notes;");
        }

        editor.Text = "Retry persisted this edit";
        await window.ReloadNotesAsync();
        Assert.Equal(editor.Text, ReadContent(fixture.Directory, note.Id));
    }
    [AvaloniaFact]
    public async Task ShutdownFreezeRejectsLateTypingWhileAnotherWindowStillDrains()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = Find<TextBox>(window, "Editor");
        var selected = Assert.IsType<NoteTab>(Find<ListBox>(window, "NoteList").SelectedItem);
        Assert.True(editor.Focus());
        window.KeyTextInput("Persisted before shutdown");
        var otherWindowDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.IsEnabled = false;
        try
        {
            var shutdownDrain = DrainInOrderAsync();
            await WaitForAsync(() => ReadContent(fixture.Directory, selected.Id) == "Persisted before shutdown",
                "first window flushed while the second is pending");
            Assert.False(shutdownDrain.IsCompleted);
            Assert.False(editor.IsEffectivelyEnabled);
            window.KeyTextInput("late keystrokes");
            Assert.Equal("Persisted before shutdown", editor.Text);
            otherWindowDrain.SetResult();
            await shutdownDrain;
        }
        finally
        {
            otherWindowDrain.TrySetResult();
            window.IsEnabled = true;
        }

        async Task DrainInOrderAsync()
        {
            await window.FlushWindowAsync();
            await otherWindowDrain.Task;
        }
    }
    [AvaloniaFact]
    public async Task VisibleOwnedDialogBlocksShutdownUntilDismissed()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        Assert.False(window.HasShutdownBlockingInteraction);
        var dialog = new Window { Content = new TextBlock { Text = "Confirm action" } };
        var closing = dialog.ShowDialog(window);
        Assert.True(window.HasShutdownBlockingInteraction);
        dialog.Close();
        await closing;
        Assert.False(window.HasShutdownBlockingInteraction);
    }
    private static void Press(Window window, Control target, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Assert.True(target.Focus());
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }
    [AvaloniaFact]
    public async Task CloseRequestedDuringStartupIsReplayedAfterStorageInitializes()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        Assert.False(Find<Button>(window, "AddButton").IsEnabled);
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "queued startup close");
        Assert.True(File.Exists(Path.Combine(fixture.Directory, ".clean-shutdown")));
        using var file = File.Open(Path.Combine(fixture.Directory, ".experiment-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static string? ReadContent(string directory, long id)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "jojot.db"),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM notes WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    }

    internal static async Task WaitForAsync(Func<bool> predicate, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            try { await Task.Delay(15, timeout.Token); }
            catch (OperationCanceledException) { Assert.Fail($"Timed out waiting for {description}."); }
        }
    }

    internal sealed class WindowFixture : IAsyncDisposable
    {
        private MainWindow? _window;

        public WindowFixture() => UndoManager.SetInstance(new UndoManager(SystemClock.Instance));

        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "jojot-avalonia-test-" + Guid.NewGuid().ToString("N"));

        public MainWindow Open(bool applyStartupPreference = false)
        {
            ExperimentApp.Options = ExperimentOptions.Parse(["--data-dir", Directory]);
            ExperimentApp.Benchmark = null;
            _window = new MainWindow(ExperimentApp.Options.DesktopGuid, applyStartupPreference);
            _window.Show();
            return _window;
        }

        public async ValueTask DisposeAsync()
        {
            if (_window is { IsVisible: true } window)
            {
                foreach (var dialog in window.OwnedWindows.ToArray())
                    dialog.Close();
                window.Close();
                await WaitForAsync(() => !window.IsVisible, "test window cleanup");
            }
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

public sealed class ExperimentOptionsTests
{
    [Theory]
    [InlineData("jojot.db")]
    [InlineData("JOJOT.DB-WAL")]
    [InlineData("jojot.db-shm")]
    [InlineData(".clean-shutdown")]
    [InlineData(".experiment-lock")]
    [InlineData("startup-cache.json")]
    public void RejectsBenchmarkOutputAtReservedDatabasePath(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "av-experiment");
        Assert.Throws<ArgumentException>(() =>
            ExperimentOptions.Parse(["--data-dir", directory, "--benchmark-output", Path.Combine(directory, fileName)]));
    }

    [Theory]
    [InlineData("JoJot")]
    [InlineData("JoJot.Dev")]
    public void RejectsInstalledJoJotData(string folder)
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder);
        Assert.Throws<ArgumentException>(() => ExperimentOptions.Parse(["--data-dir", installed]));
        Assert.Throws<ArgumentException>(() => ExperimentOptions.Parse(["--benchmark-output", Path.Combine(installed, "result.json")]));
    }
}
