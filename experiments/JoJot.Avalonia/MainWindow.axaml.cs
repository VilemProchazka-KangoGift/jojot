using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using JoJot.Models;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow : Window
{
    private readonly ExperimentOptions _options;
    private readonly BenchmarkProbe? _benchmark;
    private readonly List<NoteTab> _notes = [];
    private readonly Dictionary<NoteTab, string> _dirty = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FileStream? _dataLock;
    private NoteTab? _active;
    private bool _ready;
    private bool _busy = true;
    private bool _refreshing;
    private bool _closeRequested;
    private bool _closing;
    private bool _closed;

    public MainWindow()
    {
        _options = ExperimentApp.Options;
        _benchmark = ExperimentApp.Benchmark;
        InitializeComponent();
        Width = _options.Width;
        Height = _options.Height;
        if (_benchmark is not null)
        {
            Position = new PixelPoint(80, 80);
            ShowActivated = false;
            RequestedThemeVariant = ThemeVariant.Light;
        }

        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            try { await FlushEditsAsync(); }
            catch (Exception ex) { ReportError(ex); }
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var firstRender = _benchmark?.RecordFirstRenderAsync(this);
        try
        {
            await Task.Run(OpenStorageAsync);
            ApplyTheme(await PreferenceStore.GetPreferenceAsync("theme"));
            _notes.AddRange(await NoteStore.GetNotesForDesktopAsync(_options.DesktopGuid));
            _ready = true;
            ShowNote(_notes.FirstOrDefault());
            RefreshNoteList();
            _busy = false;
            UpdateControls();
            if (_benchmark is null)
                Editor.Focus();
            else
            {
                if (_active is null)
                    throw new InvalidOperationException("A startup benchmark requires at least one seeded note.");
                await _benchmark.RecordEditorAsync(this, _notes.Count, _active.Name, Editor.Text?.Length ?? 0, firstRender!);
                if (!_options.StayOpen)
                    _closeRequested = true;
            }
        }
        catch (Exception ex)
        {
            _busy = false;
            UpdateControls();
            ReportError(ex);
            if (_benchmark is not null)
            {
                Environment.ExitCode = 1;
                _closeRequested = true;
            }
        }
        finally
        {
            _busy = false;
            CloseIfRequested();
        }
    }

    private async Task OpenStorageAsync()
    {
        _options.ValidateDataPath();
        AppEnvironment.SetAppDataDirectory(_options.DataDirectory);
        _dataLock = new FileStream(Path.Combine(_options.DataDirectory, ".experiment-lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = Path.Combine(_options.DataDirectory, "jojot.db");
        await DatabaseCore.OpenAsync(path);
        await DatabaseCore.EnsureSchemaAsync();
        if (!await DatabaseCore.VerifyIntegrityAsync())
            throw new IOException("The experiment database failed its integrity check.");
        await DatabaseCore.RunPendingMigrationsAsync();
        if (!await SessionStore.SessionExistsAsync(_options.DesktopGuid))
            await SessionStore.CreateSessionAsync(_options.DesktopGuid, null, null);
    }

    private void UpdateControls()
    {
        WorkArea.IsEnabled = _ready && !_busy;
        AddButton.IsEnabled = WorkArea.IsEnabled;
        ThemePicker.IsEnabled = WorkArea.IsEnabled;
        Editor.IsEnabled = _active is not null;
        RenameButton.IsEnabled = _active is { IsFileBacked: false };
        PinButton.IsEnabled = _active is not null;
        DeleteButton.IsEnabled = _active is not null;
    }

    private void ShowNote(NoteTab? note)
    {
        _refreshing = true;
        try
        {
            _active = note;
            Editor.Text = note?.Content;
            Editor.CaretIndex = 0;
            NoteTitle.Text = note?.DisplayLabel ?? "Select or create a note";
            PinButton.Content = note?.Pinned == true ? "Unpin" : "Pin";
        }
        finally { _refreshing = false; }
        UpdateControls();
    }

    private void RefreshNoteList()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var matches = _notes
            .Where(note => query.Length == 0 || note.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                note.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.Pinned).ThenBy(note => note.SortOrder).ThenBy(note => note.Id)
            .ToArray();

        _refreshing = true;
        try
        {
            NoteList.ItemsSource = matches;
            NoteList.SelectedItem = matches.Contains(_active) ? _active : null;
        }
        finally { _refreshing = false; }
        UpdateStatus();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_ready)
            RefreshNoteList();
    }

    private async void OnNoteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _busy || NoteList.SelectedItem is not NoteTab note || note == _active)
            return;
        await RunActionAsync(async () =>
        {
            await FlushEditsAsync();
            ShowNote(note);
        });
        if (!_closing && ReferenceEquals(_active, note))
            Editor.Focus();
    }

    private void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CaptureEditorText())
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool CaptureEditorText()
    {
        if (_refreshing || _active is not { } note)
            return false;
        var text = Editor.Text ?? "";
        if (text == note.Content)
            return false;

        note.Content = text;
        _dirty[note] = text;
        NoteTitle.Text = note.DisplayLabel;
        UpdateStatus();
        return true;
    }

    private async Task FlushEditsAsync()
    {
        // TextBox can defer its TextChanged event. Snapshot the value before
        // switching tabs or closing, even if that event has yet to run.
        CaptureEditorText();
        _saveTimer.Stop();
        await _saveGate.WaitAsync();
        try
        {
            foreach (var (note, text) in _dirty.ToArray())
            {
                await NoteStore.UpdateNoteContentAsync(note.Id, text);
                if (_dirty[note] == text)
                    _dirty.Remove(note);
            }
            UpdateStatus();
        }
        finally { _saveGate.Release(); }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (!_ready || _busy || _closing)
            return;

        _busy = true;
        UpdateControls();
        Exception? error = null;
        try { await action(); }
        catch (Exception ex) { error = ex; }
        finally
        {
            _busy = false;
            UpdateControls();
            RefreshNoteList();
            if (error is not null)
                ReportError(error);
            CloseIfRequested();
        }
    }

    private async void OnAdd(object? sender, RoutedEventArgs e) => await AddNoteAsync();

    private async Task AddNoteAsync()
    {
        await RunActionAsync(async () =>
        {
            await FlushEditsAsync();
            var order = _notes.Count == 0 ? 0 : checked(_notes.Max(note => note.SortOrder) + 1);
            var id = await NoteStore.InsertNoteAsync(_options.DesktopGuid, null, "", false, order);
            var now = DateTime.Now;
            var note = new NoteTab { Id = id, DesktopGuid = _options.DesktopGuid, SortOrder = order, CreatedAt = now, UpdatedAt = now };
            _notes.Add(note);
            SearchBox.Clear();
            ShowNote(note);
        });
        if (!_closing && Editor.IsEnabled)
            Editor.Focus();
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_active is not { IsFileBacked: false } note)
            return;
        await RunActionAsync(async () =>
        {
            var name = new TextBox { Text = note.Name, PlaceholderText = "Use the first line of text", MaxLength = 120 };
            if (!await ConfirmAsync("Rename note", name, "Save"))
                return;
            var value = string.IsNullOrWhiteSpace(name.Text) ? null : name.Text.Trim();
            await NoteStore.UpdateNoteNameAsync(note.Id, value);
            note.Name = value;
            NoteTitle.Text = note.DisplayLabel;
        });
    }

    private async void OnPin(object? sender, RoutedEventArgs e)
    {
        if (_active is not { } note)
            return;
        await RunActionAsync(async () =>
        {
            await NoteStore.UpdateNotePinnedAsync(note.Id, !note.Pinned);
            note.Pinned = !note.Pinned;
            PinButton.Content = note.Pinned ? "Unpin" : "Pin";
        });
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_active is not { } note)
            return;
        await RunActionAsync(async () =>
        {
            if (!await ConfirmAsync("Delete note",
                new TextBlock { Text = $"Permanently delete “{note.DisplayLabel}” from this experiment?", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                "Delete"))
                return;

            await FlushEditsAsync();
            await NoteStore.DeleteNoteAsync(note.Id);
            _notes.Remove(note);
            ShowNote(_notes.OrderByDescending(item => item.Pinned).ThenBy(item => item.SortOrder).FirstOrDefault());
        });
    }

    private Task<bool> ConfirmAsync(string title, Control prompt, string accept)
    {
        var dialog = new Window
        {
            Title = title, Width = 340, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var submit = new Button { Content = accept, IsDefault = true };
        cancel.Click += (_, _) => dialog.Close(false);
        submit.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 14,
            Children =
            {
                prompt,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                    Children = { cancel, submit },
                },
            },
        };
        dialog.Opened += (_, _) =>
        {
            if (prompt is TextBox name)
            {
                name.Focus();
                name.SelectAll();
            }
            else
                cancel.Focus();
        };
        return dialog.ShowDialog<bool>(this);
    }

    private async void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _busy)
            return;
        var choice = ThemePicker.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        await RunActionAsync(async () =>
        {
            await PreferenceStore.SetPreferenceAsync("theme", choice);
            ApplyTheme(choice);
        });
    }

    private void ApplyTheme(string? choice)
    {
        var theme = choice switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        RequestedThemeVariant = theme;
        ThemePicker.SelectedIndex = theme == ThemeVariant.Light ? 1 : theme == ThemeVariant.Dark ? 2 : 0;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || !_ready || _busy)
            return;

        switch (e.Key)
        {
            case Key.T:
                e.Handled = true;
                await AddNoteAsync();
                break;
            case Key.K:
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.S:
                e.Handled = true;
                await RunActionAsync(FlushEditsAsync);
                break;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed)
            return;
        e.Cancel = true;
        if (_closing)
            return;
        if (_busy)
        {
            _closeRequested = true;
            return;
        }

        _closeRequested = false;
        _closing = true;
        _busy = true;
        UpdateControls();
        try
        {
            await FlushEditsAsync();
            if (_dataLock is not null)
            {
                await DatabaseCore.CloseAsync();
                _dataLock.Dispose();
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            _closed = true;
            Close();
        }
        catch (Exception ex)
        {
            _closing = false;
            _busy = false;
            UpdateControls();
            ReportError(ex);
        }
    }

    private void CloseIfRequested()
    {
        if (_closeRequested)
            Dispatcher.UIThread.Post(Close);
    }

    private void UpdateStatus()
    {
        var visible = NoteList.ItemCount;
        var count = visible == _notes.Count
            ? visible == 1 ? "1 note" : $"{visible} notes"
            : $"{visible} of {_notes.Count} notes";
        Status.Text = _dirty.Count == 0 ? count : $"{count} · Unsaved changes";
    }

    private void ReportError(Exception ex)
    {
        Status.Text = ex.Message;
        Console.Error.WriteLine(ex);
    }
}
