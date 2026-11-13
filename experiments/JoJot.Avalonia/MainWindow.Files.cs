using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;
using JoJot.ViewModels;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly FilePickerFileType TextFiles = new("Text files") { Patterns = ["*.txt"] };
    private readonly ConditionalWeakTable<NoteTab, FileBaseline> _fileBaselines = new();
    private sealed record FileBaseline(string Path, string? Content);
    private string? _lastSaveDirectory;

    private void SetFileBaseline(NoteTab note, string path, string? content)
    {
        _fileBaselines.AddOrUpdate(note, new FileBaseline(path, content));
        UpdateFileDirtyState(note);
    }

    private void UpdateFileDirtyState(NoteTab note)
    {
        note.HasUnsavedFileChanges = note.FilePath is { } path &&
            _fileBaselines.TryGetValue(note, out var baseline) &&
            string.Equals(path, baseline.Path, StringComparison.Ordinal) && note.Content != baseline.Content;
    }

    private Task LoadFileBaselinesAsync(IEnumerable<NoteTab> notes) =>
        Task.WhenAll(notes.Where(note => note.IsFileBacked).Select(LoadFileBaselineAsync));

    private async Task LoadFileBaselineAsync(NoteTab note)
    {
        if (note.FilePath is not { } path ||
            (_fileBaselines.TryGetValue(note, out var baseline) && baseline.Path == path))
            return;

        var contentLength = note.Content.Length;
        var saved = await Task.Run(() => ReadFileBaselineAsync(path, contentLength));
        if (note.FilePath == path &&
            (!_fileBaselines.TryGetValue(note, out var current) || current.Path != path))
            SetFileBaseline(note, path, saved);
    }

    private static async Task<string?> ReadFileBaselineAsync(string path, int contentLength)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            // Keep the original text even if the saved file is longer than the SQLite draft.
            // Bound reads of externally replaced files while allowing drafts above the import limit.
            var limit = Math.Max(contentLength, 500 * 1024);
            var buffer = new char[limit + 1];
            var length = await reader.ReadBlockAsync(buffer);
            return length <= limit ? new string(buffer, 0, length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The note remains available from SQLite even when its external file cannot be read.
        }
        return null;
    }

    private bool _filePickerOpen;

    internal bool HasShutdownBlockingInteraction => _filePickerOpen || OwnedWindows.Any(dialog => dialog.IsVisible);

    private void InitializeFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => FileDropHost.IsVisible = false, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnFileDrop, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;
        var canImport = _ready && !_busy;
        FileDropHost.IsVisible = canImport;
        e.DragEffects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        FileDropHost.IsVisible = false;
        if (e.DataTransfer.TryGetFiles() is not { Length: > 0 } files)
            return;
        e.Handled = true;
        await ImportPathsAsync(GetLocalPaths(files));
    }

    private static string[] GetLocalPaths(IEnumerable<IStorageItem> files) =>
        files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();

    private async void OnOpenFiles(object? sender, RoutedEventArgs e) => await OpenFilesAsync();

    private async Task OpenFilesAsync()
    {
        await RunActionAsync(async () =>
        {
            var selected = await ShowFilePickerAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = OpenFilesLabel,
                AllowMultiple = true,
                FileTypeFilter = [TextFiles, FilePickerFileTypes.All],
            }));
            await ImportFilesCoreAsync(GetLocalPaths(selected));
        });
        FocusEditorIfIdle();
    }

    internal async Task ImportPathsAsync(string[] paths)
    {
        await RunActionAsync(() => ImportFilesCoreAsync(paths));
        FocusEditorIfIdle();
    }

    private async Task ImportFilesCoreAsync(string[] paths)
    {
        if (paths.Length == 0)
            return;
        var summary = await FileDropService.ProcessDroppedFilesAsync(paths);
        if (summary.ValidFiles.Count > 0)
        {
            await FlushEditsAsync();
            var index = _notes.Count(note => note.Pinned);
            var firstOrder = _notes.Skip(index).Select(note => note.SortOrder).DefaultIfEmpty(0).Min() - summary.ValidFiles.Count;
            NoteTab? first = null;
            foreach (var file in summary.ValidFiles)
            {
                var note = await InsertNoteAsync(file.FileName, file.Content!, false, firstOrder++, file.FilePath);
                SetFileBaseline(note, file.FilePath!, file.Content!);
                _notes.Insert(index++, note);
                first ??= note;
            }
            SearchBox.Clear();
            ShowNote(first);
        }
        if (summary.CombinedErrorMessage is { } error)
            ShowInfoToast(error);
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_active is { } note)
            await SaveNoteAsync(note, false);
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (_active is { } note)
            await SaveNoteAsync(note, true);
    }

    private async Task SaveNoteAsync(NoteTab note, bool forceDialog)
    {
        await RunActionAsync(async () =>
        {
            await SelectForActionAsync(note);
            await FlushEditsAsync();
            if (!forceDialog && note.FilePath is { } existing)
            {
                var content = note.Content;
                await WriteFileAsync(existing, content);
                SetFileBaseline(note, existing, content);
                return;
            }
            var directory = note.FilePath is { } sourcePath ? Path.GetDirectoryName(sourcePath) : _lastSaveDirectory;
            var suggested = directory is null ? null : await StorageProvider.TryGetFolderFromPathAsync(directory);
            var selected = await ShowFilePickerAsync(() => StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.Ctx_SaveAs,
                SuggestedStartLocation = suggested,
                SuggestedFileName = note.FilePath is { } path ? Path.GetFileName(path) : MainWindowViewModel.GetDefaultFilename(note),
                DefaultExtension = "txt",
                FileTypeChoices = [TextFiles, FilePickerFileTypes.All],
                ShowOverwritePrompt = true,
            }));
            if (selected?.TryGetLocalPath() is { } target)
                await SaveNoteToPathCoreAsync(note, target);
        });
        FocusEditorIfIdle();
    }

    internal async Task SaveNoteToPathCoreAsync(NoteTab note, string target)
    {
        var content = note.Content;
        await WriteFileAsync(target, content);
        var name = Path.GetFileName(target);
        await NoteStore.UpdateNoteFilePathAndNameAsync(note.Id, target, name);
        _lastSaveDirectory = Path.GetDirectoryName(target);
        note.FilePath = target;
        note.Name = name;
        SetFileBaseline(note, target, content);
        UpdateEditorTitle();
    }

    private async Task<T> ShowFilePickerAsync<T>(Func<Task<T>> picker)
    {
        _filePickerOpen = true;
        try { return await picker(); }
        finally { _filePickerOpen = false; }
    }

    private static Task WriteFileAsync(string path, string content) => File.WriteAllTextAsync(path, content, Utf8Bom);
}
