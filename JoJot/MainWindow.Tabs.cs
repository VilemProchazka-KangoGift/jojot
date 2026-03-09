using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        await DatabaseService.DeleteEmptyNotesAsync(_desktopGuid);

        var notes = await DatabaseService.GetNotesForDesktopAsync(_desktopGuid);
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
    /// Creates a ListBoxItem for a NoteTab with the two-row visual layout.
    /// Tag stores the NoteTab reference for later retrieval.
    /// </summary>
    private ListBoxItem CreateTabListItem(NoteTab tab)
    {
        var item = new ListBoxItem { Tag = tab, Cursor = Cursors.Hand };

        var outerBorder = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Name = "OuterBorder"
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: adaptive column layout based on pinned state
        // Both buttons use 22x22 Border for adequate hit targets
        // UNPINNED: Col 0 = title (Star), Col 1 = pin icon (Auto, hidden), Col 2 = delete icon (Auto, hidden)
        // PINNED:   Col 0 = pin icon (Auto, always visible), Col 1 = title (Star), Col 2 = delete icon (Auto, hidden)
        var row0 = new Grid();
        row0.MinHeight = 22; // Prevent vertical jitter when hover icons toggle Visible/Collapsed
        row0.ColumnDefinitions.Add(new ColumnDefinition { Width = tab.Pinned ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        row0.ColumnDefinitions.Add(new ColumnDefinition { Width = tab.Pinned ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Pin/Unpin action button
        var pinBtn = new Border
        {
            Width = 22, Height = 22,
            CornerRadius = new CornerRadius(3),
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand,
            IsHitTestVisible = true,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Visibility = Visibility.Collapsed
        };

        var pinBtnIcon = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        pinBtnIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");

        if (tab.Pinned)
        {
            // Pinned tabs: show pin icon always, on hover swap to X (click to unpin)
            pinBtnIcon.Text = "\uE718"; // Pin icon
            pinBtn.ToolTip = "Unpin";
            pinBtn.Opacity = 1;
            pinBtn.Visibility = Visibility.Visible;

            pinBtn.MouseEnter += (s, e) =>
            {
                if (_isDragging) return;
                // Unpin glyph (crossed-out pin) instead of multiplication sign
                pinBtnIcon.Text = "\uE77A"; // Unpin glyph (Segoe Fluent Icons)
                // Keep existing FontFamily and FontSize — do NOT change
                pinBtnIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
            };
            pinBtn.MouseLeave += (s, e) =>
            {
                if (_isDragging) return;
                pinBtnIcon.Text = "\uE718"; // Restore pin icon
                pinBtnIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            };
        }
        else
        {
            // Unpinned tabs: show pin icon on hover/selection (click to pin)
            pinBtnIcon.Text = "\uE718"; // Pin icon
            pinBtn.ToolTip = "Pin";

            // Hover color change for unpinned pin button
            pinBtn.MouseEnter += (s, e) =>
            {
                if (_isDragging) return;
                pinBtnIcon.Foreground = (SolidColorBrush)FindResource("c-accent");
            };
            pinBtn.MouseLeave += (s, e) =>
            {
                if (_isDragging) return;
                pinBtnIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            };
        }

        pinBtn.MouseLeftButtonDown += (s, e) =>
        {
            _ = TogglePinAsync(tab);
            e.Handled = true;
        };
        pinBtn.Child = pinBtnIcon;
        // PINNED: pin in Col 0 (always visible), UNPINNED: pin in Col 1 (hidden by default)
        Grid.SetColumn(pinBtn, tab.Pinned ? 0 : 1);
        row0.Children.Add(pinBtn);

        // Title label
        var labelBlock = new TextBlock
        {
            Text = tab.DisplayLabel,
            FontSize = 13,  // Fixed size — tabs do NOT scale with font control
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");

        if (tab.IsPlaceholder)
        {
            labelBlock.FontStyle = FontStyles.Italic;
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        }

        // PINNED: title in Col 1, UNPINNED: title in Col 0
        Grid.SetColumn(labelBlock, tab.Pinned ? 1 : 0);
        row0.Children.Add(labelBlock);

        // Hidden rename TextBox (shown on F2 / double-click) — shares title column
        var renameBox = new TextBox
        {
            FontSize = 13,  // Fixed size to match labelBlock
            MinWidth = 80,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = labelBlock // Store reference to label for show/hide toggling
        };
        Grid.SetColumn(renameBox, tab.Pinned ? 1 : 0);
        row0.Children.Add(renameBox);

        // Column 2: Close/delete button — created for ALL tabs, hidden by default, shown on hover
        var closeBtn = new Border
        {
            Width = 22, Height = 22,
            CornerRadius = new CornerRadius(3),
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand,
            IsHitTestVisible = true,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Visibility = Visibility.Collapsed
        };

        var closeIcon = new TextBlock
        {
            // Fluent ChromeClose glyph at 12pt (bigger than previous 10pt per user request)
            Text = "\uE711",
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");

        // Close button hover color (red) for both pinned and unpinned
        closeBtn.MouseEnter += (s, e) =>
        {
            if (_isDragging) return;
            closeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
        };
        closeBtn.MouseLeave += (s, e) =>
        {
            if (_isDragging) return;
            closeIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        };

        closeBtn.MouseLeftButtonDown += (s, e) =>
        {
            _ = DeleteTabAsync(tab);
            e.Handled = true;
        };

        closeBtn.Child = closeIcon;
        Grid.SetColumn(closeBtn, 2);
        row0.Children.Add(closeBtn);

        Grid.SetRow(row0, 0);
        grid.Children.Add(row0);

        // Row 1: created date (left) + updated time (right)
        var row1 = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var createdBlock = new TextBlock
        {
            Text = tab.CreatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = NoteTab.CreatedTooltip(tab.CreatedAt)
        };
        createdBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        row1.Children.Add(createdBlock);
        var updatedBlock = new TextBlock
        {
            Text = tab.UpdatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = NoteTab.UpdatedTooltip(tab.UpdatedAt)
        };
        updatedBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        row1.Children.Add(updatedBlock);
        Grid.SetRow(row1, 1);
        grid.Children.Add(row1);

        // Show/hide pin and close buttons on hover
        outerBorder.MouseEnter += (s, e) =>
        {
            // Suppress hover effects during drag to prevent visual artifacts
            if (_isDragging) return;

            if (item != TabList.SelectedItem)
                outerBorder.Background = GetBrush("c-hover-bg");

            // UNPINNED: show both pin and close on hover
            // PINNED: pin already visible, show close only
            if (!tab.Pinned)
            {
                pinBtn.Visibility = Visibility.Visible;
                AnimateOpacity(pinBtn, 0, 1, 100);
            }

            // Show close button for ALL tabs on hover
            closeBtn.Visibility = Visibility.Visible;
            AnimateOpacity(closeBtn, 0, 1, 100);
        };
        outerBorder.MouseLeave += (s, e) =>
        {
            // Suppress hover effects during drag to prevent visual artifacts
            if (_isDragging) return;

            if (item != TabList.SelectedItem)
                outerBorder.Background = System.Windows.Media.Brushes.Transparent;

            // UNPINNED: hide pin on leave (pinned tabs keep pin visible always)
            if (!tab.Pinned)
            {
                AnimateOpacity(pinBtn, 1, 0, 100);
                DelayedCollapse(pinBtn);
            }

            // Hide close button for ALL tabs on leave
            AnimateOpacity(closeBtn, 1, 0, 100);
            DelayedCollapse(closeBtn);
        };

        outerBorder.Child = grid;
        item.Content = outerBorder;

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
                e.Handled = true; // Prevent WPF auto-scroll on middle-click
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
    /// Updates the visual display of a tab item after model changes (name, content, dates).
    /// Finds the ListBoxItem and rebuilds its content.
    /// </summary>
    private void UpdateTabItemDisplay(NoteTab tab)
    {
        // If a rename is active for this tab, skip the rebuild to avoid
        // destroying the rename TextBox. CommitRename will call us again after finishing.
        if (_activeRename is var (_, renTab, _, _) && renTab.Id == tab.Id)
            return;

        foreach (var obj in TabList.Items)
        {
            if (obj is ListBoxItem item && item.Tag is NoteTab t && t.Id == tab.Id)
            {
                bool wasSelected = TabList.SelectedItem == item;

                // Rebuild the item content
                var newItem = CreateTabListItem(tab);
                int index = TabList.Items.IndexOf(item);
                TabList.SelectionChanged -= TabList_SelectionChanged;
                TabList.Items[index] = newItem;
                if (wasSelected)
                {
                    TabList.SelectedItem = newItem;     // guarded — no SelectionChanged fired
                }
                TabList.SelectionChanged += TabList_SelectionChanged;

                if (wasSelected)
                {
                    ApplyActiveHighlight(newItem);
                }
                return;
            }
        }
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
                if (removed is ListBoxItem oldItem && oldItem.Content is Border oldBorder)
                {
                    oldBorder.Background = System.Windows.Media.Brushes.Transparent;

                    // Hide pin/close buttons when deselected
                    if (oldBorder.Child is Grid oldGrid && oldGrid.Children.Count > 0 && oldGrid.Children[0] is Grid oldRow0)
                    {
                        foreach (var child in oldRow0.Children)
                        {
                            if (child is Border btn && btn.Width == 22 && btn.Height == 22)
                            {
                                // Don't hide pinned tab's always-visible pin icon
                                if (oldItem.Tag is NoteTab oldTab && oldTab.Pinned && Grid.GetColumn(btn) == 0)
                                {
                                    continue;
                                }

                                btn.Opacity = 0;
                                btn.Visibility = Visibility.Collapsed;
                            }
                        }
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
        }
        catch (Exception ex)
        {
            LogService.Warn("Tab selection change failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Applies the background highlight to a selected tab item.
    /// </summary>
    private void ApplyActiveHighlight(ListBoxItem item)
    {
        if (item.Content is Border border)
        {
            border.Background = GetBrush("c-selected-bg");

            // Show pin/close buttons on selected tab (no hover needed)
            if (border.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Grid row0)
            {
                foreach (var child in row0.Children)
                {
                    if (child is Border btn && btn.Width == 22 && btn.Height == 22)
                    {
                        btn.Visibility = Visibility.Visible;
                        btn.Opacity = 1;
                    }
                }
            }
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
        var saved = await DatabaseService.GetPreferenceAsync("tab_panel_width");
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
            await DatabaseService.SetPreferenceAsync("tab_panel_width",
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

        _activeTab.Content = currentContent;
        _activeTab.CursorPosition = ContentEditor.CaretIndex;
        _activeTab.UpdatedAt = DateTime.Now;

        // Save scroll offset
        var scrollViewer = GetScrollViewer(ContentEditor);
        if (scrollViewer is not null)
            _activeTab.EditorScrollOffset = (int)scrollViewer.VerticalOffset;

        _ = DatabaseService.UpdateNoteContentAsync(_activeTab.Id, currentContent);

        // Push undo snapshot on explicit save
        UndoManager.Instance.PushSnapshot(_activeTab.Id, currentContent);

        // Refresh display label in case content changed the fallback
        UpdateTabItemDisplay(_activeTab);
    }

    // ─── Tab Creation ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new empty tab, inserts it into the database, adds to the tab list,
    /// selects it, and focuses the editor.
    /// </summary>
    public async Task CreateNewTabAsync()
    {
        SaveCurrentTabContent();

        // Insert position: right after pinned tabs
        int pinnedCount = _tabs.Count(t => t.Pinned);

        // If the first unpinned tab is already empty (no title, no content), just focus it
        if (pinnedCount < _tabs.Count)
        {
            var firstUnpinned = _tabs[pinnedCount];
            if (firstUnpinned.IsPlaceholder)
            {
                SelectTabByNote(firstUnpinned);
                Keyboard.Focus(ContentEditor);
                return;
            }
        }

        // Sort order: one less than the minimum unpinned sort_order
        int minUnpinnedSort = _tabs.Where(t => !t.Pinned)
            .Select(t => t.SortOrder).DefaultIfEmpty(0).Min();
        int newSortOrder = minUnpinnedSort - 1;

        long newId = await DatabaseService.InsertNoteAsync(
            _desktopGuid, null, "", false, newSortOrder);

        var newTab = new NoteTab
        {
            Id = newId,
            DesktopGuid = _desktopGuid,
            Content = "",
            Pinned = false,
            SortOrder = newSortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _tabs.Insert(pinnedCount, newTab);
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
        await DatabaseService.UpdateNotePinnedAsync(tab.Id, tab.Pinned);

        // Re-sort: pinned to top, then by sort_order
        var sorted = _tabs.OrderByDescending(t => t.Pinned).ThenBy(t => t.SortOrder).ToList();
        _tabs.Clear();
        foreach (var t in sorted) _tabs.Add(t);

        // Reassign sort_order to match new positions
        for (int i = 0; i < _tabs.Count; i++)
            _tabs[i].SortOrder = i;
        await DatabaseService.UpdateNoteSortOrdersAsync(_tabs.Select(t => (t.Id, t.SortOrder)));

        RebuildTabList();
        UpdateToolbarState(); // refresh pin icon
    }

    // ─── Clone Tab ────────────────────────────────────────────────

    /// <summary>
    /// Clones the current tab: duplicates content into a new tab inserted after the source.
    /// </summary>
    private async Task CloneTabAsync(NoteTab source)
    {
        SaveCurrentTabContent();

        int newSortOrder = source.SortOrder + 1;

        // Shift sort_order of all tabs after the clone position
        foreach (var tab in _tabs.Where(t => t.SortOrder >= newSortOrder))
            tab.SortOrder++;

        long newId = await DatabaseService.InsertNoteAsync(
            _desktopGuid, source.Name, source.Content,
            source.Pinned, newSortOrder);

        var clone = new NoteTab
        {
            Id = newId,
            DesktopGuid = _desktopGuid,
            Name = source.Name,
            Content = source.Content,
            Pinned = source.Pinned,
            SortOrder = newSortOrder,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        // Insert into collection at correct position
        int insertIndex = _tabs.IndexOf(source) + 1;
        if (insertIndex >= _tabs.Count)
            _tabs.Add(clone);
        else
            _tabs.Insert(insertIndex, clone);

        // Persist sort orders
        await DatabaseService.UpdateNoteSortOrdersAsync(_tabs.Select(t => (t.Id, t.SortOrder)));

        RebuildTabList();
        SelectTabByNote(clone);
        Keyboard.Focus(ContentEditor);
    }
}
