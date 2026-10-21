using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    /// <summary>Number of TabItemBorder_Loaded calls (tab container realizations). Diagnostic only.</summary>
    private static int _tabContainersLoadedCount;

    /// <summary>Tab containers realized so far (measures container churn during startup).</summary>
    internal static int TabContainersLoadedCount => Volatile.Read(ref _tabContainersLoadedCount);

    // Hover fade for a tab item's pin/close buttons, shared (frozen) by every item. Only
    // animations are used — never local values — so once an animation is removed or stops, the
    // trigger-driven base state of TabItemTemplate (hidden / Pinned / IsSelected) is what shows,
    // and a recycled container carries no stale button state into its next item.
    private static readonly DoubleAnimation HoverFadeIn =
        Frozen(new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(100) });

    // FillBehavior.Stop: after the fade the value falls back to the base (0 for a hidden button).
    private static readonly DoubleAnimation HoverFadeOut =
        Frozen(new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(100), FillBehavior = FillBehavior.Stop });

    // Holds Visible while hovered (HoldEnd).
    private static readonly ObjectAnimationUsingKeyFrames HoverShow = Frozen(VisibleFor(TimeSpan.FromMilliseconds(100), FillBehavior.HoldEnd));

    // Keeps the button Visible for the 100 ms fade-out, then stops so Visibility falls back to the
    // base (Collapsed) — the delayed collapse, with no timer and nothing tied to the container.
    private static readonly ObjectAnimationUsingKeyFrames HoverHideDelayed = Frozen(VisibleFor(TimeSpan.FromMilliseconds(100), FillBehavior.Stop));

    private static ObjectAnimationUsingKeyFrames VisibleFor(TimeSpan duration, FillBehavior fill)
    {
        var anim = new ObjectAnimationUsingKeyFrames { Duration = duration, FillBehavior = fill };
        anim.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        return anim;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>
    /// Filtered/ordered view over ViewModel.Tabs bound to TabList.ItemsSource. The ObservableCollection
    /// is kept in display order (pinned-first) by the ViewModel, so this view only applies the search
    /// filter — no sort/group, which keeps UI virtualization straightforward on a flat item list.
    /// </summary>
    private System.Windows.Data.ListCollectionView? _tabsView;

    /// <summary>False until LoadTabsAsync has populated the collection (and again while a reload is
    /// in flight) — tab creation is deferred meanwhile, because the load's Clear/repopulate would
    /// otherwise wipe a just-created tab from the UI while its INSERT still lands in the DB.</summary>
    private bool _tabsLoaded;

    /// <summary>Set when CreateNewTab/RequestNewTab (Ctrl+T, hotkey, IPC) arrives while tabs are not
    /// loaded; LoadTabsAsync honors it once the collection is populated.</summary>
    private bool _newTabRequestedWhileLoading;

    /// <summary>True while a LoadTabsAsync pass runs; a concurrent call only sets
    /// <see cref="_reloadRequested"/> so the running pass loops once more.</summary>
    private bool _loadTabsInProgress;

    /// <summary>A LoadTabsAsync call arrived during a running load — reload once more at the end.</summary>
    private bool _reloadRequested;

    /// <summary>True after the first successful load; later loads are reloads (merge, recovery).</summary>
    private bool _initialTabLoadDone;

    /// <summary>
    /// Resolves the realized ListBoxItem container for a tab, or null if the tab is virtualized
    /// (off-screen) or not yet generated. Callers must null-guard.
    /// </summary>
    private ListBoxItem? ContainerFor(NoteTab? tab) =>
        tab is null ? null : TabList.ItemContainerGenerator.ContainerFromItem(tab) as ListBoxItem;

    /// <summary>
    /// Runs a structural mutation of the tab collection with SelectionChanged suppressed. Because
    /// TabList is now bound to the collection view, clearing/removing the selected data item would
    /// otherwise fire SelectionChanged mid-operation and blank the active tab. The caller restores
    /// selection afterward (typically via RebuildTabList → SelectTabByNote or a focus cascade).
    /// </summary>
    private void MutateTabsGuarded(Action mutate)
    {
        bool prev = _isRebuildingTabList;
        _isRebuildingTabList = true;
        try { mutate(); }
        finally { _isRebuildingTabList = prev; }
    }

    /// <summary>
    /// Creates the collection view and binds it to TabList once. Idempotent — LoadTabsAsync can run
    /// multiple times (e.g. after a merge), but the ItemsSource is established a single time.
    /// </summary>
    private void EnsureTabsViewBound()
    {
        if (_tabsView is not null) return;
        _tabsView = new System.Windows.Data.ListCollectionView(_tabs)
        {
            Filter = o => o is NoteTab t && MatchesSearch(t)
        };
        TabList.ItemsSource = _tabsView;
    }

    // ─── Tab Loading ────────────────────────────────────────────────────

    /// <summary>
    /// Loads tabs from database for this desktop and populates the tab list.
    /// Called by App.CompleteWindowInitAsync after Show(), and again as a reload after a merge or
    /// startup crash recovery. Auto-creates an empty tab if no notes exist (per CONTEXT.md).
    /// Re-entrancy safe: a call while a load is running makes the running load loop once more,
    /// and new-tab requests that arrive mid-load are queued and honored at the end.
    /// </summary>
    public async Task LoadTabsAsync()
    {
        if (_loadTabsInProgress)
        {
            _reloadRequested = true;
            return;
        }

        _loadTabsInProgress = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await LoadTabsOnceAsync();
            }
            while (_reloadRequested);
        }
        finally
        {
            _loadTabsInProgress = false;
            _tabsLoaded = true; // never leave tab creation blocked, even if a load threw
        }

        if (_newTabRequestedWhileLoading)
        {
            _newTabRequestedWhileLoading = false;
            CreateNewTab();
        }
    }

    /// <summary>
    /// One load pass. First load: query, drop empty unpinned notes in memory, populate, select the
    /// first tab, focus the editor — then delete the dropped rows in the background. Reload: flush
    /// the editor, then merge DB rows with the in-memory tabs, keeping the active tab selected.
    /// </summary>
    private async Task LoadTabsOnceAsync()
    {
        bool isReload = _initialTabLoadDone;
        _tabsLoaded = false;

        // Restore persisted tab panel width (preference-cache hit — no DB round trip)
        await RestoreTabPanelWidthAsync();

        // Bind the virtualized collection view before mutating _tabs so the ListBox tracks changes.
        EnsureTabsViewBound();

        long? keepActiveId = null;
        if (isReload)
        {
            // Queue the editor's latest content ahead of the read (DB work is FIFO), and wait for
            // in-flight optimistic INSERTs so every in-memory tab carries its real id for the merge.
            SaveCurrentTabContent();
            foreach (var tab in _tabs.ToList())
                await tab.Persisted;
            keepActiveId = _activeTab?.Id;
        }

        var notes = await NoteStore.GetNotesForDesktopAsync(_desktopGuid);

        List<NoteTab> toShow;
        List<NoteTab> emptyNotes = [];
        if (isReload)
        {
            var excluded = _pendingDeletion is { } pending
                ? pending.Tabs.Select(t => t.Id).ToHashSet()
                : [];
            toShow = ViewModels.MainWindowViewModel.MergeReloadedTabs(_tabs, notes, excluded);
        }
        else
        {
            (toShow, emptyNotes) = ViewModels.MainWindowViewModel.SplitEmptyStartupNotes(notes);
        }

        MutateTabsGuarded(() =>
        {
            _tabs.Clear();
            foreach (var note in toShow)
                _tabs.Add(note);
        });
        _tabsLoaded = true;
        _initialTabLoadDone = true;

        // Startup cleanup: the empty unpinned notes were never shown — delete them off the critical path.
        if (emptyNotes.Count > 0)
            DeleteEmptyStartupNotesInBackground(emptyNotes);

        if (_tabs.Count == 0)
        {
            // Auto-create first empty tab — no empty state screen
            CreateNewTab();
            return; // CreateNewTab already selects and focuses the tab
        }

        RebuildTabList();

        var target = keepActiveId is long id ? _tabs.FirstOrDefault(t => t.Id == id) : null;
        if (target is not null)
            SelectTabByNote(target);
        else if (TabList.Items.Count > 0)
            TabList.SelectedIndex = 0;

        // Launch lands the caret in the editor (a reload never moves focus). SelectionChanged has
        // run synchronously above and enabled the editor.
        if (!isReload)
            FocusEditorIfIdle();
    }

    /// <summary>
    /// Focuses the editor unless the user is somewhere that must keep focus: an active rename,
    /// the search box, a drag, a tab-list rebuild, or an open confirmation dialog. No-op while the
    /// editor is disabled.
    /// </summary>
    private void FocusEditorIfIdle()
    {
        if (_activeRename is not null || _isDragging || _isRebuildingTabList) return;
        if (SearchBox.IsKeyboardFocusWithin || IsConfirmationOpen) return;
        if (!ContentEditor.IsEnabled) return;
        Keyboard.Focus(ContentEditor);
    }

    /// <summary>
    /// Deletes the empty unpinned notes that startup dropped from the list, in one batched
    /// statement on the DB thread. Failures are logged only — these rows were never shown.
    /// </summary>
    private void DeleteEmptyStartupNotesInBackground(List<NoteTab> emptyNotes)
    {
        var ids = emptyNotes.Where(t => t.Id > 0).Select(t => t.Id).ToList();
        if (ids.Count == 0) return;
        string desktopGuid = _desktopGuid;
        BackgroundWriter.Run(async () =>
        {
            try
            {
                int deleted = await NoteStore.DeleteNotesAsync(ids).ConfigureAwait(false);
                if (deleted > 0)
                    LogService.Info("Startup cleanup: deleted {DeletedCount} empty note(s) for desktop {DesktopGuid}", deleted, desktopGuid);
            }
            catch (Exception ex)
            {
                LogService.Warn("Startup cleanup of empty notes failed for desktop {DesktopGuid}", desktopGuid, ex);
            }
        }, "startup empty-note cleanup");
    }

    // ─── Tab List Building ──────────────────────────────────────────────

    /// <summary>
    /// Brings the tab list's derived state up to date after a mutation of the tab collection:
    /// recomputes the pinned/unpinned zone-divider flags (only the changed ones raise
    /// PropertyChanged) and re-selects the active tab. The bound ListCollectionView already
    /// tracks Add/Insert/Remove/Move incrementally and applies the search filter to added items,
    /// so a <see cref="System.Windows.Data.ListCollectionView.Refresh"/> — a full Reset that
    /// re-generates every realized container — runs only when <paramref name="refreshFilter"/> is
    /// set: when the search text changed or a pin re-sort repopulated the collection.
    /// </summary>
    /// <param name="refreshFilter">Re-evaluate the search filter over every tab.</param>
    private void RebuildTabList(bool refreshFilter = false)
    {
        ViewModel.UpdateZoneSeparators();

        if (refreshFilter)
        {
            // Guard selection churn during the refresh.
            _isRebuildingTabList = true;
            _tabsView?.Refresh();
            _isRebuildingTabList = false;
        }

        // Re-select active tab if still visible after filtering
        if (_activeTab is not null)
            SelectTabByNote(_activeTab);

        // Drag-reorder drop: fade the moved tab in. Done here rather than in a container Loaded
        // handler because recycled containers don't re-fire Loaded.
        if (_fadeInTab is { } fadeTab)
        {
            _fadeInTab = null;
            Dispatcher.BeginInvoke(() => FadeInTabItem(fadeTab), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Fades a tab item from half to full opacity (the drop feedback after a drag reorder).
    /// No-op when the item is virtualized off-screen.
    /// </summary>
    private void FadeInTabItem(NoteTab tab)
    {
        if (ContainerFor(tab) is not ListBoxItem item) return;
        if (FindNamedDescendant<Border>(item, "OuterBorder") is not Border target) return;

        var fadeIn = new DoubleAnimation
        {
            From = 0.5, To = 1.0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeIn.Completed += (_, _) =>
        {
            target.Opacity = 1.0;
            target.BeginAnimation(UIElement.OpacityProperty, null);
        };
        target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Middle-click on a tab container deletes it (soft-delete with undo toast).
    /// </summary>
    private void TabItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is ListBoxItem { Tag: NoteTab tab })
        {
            _ = DeleteTabAsync(tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Right-click on a tab container opens the themed context menu.
    /// </summary>
    private void TabItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.Tag is NoteTab tab)
        {
            var contextMenu = BuildTabContextMenu(tab, item);
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Loaded handler for the DataTemplate's OuterBorder — a diagnostic counter of container
    /// realizations only. It must do no per-item wiring: with container recycling, a reused
    /// container never re-fires Loaded, so anything set up here would stick to the wrong tab.
    /// </summary>
    private void TabItemBorder_Loaded(object sender, RoutedEventArgs e) =>
        Interlocked.Increment(ref _tabContainersLoadedCount);

    /// <summary>
    /// Resolves the pin/close buttons of the tab item that <paramref name="templateElement"/>
    /// (an element of TabItemTemplate) belongs to — a template-namescope lookup, no tree walk.
    /// </summary>
    private static (Border? Pin, Border? Close) TabButtonsOf(FrameworkElement templateElement)
    {
        // Elements generated from a DataTemplate have the ContentPresenter as TemplatedParent;
        // FindName must be given the same template the presenter applied (its ContentTemplate).
        if (templateElement.TemplatedParent is not ContentPresenter { ContentTemplate: { } template } presenter)
        {
            return (null, null);
        }
        return (template.FindName("PinBtn", presenter) as Border,
                template.FindName("CloseBtn", presenter) as Border);
    }

    /// <summary>Hover enter on a tab item: fade its pin/close buttons in (no-op visually for a
    /// button the selected/pinned triggers already show).</summary>
    private void TabItemBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        var (pin, close) = TabButtonsOf((FrameworkElement)sender);
        if (pin is not null) ShowTabButton(pin);
        if (close is not null) ShowTabButton(close);
    }

    /// <summary>Hover leave on a tab item: fade out and then collapse the buttons the base state
    /// hides; drop the hover animation on buttons the base state shows (selected tab, a pinned
    /// tab's pin), so they stay put without a flicker. Runs during a drag too, so no hover
    /// animation is left holding a button visible after the drop.</summary>
    private void TabItemBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        var border = (FrameworkElement)sender;
        var tab = border.DataContext as NoteTab;
        bool selected = tab is not null && ReferenceEquals(tab, TabList.SelectedItem);
        var (pin, close) = TabButtonsOf(border);
        if (pin is not null) HideTabButton(pin, baseVisible: selected || tab?.Pinned == true);
        if (close is not null) HideTabButton(close, baseVisible: selected);
    }

    private static void ShowTabButton(UIElement button)
    {
        button.BeginAnimation(VisibilityProperty, HoverShow);
        button.BeginAnimation(OpacityProperty, HoverFadeIn);
    }

    private static void HideTabButton(UIElement button, bool baseVisible)
    {
        if (baseVisible)
        {
            button.BeginAnimation(OpacityProperty, null);
            button.BeginAnimation(VisibilityProperty, null);
            return;
        }
        button.BeginAnimation(VisibilityProperty, HoverHideDelayed);
        button.BeginAnimation(OpacityProperty, HoverFadeOut);
    }

    /// <summary>Pin button click: toggles the pin of the tab the button currently shows.</summary>
    private void TabPinBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteTab tab })
            TogglePin(tab);
        e.Handled = true;
    }

    /// <summary>Close button click: soft-deletes the tab the button currently shows.</summary>
    private void TabCloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteTab tab })
            _ = DeleteTabAsync(tab);
        e.Handled = true;
    }

    // ─── Tab Selection ──────────────────────────────────────────────────

    /// <summary>
    /// Handles tab selection changes: saves current content, loads new tab content.
    /// The selected item's highlight and pin/close buttons are template triggers on IsSelected.
    /// </summary>
    private async void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_isDragging && !_isCompletingDrag) return;
            if (_isRebuildingTabList) return;

            // Save current editor content to active tab BEFORE flushing or switching. The editor
            // text is materialized once here and shared with the pending undo flush; the pending
            // throttled model sync is superseded by the direct assignment.
            if (_activeTab is not null)
            {
                string outgoingText = ContentEditor.Text;
                CancelContentSync();
                FlushPendingUndoSnapshot(outgoingText); // capture the outgoing tab's coalesced typing before switching
                bool contentChanged = _activeTab.Content != outgoingText;
                _activeTab.Content = outgoingText;
                _activeTab.CursorPosition = ContentEditor.CaretIndex;
                if (contentChanged)
                {
                    _activeTab.UpdatedAt = DateTime.Now;
                }
            }

            // Flush autosave service in the background — stops timer, captures content
            // synchronously, and persists it via BackgroundWriter without blocking the switch.
            // No-op when a SaveCurrentTabContent at this boundary already flushed (not dirty).
            _autosaveService.FlushInBackground();
            _checkpointTimer.Stop();

            // Save scroll offset for outgoing tab
            if (_activeTab is not null)
            {
                var scrollViewer = GetContentScrollViewer();
                if (scrollViewer is not null)
                {
                    _activeTab.EditorScrollOffset = (int)scrollViewer.VerticalOffset;
                }
            }

            // Apply new selection
            if (TabList.SelectedItem is NoteTab tab)
            {
                _activeTab = tab;

                // Suppress TextChanged during programmatic text assignment
                string content = tab.Content;
                _suppressTextChanged = true;
                ContentEditor.Text = content;
                _suppressTextChanged = false;

                ContentEditor.IsEnabled = true;

                // Restore cursor position (best effort — clamp to content length). Uses the string
                // just assigned: reading ContentEditor.Text back would materialize a new copy.
                ContentEditor.CaretIndex = Math.Min(tab.CursorPosition, content.Length);

                // Restore scroll offset after layout completes
                _ = ContentEditor.Dispatcher.BeginInvoke(() =>
                {
                    GetContentScrollViewer()?.ScrollToVerticalOffset(tab.EditorScrollOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Bind this tab's UndoStack; seed it with the loaded content as the Ctrl+Z
                // baseline if it has no history yet (through the manager, so its byte
                // accounting stays in sync).
                UndoManager.Instance.SetActiveTab(tab.Id);
                UndoManager.Instance.EnsureInitialContent(tab.Id, content);
            }
            else
            {
                _activeTab = null;
                _suppressTextChanged = true;
                ContentEditor.Text = "";
                _suppressTextChanged = false;
                ContentEditor.IsEnabled = false;
                UndoManager.Instance.SetActiveTab(null);
            }

            // Update toolbar enabled states after tab selection change
            UpdateToolbarState();

            // Re-search in new tab content if find panel is open
            RefreshFindIfPanelOpen();
        }
        catch (Exception ex)
        {
            LogService.Warn("Tab selection change failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Selects the given tab (the NoteTab data item) in the bound list and scrolls it into view.
    /// </summary>
    private void SelectTabByNote(NoteTab tab)
    {
        // If a guarded/suppressed mutation left SelectedItem already pointing at this tab while
        // the editor still shows another one, assigning the same value wouldn't raise
        // SelectionChanged — force a real change so the switch logic runs.
        if (ReferenceEquals(TabList.SelectedItem, tab) && !ReferenceEquals(_activeTab, tab))
            TabList.SelectedItem = null;

        TabList.SelectedItem = tab;
        SafeScrollIntoView(tab);
    }

    /// <summary>
    /// Scrolls an item into view, tolerating the NullReferenceException a VirtualizingStackPanel can
    /// throw during layout before it's been measured/arranged (retried once after the layout pass).
    /// </summary>
    private void SafeScrollIntoView(object? item)
    {
        if (item is null) return;
        try
        {
            TabList.ScrollIntoView(item);
        }
        catch (NullReferenceException)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try { TabList.ScrollIntoView(item); }
                catch (NullReferenceException) { /* Layout not ready; safe to ignore */ }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // ─── Smooth Wheel Scrolling ─────────────────────────────────────────

    private ScrollViewer? _tabListScrollViewer;
    private System.Windows.Threading.DispatcherTimer? _tabScrollTimer;
    private double _tabScrollTarget;

    /// <summary>Pixels scrolled per mouse-wheel notch (~1.2 tab rows; default item scrolling jumps 3).</summary>
    private const double TabScrollStepPx = 56;

    /// <summary>
    /// Replaces the default instant wheel scrolling with a short eased animation toward an
    /// accumulated target offset — smaller steps, smooth motion. Requires ScrollUnit=Pixel so
    /// VerticalOffset is in pixels. Successive notches accumulate into the same glide.
    /// </summary>
    private void TabList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The shared rename box lives inside one container's template; with container recycling a
        // scrolled-away container is reused for another tab, which would carry the box (and the
        // hidden title) along. Commit the rename before the list scrolls.
        if (_activeRename is not null) CommitRename();

        _tabListScrollViewer ??= FindDescendant<ScrollViewer>(TabList);
        var sv = _tabListScrollViewer;
        if (sv is null || sv.ScrollableHeight <= 0) return; // nothing to scroll — leave default behavior

        e.Handled = true;

        // Start a new glide from the current position; while gliding, accumulate onto the target.
        if (_tabScrollTimer is null || !_tabScrollTimer.IsEnabled)
            _tabScrollTarget = sv.VerticalOffset;
        _tabScrollTarget = Math.Clamp(
            _tabScrollTarget - e.Delta / 120.0 * TabScrollStepPx, 0, sv.ScrollableHeight);

        if (_tabScrollTimer is null)
        {
            _tabScrollTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 fps
            };
            _tabScrollTimer.Tick += (_, _) =>
            {
                var scroller = _tabListScrollViewer;
                if (scroller is null) { _tabScrollTimer!.Stop(); return; }

                double current = scroller.VerticalOffset;
                double remaining = _tabScrollTarget - current;
                if (Math.Abs(remaining) < 0.5)
                {
                    scroller.ScrollToVerticalOffset(_tabScrollTarget);
                    _tabScrollTimer!.Stop();
                    return;
                }
                // Exponential ease-out: cover 30% of the remaining distance per frame.
                scroller.ScrollToVerticalOffset(current + remaining * 0.3);
            };
        }
        _tabScrollTimer.Start();
    }

    // ─── Tab Panel Resize ───────────────────────────────────────────────

    /// <summary>
    /// Restores persisted tab panel width from preferences on startup.
    /// </summary>
    private async Task RestoreTabPanelWidthAsync()
    {
        var saved = await PreferenceStore.GetPreferenceAsync("tab_panel_width");
        if (saved is not null && double.TryParse(saved, System.Globalization.CultureInfo.InvariantCulture, out double width))
        {
            width = Math.Clamp(width, 120, 400);
            TabPanelColumn.Width = new GridLength(width);
        }
    }

    /// <summary>
    /// Saves the tab panel width to preferences after the user finishes dragging the splitter.
    /// </summary>
    private async void TabPanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        try
        {
            var width = TabPanelColumn.ActualWidth;
            await PreferenceStore.SetPreferenceAsync("tab_panel_width",
                width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            LogService.Warn("Failed to save tab panel width: {ErrorMessage}", ex.Message);
        }
    }

    // ─── Content Save ───────────────────────────────────────────────────

    /// <summary>
    /// Saves current content from the editor to the active tab model (content, caret, scroll),
    /// commits any coalesced typing as an undo step, and — when the autosave layer has unsaved
    /// changes — persists it with ONE tracked background write (<see cref="AutosaveService.FlushInBackground"/>,
    /// which captures the content synchronously and marks the service clean). The SelectionChanged
    /// that typically follows (new tab, clone, delete) then finds nothing dirty and writes nothing.
    /// </summary>
    private void SaveCurrentTabContent()
    {
        if (_activeTab is null) return;
        string currentContent = ContentEditor.Text;
        CancelContentSync(); // superseded by SaveEditorStateToTab below
        FlushPendingUndoSnapshot(currentContent); // commit any coalesced typing as an undo step at this boundary

        var scrollViewer = GetContentScrollViewer();
        int scrollOffset = scrollViewer is not null ? (int)scrollViewer.VerticalOffset : 0;

        // Always keep in-memory editor state (caret/scroll) current; it's cheap and needed for restore.
        ViewModel.SaveEditorStateToTab(currentContent, ContentEditor.CaretIndex, scrollOffset);

        // IsDirty is the authoritative "unsaved to DB" signal (an editor==model compare is useless
        // once the model is synced above). FlushInBackground runs the write's synchronous prefix
        // now, so on the reload path it is queued on the DB thread ahead of the following read.
        // An unpersisted (temp-id) tab is skipped and stays dirty; its INSERT path writes the content.
        if (_autosaveService.IsDirty)
            _autosaveService.FlushInBackground();
    }

    // ─── Tab Creation ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a new empty tab optimistically: it appears, is selected, and receives focus
    /// immediately under a temporary negative id; the INSERT runs in the background and the
    /// real id is swapped in (undo stack migrated) when it lands.
    /// </summary>
    public void CreateNewTab()
    {
        if (!_tabsLoaded)
        {
            // Window shell is up but tabs aren't loaded (DB still opening, or a reload is mid-flight):
            // remember the request; LoadTabsAsync creates the tab once the collection is populated.
            _newTabRequestedWhileLoading = true;
            return;
        }

        // An active search filter would hide the new (empty) tab — it can't match the query, so the
        // view filters it out and "nothing happens". Clear the search first, then create.
        if (!string.IsNullOrEmpty(_searchText))
            ClearSearch();

        SaveCurrentTabContent();

        var (existingPlaceholder, insertIndex, sortOrder) = ViewModel.GetNewTabPosition();

        if (existingPlaceholder is not null)
        {
            SelectTabByNote(existingPlaceholder);
            Keyboard.Focus(ContentEditor);
            return;
        }

        var newTab = new NoteTab
        {
            Id = NoteTab.NextTempId(),
            DesktopGuid = _desktopGuid,
            Content = "",
            Pinned = false,
            SortOrder = sortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        newTab.BeginPersistence();

        // The bound view picks the Insert up incrementally — no Refresh (it would re-generate every
        // realized container) and no re-select of the outgoing tab; only the zone-divider flags
        // that actually change are updated.
        ViewModel.InsertNewTab(newTab, insertIndex);
        ViewModel.UpdateZoneSeparators();
        SelectTabByNote(newTab);
        Keyboard.Focus(ContentEditor);

        BackgroundWriter.Run(async () =>
        {
            long tempId = newTab.Id;
            try
            {
                long realId = await NoteStore.InsertNoteAsync(
                    _desktopGuid, null, "", false, sortOrder).ConfigureAwait(false);

                // Assign the real id and re-key the undo stack on the background thread. Id is a plain
                // long (atomic write, no PropertyChanged, no binding keyed on it — selection is
                // reference-based) and MigrateStack is internally locked, so neither needs the UI
                // dispatcher — and MUST NOT hop to it: Persisted completes in the finally below, and
                // the sync-blocked close path (CommitPendingDeletionAsync via GetAwaiter().GetResult())
                // never pumps the dispatcher, so a hop here would deadlock the UI thread on close.
                newTab.Id = realId;
                UndoManager.Instance.MigrateStack(tempId, realId);

                // Catch-all: anything typed before the row existed lives only in the model. Reading
                // Content off-thread is a benign race (atomic reference read); autosave re-persists on
                // the next change once the real id exists.
                string content = newTab.Content;
                if (content.Length > 0)
                {
                    await NoteStore.UpdateNoteContentAsync(realId, content).ConfigureAwait(false);
                }
            }
            finally
            {
                // Persisted MUST complete in every outcome — including a thrown INSERT. It gates
                // CommitPendingDeletionAsync (which awaits tab.Persisted before DeleteNoteAsync),
                // and that method is sync-blocked via GetAwaiter().GetResult() on the window-close/
                // shutdown path. If Persisted never completed, that await would hang the UI thread
                // forever; off the close path, one failed-insert tab at the head of a bulk deletion
                // would block deletion of all its siblings. On failure the tab keeps its negative
                // id, but every persist-by-id path is a no-op for negative ids (sort-order writes
                // filter Id > 0; Update/Delete-by-id match zero rows; autosave skips tabId <= 0),
                // so unblocking awaiters here writes no bogus data. We do NOT catch the exception:
                // finally runs before it leaves the lambda, so Persisted completes AND BackgroundWriter
                // still logs it and fires the "Saving failed — kept in memory" toast.
                newTab.CompletePersistence();
            }
        }, "create tab");
    }

    /// <summary>
    /// Creates a new empty tab and focuses it.
    /// Called by App via IPC routing for new-tab commands.
    /// </summary>
    public void RequestNewTab()
    {
        LogService.Debug("RequestNewTab called for desktop {DesktopGuid}", _desktopGuid);
        CreateNewTab();
    }

    // ─── New Tab Button ─────────────────────────────────────────────────

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTab();
    }

    // ─── Pin/Unpin Toggle ───────────────────────────────────────────────

    /// <summary>
    /// Toggles the pinned state of a tab, re-sorts the collection, and persists in the
    /// background (UI first — a pin toggle must never wait on the database).
    /// </summary>
    private void TogglePin(NoteTab tab)
    {
        tab.Pinned = !tab.Pinned;

        // ReorderAfterPinToggle clears and repopulates the bound collection; guard so the transient
        // empty state doesn't fire SelectionChanged and blank the active tab. RebuildTabList reselects.
        MutateTabsGuarded(() => ViewModel.ReorderAfterPinToggle(tab));
        RebuildTabList(refreshFilter: true);
        UpdateToolbarState();

        var orders = _tabs.Where(t => t.Id > 0).Select(t => (t.Id, t.SortOrder)).ToList();
        bool pinned = tab.Pinned;
        BackgroundWriter.Run(async () =>
        {
            // Persisted is safe to await here even for a not-yet-persisted (temp-id) tab: Task 11's
            // fix guarantees it always completes, even on a failed INSERT. Pin never creates a tab
            // itself, so there is no BeginPersistence/CompletePersistence pairing to manage here.
            await tab.Persisted.ConfigureAwait(false);
            await NoteStore.UpdateNotePinnedAsync(tab.Id, pinned).ConfigureAwait(false);
            await NoteStore.UpdateNoteSortOrdersAsync(orders).ConfigureAwait(false);
        }, "pin toggle");
    }

    // ─── Clone Tab ──────────────────────────────────────────────────────

    /// <summary>
    /// Clones the current tab optimistically: the clone appears and is focused immediately;
    /// the INSERT and sort-order persist run in the background.
    /// </summary>
    private void CloneTab(NoteTab source)
    {
        SaveCurrentTabContent();

        var (insertIndex, sortOrder) = ViewModel.GetClonePosition(source);

        var clone = new NoteTab
        {
            Id = NoteTab.NextTempId(),
            DesktopGuid = _desktopGuid,
            Name = source.Name,
            Content = source.Content,
            Pinned = source.Pinned,
            SortOrder = sortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        clone.BeginPersistence();

        // Incremental insert, as in CreateNewTab: no view Refresh, no stale re-select.
        ViewModel.InsertNewTab(clone, insertIndex);
        ViewModel.UpdateZoneSeparators();
        SelectTabByNote(clone);
        Keyboard.Focus(ContentEditor);

        var orders = _tabs.Where(t => t.Id > 0).Select(t => (t.Id, t.SortOrder)).ToList();
        BackgroundWriter.Run(async () =>
        {
            long tempId = clone.Id;
            try
            {
                long realId = await NoteStore.InsertNoteAsync(
                    _desktopGuid, clone.Name, clone.Content, clone.Pinned, sortOrder).ConfigureAwait(false);

                // Assign the real id and re-key the undo stack on the background thread. Id is a plain
                // long (atomic write, no PropertyChanged, no binding keyed on it — selection is
                // reference-based) and MigrateStack is internally locked, so neither needs the UI
                // dispatcher — and MUST NOT hop to it: Persisted completes in the finally below, and
                // the sync-blocked close path (CommitPendingDeletionAsync via GetAwaiter().GetResult())
                // never pumps the dispatcher, so a hop here would deadlock the UI thread on close.
                clone.Id = realId;
                UndoManager.Instance.MigrateStack(tempId, realId);

                await NoteStore.UpdateNoteSortOrdersAsync(orders).ConfigureAwait(false);
            }
            finally
            {
                // Persisted MUST complete in every outcome — including a thrown INSERT. It gates
                // CommitPendingDeletionAsync (which awaits tab.Persisted before DeleteNoteAsync),
                // and that method is sync-blocked via GetAwaiter().GetResult() on the window-close/
                // shutdown path. If Persisted never completed, that await would hang the UI thread
                // forever. We do NOT catch the exception: finally runs before it leaves the lambda,
                // so Persisted completes AND BackgroundWriter still logs it and fires the
                // "Saving failed — kept in memory" toast.
                clone.CompletePersistence();
            }
        }, "clone tab");
    }
}
