using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;
using JoJot.ViewModels;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow : Window
{
    private readonly ExperimentOptions _options;
    private readonly bool _applyStartupPreference;
    private long? _startupWelcomeNoteId;
    private const string StartupNotePreference = "startup_note";
    private readonly BenchmarkProbe? _benchmark;
    private readonly MainWindowViewModel _viewModel;
    private ObservableCollection<NoteTab> _notes => _viewModel.Tabs;
    private NoteTab? _active => _viewModel.ActiveTab;
    private readonly TaskCompletionSource _readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FileStream? _dataLock;
    private bool _ready;
    private bool _busy = true;
    private bool _refreshing;
    private bool _closeRequested;
    private bool _closing;
    private bool _closed;
    private readonly Queue<string?> _queuedNewTabs = new();
    private TaskCompletionSource? _idleSource;
    internal static Func<MainWindow, Task>? SaveWindowGeometryAsync { get; set; }

    public MainWindow() : this(ExperimentApp.Options.DesktopGuid) { }

    public MainWindow(string desktopGuid) : this(desktopGuid, applyStartupPreference: false) { }

    internal MainWindow(string desktopGuid, bool applyStartupPreference)
    {
        _applyStartupPreference = applyStartupPreference;
        _options = ExperimentApp.Options;
        _benchmark = ExperimentApp.Benchmark;
        _viewModel = new MainWindowViewModel(desktopGuid);
        InitializeComponent();
        StartupTrace.Mark("xaml-ready");
        WindowResizeHitTest.Attach(this);
        WindowChromeTheme.Attach(this);
        Width = _options.Width;
        Height = _options.Height;
        SizeChanged += (_, _) => WorkArea.ColumnDefinitions[0].MaxWidth = Math.Clamp(Bounds.Width - 200, 120, 400);
        if (_benchmark is not null)
        {
            Position = new PixelPoint(80, 80);
            ShowActivated = false;
            RequestedThemeVariant = ThemeVariant.Light;
        }
        if (_options.VisualOutputDirectory is not null)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            Position = new PixelPoint(-12000, -12000);
        }

        Opened += OnOpened;
        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        InitializeEditor();
        InitializeTabInteractions();
        InitializeFileDrop();
    }

    internal string DesktopGuid
    {
        get => _viewModel.DesktopGuid;
        private set => _viewModel.DesktopGuid = value;
    }

    internal Task Ready => _readySource.Task;

    internal void UpdateDesktopTitle(string? desktopName, int? desktopIndex)
    {
        _viewModel.UpdateDesktopInfo(desktopName, desktopIndex);
        Title = _viewModel.WindowTitle;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var firstRender = _benchmark?.RecordFirstRenderAsync(this);
        StartupTrace.Mark("window-opened");
        try
        {
            if (ExperimentApp.ManagesStorageLifetime)
            {
                await ExperimentApp.EnsureStorageAsync();
                _startupWelcomeNoteId = ExperimentApp.StartupWelcomeNoteId;
            }
            else
                await Task.Run(OpenStandaloneStorageAsync);
            if (!await SessionStore.SessionExistsAsync(DesktopGuid))
                await SessionStore.CreateSessionAsync(DesktopGuid, null, null);

            await InitializePanelsAsync();
            StartupTrace.Mark("panels-ready");
            await RestoreTabWidthAsync();
            LocalizeCoreControls();
            await LoadInitialNotesAsync();
            StartupTrace.Mark("notes-ready");
            _ready = true;
            Status.IsVisible = false;
            _busy = false;
            UpdateControls();
            _readySource.TrySetResult();
            StartupTrace.Mark("editor-ready");
            if (_options.CompatibilityExpected is { } expected)
            {
                await ApplyCompatibilityEditAsync(expected, _options.CompatibilityReplacement!);
                _closeRequested = true;
            }
            else if (_options.VisualOutputDirectory is { } visualOutput)
            {
                await CaptureVisualProbeAsync(visualOutput);
                _closeRequested = true;
            }
            else if (_benchmark is null)
                FocusEditorIfIdle();
            else
            {
                if (_active is null)
                    throw new InvalidOperationException("A startup benchmark requires at least one seeded note.");
                await _benchmark.RecordEditorAsync(this, _notes.Count, _active.Name, Editor.Text?.Length ?? 0, firstRender!);
                if (!_options.StayOpen)
                {
                    await ExperimentApp.StartupRecovery;
                    _closeRequested = true;
                }
            }
        }
        catch (Exception ex)
        {
            _readySource.TrySetException(ex);
            ReportError(ex);
            if (_benchmark is not null || _options.CompatibilityExpected is not null || _options.VisualOutputDirectory is not null)
            {
                ExperimentApp.FailExitCode = 1;
                _closeRequested = true;
            }
        }
        finally
        {
            _busy = false;
            UpdateControls();
            DispatchPendingRequests();
        }
    }

    private async Task OpenStandaloneStorageAsync()
    {
        _options.ValidateDataPath();
        AppEnvironment.SetAppDataDirectory(_options.DataDirectory);
        _dataLock = new FileStream(Path.Combine(_options.DataDirectory, ".experiment-lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await DatabaseCore.OpenAsync(Path.Combine(_options.DataDirectory, "jojot.db"));
        await DatabaseCore.EnsureSchemaAsync();
        if (!await DatabaseCore.VerifyIntegrityAsync())
            throw new IOException("The experiment database failed its integrity check.");
        await DatabaseCore.RunPendingMigrationsAsync();
        if (_applyStartupPreference && _options.CompatibilityExpected is null && _options.VisualOutputDirectory is null)
            _startupWelcomeNoteId = await ExperimentApp.InsertWelcomeNoteIfEmptyAsync(DesktopGuid);
    }

    private async Task LoadInitialNotesAsync()
    {
        var loaded = await NoteStore.GetNotesForDesktopAsync(DesktopGuid);
        var (keep, empty) = MainWindowViewModel.SplitEmptyStartupNotes(loaded);
        ReplaceNoteList(keep);
        var welcome = _applyStartupPreference && _startupWelcomeNoteId is { } welcomeId
            ? _notes.FirstOrDefault(note => note.Id == welcomeId) : null;
        var normalStartup = _benchmark is null && _options.CompatibilityExpected is null && _options.VisualOutputDirectory is null;
        if (welcome is not null)
            ShowNote(welcome);
        else if (normalStartup && (_notes.Count == 0 ||
                 (_applyStartupPreference && await PreferenceStore.GetPreferenceAsync(StartupNotePreference) == "new")))
            await CreateNoteCoreAsync();
        else
        {
            var selectedId = normalStartup && _applyStartupPreference
                ? await LoadActiveTabIdAsync() : null;
            ShowNote(_notes.FirstOrDefault(note => note.Id == selectedId) ?? _notes.FirstOrDefault());
        }

        RefreshNoteList();
        _ = LoadBackgroundFileBaselinesAsync();
        if (empty.Count > 0)
            _ = DeleteEmptyNotesAsync(empty);
    }

    private async Task DeleteEmptyNotesAsync(List<NoteTab> empty)
    {
        try { await NoteStore.DeleteNotesAsync(empty.Select(note => note.Id).ToList()); }
        catch (Exception ex) { ReportError(ex); }
    }

    internal async Task ReloadNotesAsync()
    {
        if (!_ready)
            await Ready;
        await WaitForWindowIdleAsync();
        if (_closing || _closed)
            return;
        await RunActionAsync(ReloadNotesCoreAsync, propagateFailure: true);
    }

    private async Task ReloadNotesCoreAsync()
    {
        await FlushEditsAsync();
        var keepId = _active?.Id;
        var rows = await NoteStore.GetNotesForDesktopAsync(DesktopGuid);
        var excluded = _pendingDeletion?.Tabs.Select(note => note.Id).ToHashSet() ?? [];
        var merged = MainWindowViewModel.MergeReloadedTabs(_notes, rows, excluded);
        ReplaceNoteList(merged);
        if (_notes.Count == 0)
            await CreateNoteCoreAsync();
        else
            ShowNote(_notes.FirstOrDefault(note => note.Id == keepId) ?? _notes[0]);
        RefreshNoteList();
        _ = LoadBackgroundFileBaselinesAsync();
    }

    private void ReplaceNoteList(IEnumerable<NoteTab> notes)
    {
        _refreshing = true;
        try
        {
            _notes.Clear();
            foreach (var note in notes)
                _notes.Add(note);
        }
        finally { _refreshing = false; }
    }

    private void UpdateControls()
    {
        WorkArea.IsEnabled = _ready && !_busy;
        AddButton.IsEnabled = WorkArea.IsEnabled;
        Editor.IsEnabled = _active is not null;
        UpdateToolbarState();
    }

    private async Task RunActionAsync(Func<Task> action, bool propagateFailure = false)
    {
        if (!_ready || _busy || _closing)
            return;

        SaveEditorPosition();
        _busy = true;
        UpdateControls();
        Exception? error = null;
        try { await action(); }
        catch (Exception ex)
        {
            error = ex;
            if (propagateFailure)
                throw;
        }
        finally
        {
            _busy = false;
            UpdateControls();
            RefreshNoteList();
            if (error is not null)
                ReportError(error);
            DispatchPendingRequests();
        }
    }

    private Task<bool> ConfirmAsync(string title, Control prompt, string accept)
    {
        var dialog = new Window
        {
            Title = title, Width = 360, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = Strings.Common_Cancel, IsCancel = true };
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
        dialog.Opened += (_, _) => cancel.Focus();
        return dialog.ShowDialog<bool>(this);
    }

    private void ApplyTheme(string? choice)
    {
        RequestedThemeVariant = choice switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private async Task WaitForWindowIdleAsync()
    {
        while (_busy && !_closing)
            await (_idleSource ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    internal async Task FlushWindowAsync()
    {
        await WaitForWindowIdleAsync();
        if (!_ready)
            return;
        CaptureEditorText();
        FlushUndoSnapshot();
        await FlushEditsAsync();
        await CommitPendingDeletionAsync();
        await SaveTabWidthAsync();
        await SaveActiveTabAsync();
        if (SaveWindowGeometryAsync is { } save)
            await save(this);
    }

    private Task<long?> LoadActiveTabIdAsync() => DatabaseCore.RunOnDbThreadAsync(async () =>
    {
        await using var command = DatabaseCore.CreateCommand(
            "SELECT active_tab_id FROM app_state WHERE desktop_guid = @guid;", ("@guid", DesktopGuid));
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is long id ? id : (long?)null;
    });

    private Task SaveActiveTabAsync() => DatabaseCore.ExecuteNonQueryAsync(
        "UPDATE app_state SET active_tab_id = @id WHERE desktop_guid = @guid;",
        ("@id", (object?)_active?.Id ?? DBNull.Value), ("@guid", DesktopGuid));

    internal void FlushForOsShutdown()
    {
        if (!_ready)
            return;
        CaptureEditorText();
        SaveEditorPosition();
        StopEditorTimers();
        // Earlier writes already queued by autosave complete first on the same DB worker.
        // Keep the UI-thread continuation out of this OS shutdown callback.
        foreach (var (note, content) in _dirty.ToArray())
            NoteStore.UpdateNoteContentAsync(note.Id, content).GetAwaiter().GetResult();
        if (_pendingDeletion is { } pending)
            NoteStore.DeleteNotesAsync(pending.Tabs.Select(note => note.Id).ToList()).GetAwaiter().GetResult();
        SaveActiveTabAsync().GetAwaiter().GetResult();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed || e.CloseReason == WindowCloseReason.ApplicationShutdown)
        {
            DismissSidePanel();
            StopEditorTimers();
            return;
        }
        e.Cancel = true;
        if (_closing)
            return;
        if (_busy)
        {
            _closeRequested = true;
            return;
        }

        _closeRequested = false;
        FinishRename(save: false);
        DismissSidePanel();
        SaveEditorPosition();
        _closing = true;
        _busy = true;
        UpdateControls();
        try
        {
            if (_ready)
                await FlushWindowAsync();
            if (_dataLock is not null)
            {
                await DatabaseCore.CloseAsync();
                _dataLock.Dispose();
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            StopEditorTimers();
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

    private void DispatchPendingRequests()
    {
        _idleSource?.TrySetResult();
        _idleSource = null;
        if (_closeRequested)
            Dispatcher.UIThread.Post(Close);
        else if (_ready && !_closing && _queuedNewTabs.TryDequeue(out var initialContent))
            Dispatcher.UIThread.Post(() => RequestNewTab(initialContent));
    }

    private void ReportError(Exception ex)
    {
        Status.Text = ex.Message;
        Status.IsVisible = true;
        Console.Error.WriteLine(ex);
    }

    private void OnPreferencesMenu(object? sender, RoutedEventArgs e) => ShowPreferences();
    private void OnCleanupMenu(object? sender, RoutedEventArgs e) => ShowCleanup();
    private void OnRecoveryMenu(object? sender, RoutedEventArgs e) => ShowRecovery();
    private async void OnExitMenu(object? sender, RoutedEventArgs e)
    {
        try { await ExitUsingMenuAsync(); }
        catch (Exception ex) { ReportError(ex); }
    }

    internal async Task ApplyCompatibilityEditAsync(string expectedContent, string replacement)
    {
        if (_active is null || !Editor.IsEffectivelyEnabled || Editor.Text != expectedContent)
            throw new InvalidOperationException("The compatibility fixture did not load the expected editor content.");
        Editor.Text = replacement;
        await FlushEditsAsync();
    }
}
