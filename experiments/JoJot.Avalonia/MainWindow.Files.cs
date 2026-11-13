using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    private readonly ConditionalWeakTable<NoteTab, FileBaseline> _fileBaselines = new();
    private sealed record FileBaseline(string Path, string? Content, Encoding? Encoding);
    private string? _lastSaveDirectory;

    private void SetFileBaseline(NoteTab note, string path, string? content, Encoding? encoding)
    {
        _fileBaselines.AddOrUpdate(note, new FileBaseline(path, content, encoding));
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
        var preferred = await ReadPreferredEncodingAsync(path);
        var saved = await Task.Run(() => ReadFileBaselineAsync(path, contentLength, preferred));
        if (note.FilePath == path &&
            (!_fileBaselines.TryGetValue(note, out var current) || current.Path != path))
            SetFileBaseline(note, path, saved?.Content, saved?.Encoding);
    }

    private static int FileInspectionLimit(int contentLength) =>
        Math.Max(TextNoteFiles.ImportLimit, (int)Math.Min(int.MaxValue, 4L * contentLength + 4));

    private static async Task<TextNoteFiles.Document?> ReadFileBaselineAsync(string path, int contentLength, Encoding? preferred)
    {
        try { return await TextNoteFiles.ReadAsync(path, FileInspectionLimit(contentLength), preferred).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The note remains available from SQLite even when its external file cannot be read.
            return null;
        }
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
                FileTypeFilter = [FilePickerFileTypes.All],
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
        var files = new List<(string Path, TextNoteFiles.Document Document)>();
        var errorCount = 0;
        string? lastError = null;
        foreach (var path in paths)
        {
            try
            {
                var preferred = await ReadPreferredEncodingAsync(path);
                files.Add((path, await Task.Run(() => TextNoteFiles.ReadAsync(path, preferred: preferred))));
            }
            catch (Exception ex) when (ex is InvalidDataException or TextNoteFiles.FileTooLargeException)
            {
                lastError = ex.Message;
                errorCount++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                lastError = string.Format(Strings.Drop_ReadFailed, Path.GetFileName(path));
                errorCount++;
            }
        }

        if (files.Count > 0)
        {
            await FlushEditsAsync();
            var index = _notes.Count(note => note.Pinned);
            var firstOrder = _notes.Skip(index).Select(note => note.SortOrder).DefaultIfEmpty(0).Min() - files.Count;
            NoteTab? first = null;
            foreach (var (path, document) in files)
            {
                var note = await InsertNoteAsync(Path.GetFileName(path), document.Content, false, firstOrder++, path);
                SetFileBaseline(note, path, document.Content, document.Encoding);
                await RememberEncodingAsync(path, document.Encoding);
                _notes.Insert(index++, note);
                first ??= note;
            }
            SearchBox.Clear();
            ShowNote(first);
        }
        if (errorCount > 0 && lastError is not null)
            ShowInfoToast(files.Count > 0
                ? string.Format(Strings.Drop_PartialSuccess, files.Count, errorCount, lastError) : lastError);
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
                var encoding = await WriteFileAsync(note, existing, content);
                SetFileBaseline(note, existing, content, encoding);
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
                FileTypeChoices = [FilePickerFileTypes.All],
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
        var encoding = await WriteFileAsync(note, target, content);
        var name = Path.GetFileName(target);
        await NoteStore.UpdateNoteFilePathAndNameAsync(note.Id, target, name);
        _lastSaveDirectory = Path.GetDirectoryName(target);
        note.FilePath = target;
        note.Name = name;
        SetFileBaseline(note, target, content, encoding);
        UpdateEditorTitle();
    }

    private async Task<T> ShowFilePickerAsync<T>(Func<Task<T>> picker)
    {
        _filePickerOpen = true;
        try { return await picker(); }
        finally { _filePickerOpen = false; }
    }

    private static string EncodingKey(string path) =>
        "file_encoding_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

    private static async Task<Encoding?> ReadPreferredEncodingAsync(string path)
    {
        var value = await PreferenceStore.GetPreferenceAsync(EncodingKey(path));
        return int.TryParse(value, out var codePage) ? TextNoteFiles.GetLegacyEncoding(codePage) : null;
    }

    private static async Task RememberEncodingAsync(string path, Encoding encoding)
    {
        var key = EncodingKey(path);
        if (TextNoteFiles.GetLegacyEncoding(encoding.CodePage) is not null)
            await PreferenceStore.SetPreferenceAsync(key, encoding.CodePage.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else if (!string.IsNullOrEmpty(await PreferenceStore.GetPreferenceAsync(key)))
            await PreferenceStore.SetPreferenceAsync(key, "");
    }

    private async Task<Encoding> WriteFileAsync(NoteTab note, string path, string content)
    {
        _fileBaselines.TryGetValue(note, out var baseline);
        var source = baseline?.Encoding ?? (note.FilePath is { } sourcePath ? await ReadPreferredEncodingAsync(sourcePath) : null);
        var preferred = await ReadPreferredEncodingAsync(path);
        var fallback = source ?? TextNoteFiles.DefaultEncoding;
        var limit = FileInspectionLimit(Math.Max(content.Length, baseline?.Content?.Length ?? 0));
        var encoding = await Task.Run(() => TextNoteFiles.WriteAsync(path, content, fallback, limit, preferred));
        await RememberEncodingAsync(path, encoding);
        return encoding;
    }
}
