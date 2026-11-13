using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
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
    private NoteTab? _renameTab;
    private TextBox? _renameBox;
    private TextBlock? _renamedTitle;
    private PendingTabDeletion? _pendingDeletion;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Action? _toastUndo;
    private readonly DispatcherTimer _tabSearchTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private sealed record PendingTabDeletion(List<NoteTab> Tabs, List<int> OriginalIndexes);

    private void InitializeTabInteractions()
    {
        NoteList.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
        NoteList.AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel);
        NoteList.AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel);
        NoteList.AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (e.Source == NoteList)
                ResetTabDrag();
        });
        NoteList.AddHandler(ContextRequestedEvent, OnTabContextRequested);
        NoteList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null &&
                NoteFromSource(e.Source) is { } note)
            {
                e.Handled = true;
                BeginRename(note);
            }
        };
        _tabSearchTimer.Tick += (_, _) =>
        {
            _tabSearchTimer.Stop();
            RefreshNoteList();
        };
        _toastTimer.Tick += async (_, _) =>
        {
            if (_busy)
                return;
            _toastTimer.Stop();
            if (_pendingDeletion is null)
                HideToast();
            else
                await RunActionAsync(CommitPendingDeletionAsync);
        };
    }

    private void RefreshNoteList()
    {
        _viewModel.SearchText = SearchBox.Text?.Trim() ?? "";
        _viewModel.UpdateZoneSeparators();
        var matches = _viewModel.FilteredTabs;
        _refreshing = true;
        try
        {
            if (!ReferenceEquals(NoteList.ItemsSource, matches))
                NoteList.ItemsSource = matches;
            NoteList.SelectedItem = _active is { } note && matches.Contains(note) ? note : null;
        }
        finally { _refreshing = false; }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var visible = NoteList.ItemCount;
        var count = visible == _notes.Count
            ? CoreLabel(visible == 1 ? "1 note" : $"{visible} notes",
                $"{visible} {PanelLanguage.Plural("poznámka", "poznámky", "poznámek", visible)}")
            : CoreLabel($"{visible} of {_notes.Count} notes", $"{visible} z {_notes.Count} poznámek");
        global::Avalonia.Automation.AutomationProperties.SetHelpText(NoteList, count);
        if (_ready)
            Status.IsVisible = false;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        FinishRename(save: false);
        _tabSearchTimer.Stop();
        _tabSearchTimer.Start();
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
        FocusEditorIfIdle();
    }

    internal void RequestNewTab(string? initialContent = null)
    {
        if (_closing || _closed || Ready.IsFaulted)
            return;
        if (!_ready || _busy)
        {
            _queuedNewTabs.Enqueue(initialContent);
            return;
        }
        _ = AddNoteAsync(initialContent);
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => RequestNewTab();

    private async Task AddNoteAsync(string? initialContent)
    {
        NoteTab? newTab = null;
        await RunActionAsync(async () =>
        {
            await FlushEditsAsync();
            SearchBox.Clear();
            await CreateNoteCoreAsync(initialContent);
            newTab = _active;
        });
        DismissSidePanel();
        if (FindHost.IsVisible)
            HideFindPanel();
        if (!_closing && Editor.IsEffectivelyEnabled)
            Editor.Focus();

        if (newTab is not null)
            Dispatcher.UIThread.Post(() => ScrollNewTabToTop(newTab), DispatcherPriority.Loaded);
    }

    private void ScrollNewTabToTop(NoteTab note)
    {
        if (_closing || !ReferenceEquals(_active, note))
            return;

        NoteList.ScrollIntoView(note);
        if (NoteList.ContainerFromItem(note) is not ListBoxItem row ||
            NoteList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll ||
            scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault() is not { } viewport ||
            row.TranslatePoint(default, viewport) is not { } position)
            return;

        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + position.Y);
    }

    private async Task CreateNoteCoreAsync(string? initialContent = null)
    {
        var (placeholder, index, sortOrder) = _viewModel.GetNewTabPosition();
        if (placeholder is not null)
        {
            ShowNote(placeholder);
            if (!string.IsNullOrEmpty(initialContent))
            {
                Editor.Text = initialContent;
                await FlushEditsAsync();
            }
            return;
        }
        var note = await InsertNoteAsync(null, initialContent ?? "", false, sortOrder);
        _viewModel.InsertNewTab(note, index);
        ShowNote(note);
    }

    private async Task<NoteTab> InsertNoteAsync(string? name, string content, bool pinned, int sortOrder, string? path = null)
    {
        var id = await NoteStore.InsertNoteAsync(DesktopGuid, name, content, pinned, sortOrder, path);
        var now = DateTime.Now;
        return new NoteTab
        {
            Id = id, DesktopGuid = DesktopGuid, Name = name, Content = content, Pinned = pinned, SortOrder = sortOrder,
            FilePath = path, CreatedAt = now, UpdatedAt = now,
        };
    }

    private async Task SelectForActionAsync(NoteTab note)
    {
        if (note == _active)
            return;
        await FlushEditsAsync();
        ShowNote(note);
        RefreshNoteList();
    }

    private async Task TogglePinAsync(NoteTab note)
    {
        await RunActionAsync(async () =>
        {
            await SelectForActionAsync(note);
            var pinned = !note.Pinned;
            await NoteStore.UpdateNotePinnedAsync(note.Id, pinned);
            note.Pinned = pinned;
            _refreshing = true;
            try { _viewModel.ReorderAfterPinToggle(note); }
            finally { _refreshing = false; }
            await SaveTabOrderAsync();
        });
        FocusEditorIfIdle();
    }

    private async Task CloneNoteAsync(NoteTab note)
    {
        await RunActionAsync(async () =>
        {
            await SelectForActionAsync(note);
            await FlushEditsAsync();
            var previousOrder = _notes.Select(item => (item, item.SortOrder)).ToArray();
            var (index, order) = _viewModel.GetClonePosition(note);
            NoteTab clone;
            try { clone = await InsertNoteAsync(note.Name, note.Content, note.Pinned, order); }
            catch
            {
                foreach (var (item, sortOrder) in previousOrder)
                    item.SortOrder = sortOrder;
                throw;
            }
            _viewModel.InsertNewTab(clone, index);
            SearchBox.Clear();
            ShowNote(clone);
            await SaveTabOrderAsync();
        });
        FocusEditorIfIdle();
    }

    private Task SaveTabOrderAsync() =>
        NoteStore.UpdateNoteSortOrdersAsync(_notes.Select(note => (note.Id, note.SortOrder)));

    private void BeginRename(NoteTab note)
    {
        if (!_ready || _busy || note.IsFileBacked)
            return;
        FinishRename(save: false);
        NoteList.ScrollIntoView(note);
        Dispatcher.UIThread.Post(() => ShowRenameBox(note), DispatcherPriority.Loaded);
    }

    private void ShowRenameBox(NoteTab note)
    {
        if (_busy || !_notes.Contains(note) || NoteList.ContainerFromItem(note) is not ListBoxItem item)
            return;
        var title = item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Name == "TabLabel");
        if (title?.Parent is not StackPanel host)
            return;
        var box = _renameBox ??= CreateRenameBox();
        _renameTab = note;
        _renamedTitle = title;
        box.Text = note.Name ?? "";
        box.PlaceholderText = note.DisplayLabel;
        title.IsVisible = false;
        host.Children.Insert(0, box);
        box.Focus();
        box.SelectAll();
    }

    private TextBox CreateRenameBox()
    {
        var box = new TextBox { Name = "InlineRenameBox", Padding = new Thickness(3, 1), MaxLength = 120 };
        box.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Escape))
                return;
            args.Handled = true;
            FinishRename(save: args.Key == Key.Enter);
            FocusEditorIfIdle();
        };
        box.LostFocus += (_, _) => FinishRename(save: true);
        return box;
    }

    private void FinishRename(bool save)
    {
        if (_renameTab is not { } note || _renameBox is not { } box)
            return;
        _renameTab = null;
        if (box.Parent is StackPanel host)
            host.Children.Remove(box);
        if (_renamedTitle is { } title)
            title.IsVisible = true;
        _renamedTitle = null;
        if (save)
            _ = RenameNoteAsync(note, box.Text);
    }

    internal Task RenameNoteAsync(NoteTab note, string? name) => RunActionAsync(async () =>
    {
        if (note.IsFileBacked)
            return;
        var value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await NoteStore.UpdateNoteNameAsync(note.Id, value);
        note.Name = value;
        UpdateEditorTitle();
    });

    private Task DeleteNoteWithUndoAsync(NoteTab note) =>
        DeleteWithUndoAsync([note], includePinned: true);

    internal Task DeleteNotesWithUndoAsync(IEnumerable<NoteTab> candidates) =>
        DeleteWithUndoAsync(candidates, includePinned: false);

    private async Task DeleteWithUndoAsync(IEnumerable<NoteTab> candidates, bool includePinned)
    {
        var tabs = candidates.Where(_notes.Contains).Where(note => includePinned || !note.Pinned).Distinct().ToList();
        if (tabs.Count == 0)
            return;
        await RunActionAsync(async () =>
        {
            await FlushEditsAsync();
            await CommitPendingDeletionAsync();
            var indexes = tabs.Select(_notes.IndexOf).ToList();
            NoteTab? next = null;
            _refreshing = true;
            try
            {
                if (includePinned)
                    next = _viewModel.RemoveTab(tabs[0]);
                else
                    (_, _, next) = _viewModel.RemoveMultiple(tabs);
            }
            finally { _refreshing = false; }
            _pendingDeletion = new PendingTabDeletion(tabs, indexes);
            if (_active is null || tabs.Contains(_active))
            {
                if (next is null && _notes.Count > 0)
                {
                    SearchBox.Clear();
                    next = _notes[0];
                }
                ShowNote(next);
            }
            var message = tabs.Count == 1
                ? string.Format(Strings.Toast_Deleted, $"“{tabs[0].DisplayLabel}”")
                : string.Format(PanelLanguage.Plural(Strings.Toast_NotesDeleted_One,
                    Strings.Toast_NotesDeleted_Few, Strings.Toast_NotesDeleted, tabs.Count), tabs.Count);
            ShowToast(message, RestoreDeletedNotes);
            if (_notes.Count == 0)
                await CreateNoteCoreAsync();
        });
        FocusEditorIfIdle();
    }

    private async Task CommitPendingDeletionAsync()
    {
        if (_pendingDeletion is not { } pending)
            return;
        await NoteStore.DeleteNotesAsync(pending.Tabs.Select(note => note.Id).ToList());
        foreach (var note in pending.Tabs)
            UndoManager.Instance.RemoveStack(note.Id);
        _pendingDeletion = null;
        HideToast();
    }

    private void RestoreDeletedNotes()
    {
        if (_busy || _pendingDeletion is not { } pending)
            return;
        _pendingDeletion = null;
        _refreshing = true;
        try { _viewModel.RestoreTabs(pending.Tabs, pending.OriginalIndexes); }
        finally { _refreshing = false; }
        SearchBox.Clear();
        ShowNote(pending.Tabs[0]);
        RefreshNoteList();
        HideToast();
        FocusEditorIfIdle();
    }

    public void ShowInfoToast(string message)
    {
        if (_pendingDeletion is not null)
        {
            Status.Text = message;
            Status.IsVisible = true;
        }
        else
            ShowToast(message, null);
    }

    private void ShowToast(string message, Action? undo)
    {
        ToastText.Text = message;
        UndoToastButton.IsVisible = undo is not null;
        _toastUndo = undo;
        ToastHost.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        _toastUndo = null;
        ToastHost.IsVisible = false;
    }

    private void OnUndoToast(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var undo = _toastUndo;
        HideToast();
        undo?.Invoke();
    }

    private async void OnDismissToast(object? sender, RoutedEventArgs e)
    {
        if (_pendingDeletion is null)
            HideToast();
        else
            await RunActionAsync(CommitPendingDeletionAsync);
    }

    private static NoteTab? NoteFromSource(object? source) => TabContainerFromSource(source)?.DataContext as NoteTab;

    private static ListBoxItem? TabContainerFromSource(object? source) =>
        source as ListBoxItem ?? (source as Visual)?.FindAncestorOfType<ListBoxItem>();

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_busy || TabContainerFromSource(e.Source) is not { DataContext: NoteTab note } item)
            return;
        var menu = new ContextMenu();
        menu.Items.Add(TabMenuItem(Strings.Ctx_Rename, "F2", () =>
        {
            BeginRename(note);
            return Task.CompletedTask;
        }, !note.IsFileBacked));
        menu.Items.Add(TabMenuItem(note.Pinned ? Strings.Ctx_Unpin : Strings.Ctx_Pin, "Ctrl+P", () => TogglePinAsync(note)));
        menu.Items.Add(TabMenuItem(Strings.Ctx_Clone, "Ctrl+K", () => CloneNoteAsync(note)));
        menu.Items.Add(TabMenuItem(Strings.Ctx_Save, "Ctrl+S", () => SaveNoteAsync(note, false)));
        menu.Items.Add(TabMenuItem(Strings.Ctx_SaveAs, "Ctrl+Shift+S", () => SaveNoteAsync(note, true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(TabMenuItem(Strings.Ctx_Delete, "Ctrl+W", () => DeleteNoteWithUndoAsync(note)));
        menu.Items.Add(TabMenuItem(Strings.Ctx_DeleteAllBelow, null,
            () => DeleteNotesWithUndoAsync(_notes.Skip(_notes.IndexOf(note) + 1)),
            _notes.Skip(_notes.IndexOf(note) + 1).Any(candidate => !candidate.Pinned)));
        menu.Open(item);
        e.Handled = true;
    }

    private static MenuItem TabMenuItem(string header, string? gesture, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (gesture is not null)
            item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += async (_, _) => await action();
        return item;
    }

    private async void OnPin(object? sender, RoutedEventArgs e)
    {
        if (_active is { } note)
            await TogglePinAsync(note);
    }

    private async void OnRowPin(object? sender, RoutedEventArgs e)
    {
        if (NoteFromSource(sender) is { } note)
            await TogglePinAsync(note);
    }

    private async void OnClone(object? sender, RoutedEventArgs e)
    {
        if (_active is { } note)
            await CloneNoteAsync(note);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_active is { } note)
            await DeleteNoteWithUndoAsync(note);
    }

    private async void OnRowClose(object? sender, RoutedEventArgs e)
    {
        if (NoteFromSource(sender) is { } note)
            await DeleteNoteWithUndoAsync(note);
    }
}
