using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;
using JoJot.Themes;

namespace JoJot;

public partial class MainWindow
{
    private static readonly SolidColorBrush DangerBrush =
        new(System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));

    /// <summary>Number of TabItemBorder_Loaded calls (tab container realizations). Diagnostic only.</summary>
    private static int _tabContainersLoadedCount;

    /// <summary>Tab containers realized so far (measures container churn during startup).</summary>
    internal static int TabContainersLoadedCount => Volatile.Read(ref _tabContainersLoadedCount);

    static MainWindow()
    {
        DangerBrush.Freeze(); // frozen brushes are cheaper to render and thread-safe
    }

    /// <summary>
    /// Filtered/ordered view over ViewModel.Tabs bound to TabList.ItemsSource. The ObservableCollection
    /// is kept in display order (pinned-first) by the ViewModel, so this view only applies the search
    /// filter — no sort/group, which keeps UI virtualization straightforward on a flat item list.
    /// </summary>
    private System.Windows.Data.ListCollectionView? _tabsView;

    /// <summary>False until the first LoadTabsAsync — blocks tab creation while the window
    /// shell is visible but the database is still opening.</summary>
    private bool _tabsLoaded;

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
    /// Called by App.CreateWindowForDesktop after Show().
    /// Auto-creates an empty tab if no notes exist (per CONTEXT.md).
    /// </summary>
    public async Task LoadTabsAsync()
    {
        // Restore persisted tab panel width
        await RestoreTabPanelWidthAsync();

        // Bind the virtualized collection view before mutating _tabs so the ListBox tracks changes.
        EnsureTabsViewBound();
        _tabsLoaded = true;

        // Silently delete empty unpinned notes before loading
        await NoteStore.DeleteEmptyNotesAsync(_desktopGuid);

        var notes = await NoteStore.GetNotesForDesktopAsync(_desktopGuid);
        MutateTabsGuarded(() =>
        {
            _tabs.Clear();
            foreach (var note in notes)
                _tabs.Add(note);
        });

        if (_tabs.Count == 0)
        {
            // Auto-create first empty tab — no empty state screen
            CreateNewTab();
            return; // CreateNewTab already selects the tab
        }

        RebuildTabList();

        // Select first tab
        if (TabList.Items.Count > 0)
            TabList.SelectedIndex = 0;
    }

    // ─── Tab List Building ──────────────────────────────────────────────

    /// <summary>
    /// Refreshes the tab list: recomputes the pinned/unpinned zone-divider flags and re-applies the
    /// search filter on the bound collection view. No longer tears down/recreates containers — the
    /// data-bound, virtualized ListBox updates itself; this only drives derived visual state.
    /// </summary>
    private void RebuildTabList()
    {
        // Zone divider: the first VISIBLE unpinned tab shows a top divider when any visible pinned
        // tabs exist. Computed over the filtered set so an active search doesn't leave the divider
        // on a hidden tab (or missing between visible pinned and visible unpinned results).
        bool hasVisiblePinned = false;
        foreach (var tab in _tabs)
            if (tab.Pinned && MatchesSearch(tab)) { hasVisiblePinned = true; break; }

        bool firstUnpinnedSeen = false;
        foreach (var tab in _tabs)
        {
            bool show = hasVisiblePinned && !tab.Pinned && !firstUnpinnedSeen && MatchesSearch(tab);
            if (show) firstUnpinnedSeen = true;
            tab.ShowUnpinnedSeparator = show;
        }

        // Re-apply the search filter. Guard selection churn during the refresh.
        _isRebuildingTabList = true;
        _tabsView?.Refresh();
        _isRebuildingTabList = false;

        // Re-select active tab if still visible after filtering
        if (_activeTab is not null)
            SelectTabByNote(_activeTab);
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
    /// Loaded handler for the DataTemplate root Border.
    /// Wires hover show/hide animations and pin/close click handlers.
    /// </summary>
    private void TabItemBorder_Loaded(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _tabContainersLoadedCount);
        var outerBorder = (Border)sender;
        if (outerBorder.DataContext is not NoteTab tab) return;

        // Fade-in animation after drag-reorder drop
        if (_fadeInTab is not null && tab.Id == _fadeInTab.Id)
        {
            _fadeInTab = null;
            outerBorder.Opacity = 0.5;
            var target = outerBorder;
            Dispatcher.BeginInvoke(() =>
            {
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
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        var pinBtn = FindNamedDescendant<Border>(outerBorder, "PinBtn");
        var pinIcon = FindNamedDescendant<TextBlock>(outerBorder, "PinIcon");
        var closeBtn = FindNamedDescendant<Border>(outerBorder, "CloseBtn");
        var closeIcon = FindNamedDescendant<TextBlock>(outerBorder, "CloseIcon");
        if (pinBtn is null || pinIcon is null || closeBtn is null || closeIcon is null) return;

        // Find parent ListBoxItem for selection check
        var item = FindAncestor<ListBoxItem>(outerBorder);

        // Pin button click
        pinBtn.MouseLeftButtonDown += (s, ev) =>
        {
            TogglePin(tab);
            ev.Handled = true;
        };

        // Pin button hover — behavior differs for pinned vs unpinned
        if (tab.Pinned)
        {
            pinBtn.MouseEnter += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.Text = "\uE77A"; // Unpin glyph
                pinIcon.Foreground = DangerBrush;
            };
            pinBtn.MouseLeave += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.Text = "\uE718"; // Pin icon
                pinIcon.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
            };
        }
        else
        {
            pinBtn.MouseEnter += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
            };
            pinBtn.MouseLeave += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
            };
        }

        // Close button click + hover
        closeBtn.MouseLeftButtonDown += (s, ev) =>
        {
            _ = DeleteTabAsync(tab);
            ev.Handled = true;
        };
        closeBtn.MouseEnter += (s, ev) =>
        {
            if (_isDragging) return;
            closeIcon.Foreground = DangerBrush;
        };
        closeBtn.MouseLeave += (s, ev) =>
        {
            if (_isDragging) return;
            closeIcon.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        };

        // Outer border hover — show/hide pin and close buttons.
        // Hover/selection BACKGROUND is handled declaratively by DataTemplate triggers; never set
        // OuterBorder.Background here — a local value would permanently override those triggers.
        outerBorder.MouseEnter += (s, ev) =>
        {
            if (_isDragging) return;

            if (!tab.Pinned)
            {
                pinBtn.Visibility = Visibility.Visible;
                AnimateOpacity(pinBtn, 0, 1, 100);
            }

            closeBtn.Visibility = Visibility.Visible;
            AnimateOpacity(closeBtn, 0, 1, 100);
        };
        outerBorder.MouseLeave += (s, ev) =>
        {
            if (_isDragging) return;

            if (!tab.Pinned)
            {
                AnimateOpacity(pinBtn, 1, 0, 100);
                DelayedCollapse(pinBtn);
            }

            AnimateOpacity(closeBtn, 1, 0, 100);
            DelayedCollapse(closeBtn);
        };

        // If this container is (or is recycled into) the selected tab, re-apply the selection
        // highlight — under virtualization a container can be realized after SelectionChanged ran.
        if (item is not null && ReferenceEquals(tab, TabList.SelectedItem))
            ApplyActiveHighlight(item);
    }

    /// <summary>
    /// Walks the visual tree upward to find the first ancestor of type T.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T result) return result;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    // ─── Tab Selection ──────────────────────────────────────────────────

    /// <summary>
    /// Handles tab selection changes: saves current content, loads new tab content.
    /// Applies background highlight to active tab.
    /// </summary>
    private async void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_isDragging && !_isCompletingDrag) return;
            if (_isRebuildingTabList) return;

            // Save current editor content to active tab BEFORE flushing or switching
            if (_activeTab is not null)
            {
                FlushContentSync();
                FlushPendingUndoSnapshot(); // capture the outgoing tab's coalesced typing before switching
                bool contentChanged = _activeTab.Content != ContentEditor.Text;
                _activeTab.Content = ContentEditor.Text;
                _activeTab.CursorPosition = ContentEditor.CaretIndex;
                if (contentChanged)
                {
                    _activeTab.UpdatedAt = DateTime.Now;
                }
            }

            // Hide pin/close buttons on deselected items (background reverts via template triggers)
            foreach (var removed in e.RemovedItems)
            {
                if (removed is not NoteTab oldTab) continue;
                var oldItem = ContainerFor(oldTab);
                if (oldItem is null) continue; // virtualized off-screen — nothing to clean up

                var oldPinBtn = FindNamedDescendant<Border>(oldItem, "PinBtn");
                var oldCloseBtn = FindNamedDescendant<Border>(oldItem, "CloseBtn");

                // Don't hide a pinned tab's always-visible pin icon
                if (oldPinBtn is not null && !oldTab.Pinned)
                {
                    oldPinBtn.Opacity = 0;
                    oldPinBtn.Visibility = Visibility.Collapsed;
                }

                if (oldCloseBtn is not null)
                {
                    oldCloseBtn.Opacity = 0;
                    oldCloseBtn.Visibility = Visibility.Collapsed;
                }
            }

            // Flush autosave service in the background — stops timer, captures content
            // synchronously, and persists it via BackgroundWriter without blocking the switch
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
                var newItem = ContainerFor(tab);
                if (newItem is not null)
                {
                    ApplyActiveHighlight(newItem);
                }
                else
                {
                    // Container not realized yet — apply once it is. (TabItemBorder_Loaded also
                    // reapplies, covering recycled containers scrolled back into view.)
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        var c = ContainerFor(tab);
                        if (c is not null) ApplyActiveHighlight(c);
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }

                _activeTab = tab;

                // Suppress TextChanged during programmatic text assignment
                _suppressTextChanged = true;
                ContentEditor.Text = tab.Content;
                _suppressTextChanged = false;

                ContentEditor.IsEnabled = true;

                // Restore cursor position (best effort — clamp to content length)
                ContentEditor.CaretIndex = Math.Min(tab.CursorPosition, ContentEditor.Text.Length);

                // Restore scroll offset after layout completes
                _ = ContentEditor.Dispatcher.BeginInvoke(() =>
                {
                    GetContentScrollViewer()?.ScrollToVerticalOffset(tab.EditorScrollOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Bind this tab's UndoStack
                UndoManager.Instance.SetActiveTab(tab.Id);
                var stack = UndoManager.Instance.GetOrCreateStack(tab.Id);
                // Push initial content as first snapshot if stack is empty
                if (!UndoManager.Instance.CanUndo(tab.Id) && !UndoManager.Instance.CanRedo(tab.Id))
                {
                    stack.PushInitialContent(tab.Content);
                }
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
    /// Reveals the pin/close buttons on the selected tab item. The selection BACKGROUND is applied
    /// declaratively by the DataTemplate IsSelected trigger — do not set it here (a local value
    /// would override the trigger forever). If the DataTemplate visual tree is not yet applied
    /// (e.g. on window open or after RebuildTabList), defers a single retry to DispatcherPriority.Loaded.
    /// </summary>
    private void ApplyActiveHighlight(ListBoxItem item, bool isDeferred = false)
    {
        // Guard against stale deferred calls — if selection changed since the original call, this
        // container's tab is no longer selected. (SelectedItem is a NoteTab; compare via the item's Tag.)
        if (item.Tag is not NoteTab data || !ReferenceEquals(data, TabList.SelectedItem)) return;

        var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
        if (outerBorder is null)
        {
            if (!isDeferred)
            {
                // DataTemplate not yet applied — defer until layout pass completes
                Dispatcher.BeginInvoke(() => ApplyActiveHighlight(item, isDeferred: true),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return;
        }

        var pinBtn = FindNamedDescendant<Border>(item, "PinBtn");
        var closeBtn = FindNamedDescendant<Border>(item, "CloseBtn");

        if (pinBtn is not null)
        {
            pinBtn.Visibility = Visibility.Visible;
            pinBtn.Opacity = 1;
        }
        if (closeBtn is not null)
        {
            closeBtn.Visibility = Visibility.Visible;
            closeBtn.Opacity = 1;
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
    /// Saves current content from the editor to the active tab model and database.
    /// Also saves scroll offset and pushes undo snapshot.
    /// </summary>
    private void SaveCurrentTabContent()
    {
        if (_activeTab is null) return;
        FlushContentSync();
        FlushPendingUndoSnapshot(); // commit any coalesced typing as an undo step at this boundary
        string currentContent = ContentEditor.Text;

        var scrollViewer = GetContentScrollViewer();
        int scrollOffset = scrollViewer is not null ? (int)scrollViewer.VerticalOffset : 0;

        // Always keep in-memory editor state (caret/scroll) current; it's cheap and needed for restore.
        ViewModel.SaveEditorStateToTab(currentContent, ContentEditor.CaretIndex, scrollOffset);

        // Persist when the autosave layer has unsaved changes. The old editor==model compare was
        // unreliable (ThrottleContentSync keeps the model synced to the editor), so it skipped
        // needed writes; IsDirty is the authoritative "unsaved to DB" signal.
        if (!_autosaveService.IsDirty && currentContent == _activeTab.Content) return;

        _ = NoteStore.UpdateNoteContentAsync(_activeTab.Id, currentContent);
        UndoManager.Instance.PushSnapshot(_activeTab.Id, currentContent);
    }

    // ─── Tab Creation ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a new empty tab optimistically: it appears, is selected, and receives focus
    /// immediately under a temporary negative id; the INSERT runs in the background and the
    /// real id is swapped in (undo stack migrated) when it lands.
    /// </summary>
    public void CreateNewTab()
    {
        if (!_tabsLoaded) return; // window shell is up but the DB isn't — ignore early Ctrl+T

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

        ViewModel.InsertNewTab(newTab, insertIndex);
        RebuildTabList();
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
        LogService.Info("RequestNewTab called for desktop {DesktopGuid}", _desktopGuid);
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
        RebuildTabList();
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

        ViewModel.InsertNewTab(clone, insertIndex);
        RebuildTabList();
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
