using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Tab Loading ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads tabs from database for this desktop and populates the tab list.
    /// Called by App.CreateWindowForDesktop after Show().
    /// Auto-creates an empty tab if no notes exist (per CONTEXT.md).
    /// </summary>
    public async Task LoadTabsAsync()
    {
        // Restore persisted tab panel width
        await RestoreTabPanelWidthAsync();

        // Silently delete empty unpinned notes before loading
        await NoteStore.DeleteEmptyNotesAsync(_desktopGuid);

        var notes = await NoteStore.GetNotesForDesktopAsync(_desktopGuid);
        _tabs.Clear();
        foreach (var note in notes)
            _tabs.Add(note);

        if (_tabs.Count == 0)
        {
            // Auto-create first empty tab — no empty state screen
            await CreateNewTabAsync();
            return; // CreateNewTabAsync already selects the tab
        }

        RebuildTabList();

        // Select first tab
        if (TabList.Items.Count > 0)
            TabList.SelectedIndex = 0;
    }

    // ─── Tab List Building ──────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the entire tab list from the _tabs collection, applying search filter.
    /// Includes a zone separator between pinned and unpinned tabs.
    /// </summary>
    private void RebuildTabList()
    {
        TabList.SelectionChanged -= TabList_SelectionChanged;
        _isRebuildingTabList = true;
        TabList.Items.Clear();

        bool hasPinned = false;

        // First pass: identify if there are pinned tabs (for zone separator)
        foreach (var tab in _tabs)
        {
            if (!MatchesSearch(tab)) continue;
            if (tab.Pinned) { hasPinned = true; break; }
        }

        bool separatorAdded = false;

        foreach (var tab in _tabs)
        {
            if (!MatchesSearch(tab)) continue;

            // Insert zone separator before first unpinned tab
            if (!tab.Pinned && hasPinned && !separatorAdded)
            {
                var separator = new ListBoxItem
                {
                    Content = new Separator { Margin = new Thickness(8, 4, 8, 4) },
                    IsHitTestVisible = false,
                    IsEnabled = false,
                    Focusable = false
                };
                TabList.Items.Add(separator);
                separatorAdded = true;
            }

            var item = CreateTabListItem(tab);
            TabList.Items.Add(item);
        }

        TabList.SelectionChanged += TabList_SelectionChanged;
        _isRebuildingTabList = false;

        // Re-select active tab if still visible
        if (_activeTab is not null)
            SelectTabByNote(_activeTab);
    }

    /// <summary>
    /// Creates a ListBoxItem for a NoteTab using the XAML DataTemplate.
    /// Tag stores the NoteTab reference for later retrieval.
    /// Hover/click behavior is wired in the TabItemBorder_Loaded handler.
    /// </summary>
    private ListBoxItem CreateTabListItem(NoteTab tab)
    {
        var item = new ListBoxItem
        {
            Tag = tab,
            Cursor = Cursors.Hand,
            Content = tab,
            ContentTemplate = (DataTemplate)FindResource("TabItemTemplate")
        };

        // Wire mouse events for drag-to-reorder
        item.PreviewMouseLeftButtonDown += TabItem_PreviewMouseLeftButtonDown;
        item.PreviewMouseMove += TabItem_PreviewMouseMove;
        item.PreviewMouseLeftButtonUp += TabItem_PreviewMouseLeftButtonUp;

        // Middle-click: delete tab
        item.PreviewMouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _ = DeleteTabAsync(tab);
                e.Handled = true;
            }
        };

        // Right-click: show themed context menu
        item.MouseRightButtonUp += (s, e) =>
        {
            var contextMenu = BuildTabContextMenu(tab, item);
            contextMenu.IsOpen = true;
            e.Handled = true;
        };

        return item;
    }

    /// <summary>
    /// Loaded handler for the DataTemplate root Border.
    /// Wires hover show/hide animations and pin/close click handlers.
    /// </summary>
    private void TabItemBorder_Loaded(object sender, RoutedEventArgs e)
    {
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
            _ = TogglePinAsync(tab);
            ev.Handled = true;
        };

        // Pin button hover — behavior differs for pinned vs unpinned
        if (tab.Pinned)
        {
            pinBtn.MouseEnter += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.Text = "\uE77A"; // Unpin glyph
                pinIcon.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
            };
            pinBtn.MouseLeave += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.Text = "\uE718"; // Pin icon
                pinIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            };
        }
        else
        {
            pinBtn.MouseEnter += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.Foreground = (SolidColorBrush)FindResource("c-accent");
            };
            pinBtn.MouseLeave += (s, ev) =>
            {
                if (_isDragging) return;
                pinIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
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
            closeIcon.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
        };
        closeBtn.MouseLeave += (s, ev) =>
        {
            if (_isDragging) return;
            closeIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        };

        // Outer border hover — show/hide pin and close buttons
        outerBorder.MouseEnter += (s, ev) =>
        {
            if (_isDragging) return;

            if (item is not null && item != TabList.SelectedItem)
                outerBorder.Background = GetBrush("c-hover-bg");

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

            if (item is not null && item != TabList.SelectedItem)
                outerBorder.Background = System.Windows.Media.Brushes.Transparent;

            if (!tab.Pinned)
            {
                AnimateOpacity(pinBtn, 1, 0, 100);
                DelayedCollapse(pinBtn);
            }

            AnimateOpacity(closeBtn, 1, 0, 100);
            DelayedCollapse(closeBtn);
        };
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

    // ─── Tab Selection ──────────────────────────────────────────────────────

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
                bool contentChanged = _activeTab.Content != ContentEditor.Text;
                _activeTab.Content = ContentEditor.Text;
                _activeTab.CursorPosition = ContentEditor.CaretIndex;
                if (contentChanged)
                {
                    _activeTab.UpdatedAt = DateTime.Now;
                }
            }

            // Remove background highlight from deselected items and hide buttons
            foreach (var removed in e.RemovedItems)
            {
                if (removed is ListBoxItem oldItem)
                {
                    var oldBorder = FindNamedDescendant<Border>(oldItem, "OuterBorder");
                    if (oldBorder is not null)
                        oldBorder.Background = System.Windows.Media.Brushes.Transparent;

                    var oldPinBtn = FindNamedDescendant<Border>(oldItem, "PinBtn");
                    var oldCloseBtn = FindNamedDescendant<Border>(oldItem, "CloseBtn");

                    // Don't hide pinned tab's always-visible pin icon
                    if (oldPinBtn is not null && !(oldItem.Tag is NoteTab oldTab && oldTab.Pinned))
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
            }

            // Flush autosave service — stops timer, saves content,
            // and updates _lastWriteCompleted to keep write frequency cap accurate
            await _autosaveService.FlushAsync();
            _checkpointTimer.Stop();

            // Save scroll offset for outgoing tab
            if (_activeTab is not null)
            {
                var scrollViewer = GetScrollViewer(ContentEditor);
                if (scrollViewer is not null)
                {
                    _activeTab.EditorScrollOffset = (int)scrollViewer.VerticalOffset;
                }
            }

            // Apply new selection
            if (TabList.SelectedItem is ListBoxItem newItem && newItem.Tag is NoteTab tab)
            {
                ApplyActiveHighlight(newItem);

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
                    var sv = GetScrollViewer(ContentEditor);
                    sv?.ScrollToVerticalOffset(tab.EditorScrollOffset);
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
    /// Applies the background highlight to a selected tab item.
    /// Uses FindNamedDescendant to locate pin/close buttons in the DataTemplate.
    /// </summary>
    private void ApplyActiveHighlight(ListBoxItem item)
    {
        var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
        if (outerBorder is null) return;

        outerBorder.Background = GetBrush("c-selected-bg");

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
    /// Finds and selects the ListBoxItem whose Tag matches the given NoteTab.
    /// </summary>
    private void SelectTabByNote(NoteTab tab)
    {
        foreach (var obj in TabList.Items)
        {
            if (obj is ListBoxItem item && item.Tag is NoteTab t && t.Id == tab.Id)
            {
                TabList.SelectedItem = item;
                try
                {
                    TabList.ScrollIntoView(item);
                }
                catch (NullReferenceException)
                {
                    // WPF VirtualizingStackPanel can throw during layout
                    // if the panel hasn't been measured/arranged yet.
                    // Defer the scroll to after layout completes.
                    Dispatcher.InvokeAsync(() =>
                    {
                        try { TabList.ScrollIntoView(item); }
                        catch (NullReferenceException) { }
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
                return;
            }
        }
    }

    // ─── Tab Panel Resize ────────────────────────────

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

    // ─── Content Save ───────────────────────────────────────────────────────

    /// <summary>
    /// Saves current content from the editor to the active tab model and database.
    /// Also saves scroll offset and pushes undo snapshot.
    /// </summary>
    private void SaveCurrentTabContent()
    {
        if (_activeTab is null) return;
        string currentContent = ContentEditor.Text;
        if (currentContent == _activeTab.Content) return; // No change

        var scrollViewer = GetScrollViewer(ContentEditor);
        int scrollOffset = scrollViewer is not null ? (int)scrollViewer.VerticalOffset : 0;

        ViewModel.SaveEditorStateToTab(currentContent, ContentEditor.CaretIndex, scrollOffset);

        _ = NoteStore.UpdateNoteContentAsync(_activeTab.Id, currentContent);
        UndoManager.Instance.PushSnapshot(_activeTab.Id, currentContent);
    }

    // ─── Tab Creation ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new empty tab, inserts it into the database, adds to the tab list,
    /// selects it, and focuses the editor.
    /// </summary>
    public async Task CreateNewTabAsync()
    {
        SaveCurrentTabContent();

        var (existingPlaceholder, insertIndex, sortOrder) = ViewModel.GetNewTabPosition();

        if (existingPlaceholder is not null)
        {
            SelectTabByNote(existingPlaceholder);
            Keyboard.Focus(ContentEditor);
            return;
        }

        long newId = await NoteStore.InsertNoteAsync(
            _desktopGuid, null, "", false, sortOrder);

        var newTab = new NoteTab
        {
            Id = newId,
            DesktopGuid = _desktopGuid,
            Content = "",
            Pinned = false,
            SortOrder = sortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        ViewModel.InsertNewTab(newTab, insertIndex);
        RebuildTabList();
        SelectTabByNote(newTab);
        Keyboard.Focus(ContentEditor);
    }

    /// <summary>
    /// Creates a new empty tab and focuses it.
    /// Called by App via IPC routing for new-tab commands.
    /// </summary>
    public void RequestNewTab()
    {
        LogService.Info("RequestNewTab called for desktop {DesktopGuid}", _desktopGuid);
        _ = CreateNewTabAsync();
    }

    // ─── New Tab Button ─────────────────────────────────────────────────────

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        _ = CreateNewTabAsync();
    }

    // ─── Pin/Unpin Toggle ─────────────────────────────────────────

    /// <summary>
    /// Toggles the pinned state of a tab, re-sorts the collection, and persists.
    /// Pinned tabs always sort to top.
    /// </summary>
    private async Task TogglePinAsync(NoteTab tab)
    {
        tab.Pinned = !tab.Pinned;
        await NoteStore.UpdateNotePinnedAsync(tab.Id, tab.Pinned);

        ViewModel.ReorderAfterPinToggle(tab);
        await NoteStore.UpdateNoteSortOrdersAsync(_tabs.Select(t => (t.Id, t.SortOrder)));

        RebuildTabList();
        UpdateToolbarState();
    }

    // ─── Clone Tab ────────────────────────────────────────────────

    /// <summary>
    /// Clones the current tab: duplicates content into a new tab inserted after the source.
    /// </summary>
    private async Task CloneTabAsync(NoteTab source)
    {
        SaveCurrentTabContent();

        var (insertIndex, sortOrder) = ViewModel.GetClonePosition(source);

        long newId = await NoteStore.InsertNoteAsync(
            _desktopGuid, source.Name, source.Content,
            source.Pinned, sortOrder);

        var clone = new NoteTab
        {
            Id = newId,
            DesktopGuid = _desktopGuid,
            Name = source.Name,
            Content = source.Content,
            Pinned = source.Pinned,
            SortOrder = sortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        ViewModel.InsertNewTab(clone, insertIndex);
        await NoteStore.UpdateNoteSortOrdersAsync(_tabs.Select(t => (t.Id, t.SortOrder)));

        RebuildTabList();
        SelectTabByNote(clone);
        Keyboard.Focus(ContentEditor);
    }
}
