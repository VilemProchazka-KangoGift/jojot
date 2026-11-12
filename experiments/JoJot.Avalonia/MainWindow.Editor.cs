using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private readonly Dictionary<NoteTab, string> _dirty = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _checkpointTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private NoteTab? _undoPending;
    private bool _applyingHistory;
    private double _lastSavedTabWidth;
    private ScrollViewer? _editorScroll;

    private void InitializeEditor()
    {
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            try { await FlushEditsAsync(); }
            catch (Exception ex) { ReportError(ex); }
        };
        _undoTimer.Tick += (_, _) => FlushUndoSnapshot();
        _checkpointTimer.Tick += (_, _) =>
        {
            if (_active is { } note && UndoManager.Instance.IsCheckpointDue(note.Id))
                UndoManager.Instance.PushCheckpoint(note.Id, Editor.Text ?? "", Editor.CaretIndex);
        };
        Editor.TemplateApplied += (_, args) =>
            _editorScroll = args.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        Editor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel);
        Editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Tunnel);
        TabSplitter.DragCompleted += async (_, _) =>
        {
            try { await SaveTabWidthAsync(); }
            catch (Exception ex) { ReportError(ex); }
        };
        Activated += (_, _) =>
        {
            UndoManager.Instance.SetActiveTab(_active?.Id);
            foreach (var note in _notes)
                note.RefreshStaleness();
        };
    }

    private void StopEditorTimers()
    {
        _saveTimer.Stop();
        _undoTimer.Stop();
        _checkpointTimer.Stop();
        _findTimer.Stop();
        _tabSearchTimer.Stop();
        _toastTimer.Stop();
        _findGeneration++;
    }

    internal void SetAutosaveInterval(int milliseconds)
    {
        _saveTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 100, 60_000));
    }

    internal void SetEditorWrap(bool wrap)
    {
        Editor.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(Editor, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    private async Task RestoreTabWidthAsync()
    {
        var value = await PreferenceStore.GetPreferenceAsync("tab_panel_width");
        if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var width) &&
            double.IsFinite(width))
            WorkArea.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(width, 120, 340));
        _lastSavedTabWidth = WorkArea.ColumnDefinitions[0].Width.Value;
    }

    private async Task SaveTabWidthAsync()
    {
        var column = WorkArea.ColumnDefinitions[0];
        var width = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
        if (width <= 0 || Math.Abs(width - _lastSavedTabWidth) < 0.5)
            return;
        await PreferenceStore.SetPreferenceAsync("tab_panel_width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _lastSavedTabWidth = width;
    }

    private void ShowNote(NoteTab? note)
    {
        if (ReferenceEquals(_active, note) && note is not null)
            return;
        var outgoing = _active;
        CaptureEditorText();
        FlushUndoSnapshot();
        if (!_busy)
            SaveEditorPosition();
        _refreshing = true;
        try
        {
            _viewModel.ActiveTab = note;
            Editor.Text = note?.Content;
            var cursor = Math.Clamp(note?.CursorPosition ?? 0, 0, Editor.Text?.Length ?? 0);
            Editor.CaretIndex = cursor;
            Editor.SelectionStart = cursor;
            Editor.SelectionEnd = cursor;
            if (note is not null)
            {
                if (UndoManager.Instance.GetStack(note.Id) is null)
                    UndoManager.Instance.PushInitialContent(note.Id, note.Content, cursor);
                UndoManager.Instance.SetActiveTab(note.Id);
                if (_benchmark is null && (outgoing is not null || note.EditorScrollOffset != 0))
                    Dispatcher.UIThread.Post(() => RestoreEditorPosition(note), DispatcherPriority.Background);
            }
            else
                UndoManager.Instance.SetActiveTab(null);
            UpdateEditorTitle();
        }
        finally { _refreshing = false; }
        UpdateControls();
        ScheduleFindRefresh();
    }

    private void SaveEditorPosition()
    {
        if (_active is not { } note)
            return;
        note.CursorPosition = Editor.CaretIndex;
        if (GetEditorScroll() is { } scroll)
            note.EditorScrollOffset = (int)scroll.Offset.Y;
    }

    private ScrollViewer? GetEditorScroll() =>
        _editorScroll ??= Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void RestoreEditorPosition(NoteTab note)
    {
        if (ReferenceEquals(_active, note) && GetEditorScroll() is { } scroll)
            scroll.Offset = new Vector(0, note.EditorScrollOffset);
    }

    private void UpdateEditorTitle() => NoteTitle.Text = _active?.DisplayLabel ?? "";

    private void FocusEditorIfIdle()
    {
        if (!_closing && Editor.IsEffectivelyEnabled && !SearchBox.IsKeyboardFocusWithin &&
            _renameTab is null && !PanelHost.IsVisible && !FindHost.IsVisible)
            Editor.Focus();
    }

    private void OnEditorChanged(object? sender, TextChangedEventArgs e) => CaptureEditorText();

    private void CaptureEditorText()
    {
        if (_refreshing || _active is not { } note)
            return;
        var text = Editor.Text ?? "";
        if (text == note.Content)
            return;

        note.Content = text;
        note.UpdatedAt = DateTime.Now;
        _dirty[note] = text;
        UpdateEditorTitle();
        UpdateStatus();
        _saveTimer.Stop();
        _saveTimer.Start();
        if (!_applyingHistory)
        {
            _undoPending = note;
            _undoTimer.Stop();
            _undoTimer.Start();
        }
        if (!_checkpointTimer.IsEnabled && _benchmark is null)
            _checkpointTimer.Start();
        UpdateToolbarState();
        ScheduleFindRefresh();
    }

    private async Task FlushEditsAsync()
    {
        CaptureEditorText();
        _saveTimer.Stop();
        await _saveGate.WaitAsync();
        try
        {
            foreach (var (note, text) in _dirty.ToArray())
            {
                await NoteStore.UpdateNoteContentAsync(note.Id, text);
                if (_dirty.TryGetValue(note, out var latest) && latest == text)
                    _dirty.Remove(note);
            }
            UpdateStatus();
        }
        finally { _saveGate.Release(); }
    }

    private void FlushUndoSnapshot()
    {
        _undoTimer.Stop();
        if (_undoPending is not { } note)
            return;
        _undoPending = null;
        UndoManager.Instance.PushSnapshot(note.Id, note.Content,
            ReferenceEquals(_active, note) ? Editor.CaretIndex : note.CursorPosition);
        UpdateToolbarState();
    }

    private void MoveHistory(bool redo)
    {
        if (_active is not { } note || _busy)
            return;
        CaptureEditorText();
        FlushUndoSnapshot();
        var snapshot = redo ? UndoManager.Instance.Redo(note.Id) : UndoManager.Instance.Undo(note.Id);
        if (snapshot is { } entry)
        {
            _applyingHistory = true;
            try
            {
                Editor.Text = entry.Content;
                Editor.CaretIndex = Math.Clamp(entry.CursorPosition, 0, entry.Content.Length);
                CaptureEditorText();
            }
            finally { _applyingHistory = false; }
        }
        UpdateToolbarState();
        FocusEditorIfIdle();
    }

    private void UpdateToolbarState()
    {
        var note = _active;
        RenameButton.IsEnabled = note is { IsFileBacked: false };
        PinButton.IsEnabled = note is not null;
        var pinned = note?.Pinned == true;
        PinButton.Content = pinned ? "\uE77A" : "\uE718";
        ToolTip.SetTip(PinButton, pinned ? Strings.Tooltip_Unpin : Strings.Tooltip_Pin);
        AutomationProperties.SetName(PinButton, pinned ? Strings.Ctx_Unpin : Strings.Ctx_Pin);
        CloneButton.IsEnabled = note is not null;
        CopyButton.IsEnabled = note is not null && note.Content.Length > 0;
        PasteButton.IsEnabled = note is not null;
        SaveButton.IsEnabled = note is not null;
        SaveAsButton.IsEnabled = note is not null;
        SaveAsButton.IsVisible = note is { IsFileBacked: true };
        FindButton.IsEnabled = note is not null;
        DeleteButton.IsEnabled = note is not null;
        UndoButton.IsEnabled = note is not null && (_undoPending == note || UndoManager.Instance.CanUndo(note.Id));
        RedoButton.IsEnabled = note is not null && _undoPending != note && UndoManager.Instance.CanRedo(note.Id);
    }

    private async Task CopyNoteAsync()
    {
        var text = string.IsNullOrEmpty(Editor.SelectedText) ? Editor.Text : Editor.SelectedText;
        if (!string.IsNullOrEmpty(text) && Clipboard is { } clipboard)
        {
            try { await clipboard.SetTextAsync(text); }
            catch (Exception ex) { ReportError(ex); }
        }
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => MoveHistory(false);
    private void OnRedo(object? sender, RoutedEventArgs e) => MoveHistory(true);
    private async void OnCopy(object? sender, RoutedEventArgs e) => await CopyNoteAsync();
    private void OnPaste(object? sender, RoutedEventArgs e) => Editor.Paste();

    private void OnEditorPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.ClickCount != 3 || !e.GetCurrentPoint(Editor).Properties.IsLeftButtonPressed)
            return;
        var presenter = Editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        var text = Editor.Text ?? "";
        var position = presenter is null ? Editor.CaretIndex
            : presenter.TextLayout.HitTestPoint(e.GetPosition(presenter)).TextPosition;
        var start = Math.Clamp(position, 0, text.Length);
        var end = start;
        while (start > 0 && text[start - 1] is not ('\n' or '\r'))
            start--;
        while (end < text.Length && text[end] is not ('\n' or '\r'))
            end++;
        Editor.Focus();
        Editor.CaretIndex = end;
        Editor.SelectionStart = start;
        Editor.SelectionEnd = end;
        e.Handled = true;
    }

    private async void OnEditorWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            return;
        e.Handled = true;
        await ChangeEditorFontAsync(Math.Sign(e.Delta.Y));
    }
}
