using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;

namespace JoJot
{
    /// <summary>
    /// Per-desktop window bound to a virtual desktop GUID.
    /// Phase 4: Full tab management UI with tab panel, content editor, search, and keyboard navigation.
    /// Windows are created on-demand and destroyed on close (not hidden).
    /// The process stays alive via ShutdownMode.OnExplicitShutdown.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string _desktopGuid;
        private readonly ObservableCollection<NoteTab> _tabs = new();
        private NoteTab? _activeTab;
        private string _searchText = "";

        // ─── Rename state ───────────────────────────────────────────────────────
        private (ListBoxItem Item, NoteTab Tab, TextBox Box, TextBlock Label)? _activeRename;

        // ─── Drag-to-reorder state ──────────────────────────────────────────────
        private bool _isDragging;
        private System.Windows.Point _dragStartPoint;
        private ListBoxItem? _dragItem;
        private NoteTab? _dragTab;
        private int _dragInsertIndex = -1;
        private Border? _dropIndicatorBorder;

        // ─── Soft-delete / toast state (Phase 5) ────────────────────────────────
        private record PendingDeletion(
            List<NoteTab> Tabs,
            List<int> OriginalIndexes,
            CancellationTokenSource Cts
        );

        private PendingDeletion? _pendingDeletion;

        // ─── Accent color (pre-theming hardcoded, Phase 7 replaces with token) ──
        private static readonly SolidColorBrush AccentBrush =
            new(System.Windows.Media.Color.FromRgb(0x21, 0x96, 0xF3));
        private static readonly SolidColorBrush MutedTextBrush =
            new(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush HoverBrush =
            new(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0));

        /// <summary>
        /// Creates a MainWindow bound to a specific virtual desktop.
        /// The desktopGuid is used for geometry save/restore and window registry keying.
        /// </summary>
        public MainWindow(string desktopGuid)
        {
            _desktopGuid = desktopGuid;
            InitializeComponent();

            // Handle drag cancellation when mouse capture is lost
            TabList.LostMouseCapture += (s, e) =>
            {
                if (_isDragging) CompleteDrag();
            };
        }

        /// <summary>
        /// The virtual desktop GUID this window is bound to.
        /// Used by App for registry keying and event routing.
        /// </summary>
        public string DesktopGuid => _desktopGuid;

        // ─── Tab Loading ────────────────────────────────────────────────────────

        /// <summary>
        /// Loads tabs from database for this desktop and populates the tab list.
        /// Called by App.CreateWindowForDesktop after Show().
        /// Auto-creates an empty tab if no notes exist (per CONTEXT.md).
        /// </summary>
        public async Task LoadTabsAsync()
        {
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

            // Re-select active tab if still visible
            if (_activeTab != null)
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
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Background = System.Windows.Media.Brushes.Transparent,
                Name = "OuterBorder"
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: pin icon + label
            var row0 = new StackPanel { Orientation = Orientation.Horizontal };

            if (tab.Pinned)
            {
                row0.Children.Add(new TextBlock
                {
                    Text = "\U0001F4CC",
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var labelBlock = new TextBlock
            {
                Text = tab.DisplayLabel,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = tab.Pinned ? 120 : 140,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (tab.IsPlaceholder)
            {
                labelBlock.FontStyle = FontStyles.Italic;
                labelBlock.Foreground = MutedTextBrush;
            }

            row0.Children.Add(labelBlock);

            // Hidden rename TextBox (shown on F2 / double-click)
            var renameBox = new TextBox
            {
                FontSize = 13,
                MinWidth = 80,
                MaxWidth = 140,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = labelBlock // Store reference to label for show/hide toggling
            };
            row0.Children.Add(renameBox);

            Grid.SetRow(row0, 0);
            grid.Children.Add(row0);

            // Row 1: created date (left) + updated time (right)
            var row1 = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row1.Children.Add(new TextBlock
            {
                Text = tab.CreatedDisplay,
                FontSize = 10,
                Foreground = MutedTextBrush,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            row1.Children.Add(new TextBlock
            {
                Text = tab.UpdatedDisplay,
                FontSize = 10,
                Foreground = MutedTextBrush,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetRow(row1, 1);
            grid.Children.Add(row1);

            // Delete icon: × overlay, upper-right, hidden until hover (TDEL-03)
            var deleteIcon = new TextBlock
            {
                Text = "\u00D7",
                FontSize = 12,
                Foreground = MutedTextBrush,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
                Cursor = Cursors.Hand,
                IsHitTestVisible = true
            };
            Grid.SetRowSpan(deleteIcon, 2);
            grid.Children.Add(deleteIcon);

            // Fade icon in/out on tab hover
            outerBorder.MouseEnter += (s, e) =>
            {
                if (item != TabList.SelectedItem)
                    outerBorder.Background = HoverBrush;
                AnimateOpacity(deleteIcon, 0, 1, 100);
            };
            outerBorder.MouseLeave += (s, e) =>
            {
                if (item != TabList.SelectedItem)
                    outerBorder.Background = System.Windows.Media.Brushes.Transparent;
                AnimateOpacity(deleteIcon, 1, 0, 100);
            };

            // Color change on x icon hover: gray → red → gray
            deleteIcon.MouseEnter += (s, e) =>
                deleteIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
            deleteIcon.MouseLeave += (s, e) =>
                deleteIcon.Foreground = MutedTextBrush;

            // Click x to delete tab
            deleteIcon.MouseLeftButtonDown += (s, e) =>
            {
                _ = DeleteTabAsync(tab);
                e.Handled = true; // Prevent bubbling to ListBoxItem selection
            };

            outerBorder.Child = grid;
            item.Content = outerBorder;

            // Wire mouse events for drag-to-reorder (Phase 4 Plan 03 fills in drag logic)
            item.PreviewMouseLeftButtonDown += TabItem_PreviewMouseLeftButtonDown;
            item.PreviewMouseMove += TabItem_PreviewMouseMove;
            item.PreviewMouseLeftButtonUp += TabItem_PreviewMouseLeftButtonUp;

            // Middle-click: delete tab (TDEL-04)
            item.PreviewMouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    _ = DeleteTabAsync(tab);
                    e.Handled = true; // Prevent WPF auto-scroll on middle-click
                }
            };

            return item;
        }

        /// <summary>
        /// Updates the visual display of a tab item after model changes (name, content, dates).
        /// Finds the ListBoxItem and rebuilds its content.
        /// </summary>
        private void UpdateTabItemDisplay(NoteTab tab)
        {
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
                    TabList.SelectionChanged += TabList_SelectionChanged;

                    if (wasSelected)
                    {
                        TabList.SelectedItem = newItem;
                        ApplyActiveHighlight(newItem);
                    }
                    return;
                }
            }
        }

        // ─── Tab Selection ──────────────────────────────────────────────────────

        /// <summary>
        /// Handles tab selection changes: saves current content, loads new tab content.
        /// Applies 2px left accent border to active tab (TABS-04).
        /// </summary>
        private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Remove accent from deselected items
            foreach (var removed in e.RemovedItems)
            {
                if (removed is ListBoxItem oldItem && oldItem.Content is Border oldBorder)
                {
                    oldBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                    oldBorder.Background = System.Windows.Media.Brushes.Transparent;
                }
            }

            // Save current tab content before switching
            SaveCurrentTabContent();

            // Apply new selection
            if (TabList.SelectedItem is ListBoxItem newItem && newItem.Tag is NoteTab tab)
            {
                ApplyActiveHighlight(newItem);

                _activeTab = tab;
                ContentEditor.Text = tab.Content;
                ContentEditor.IsEnabled = true;

                // Restore cursor position (best effort — clamp to content length)
                ContentEditor.CaretIndex = Math.Min(tab.CursorPosition, ContentEditor.Text.Length);
            }
            else
            {
                _activeTab = null;
                ContentEditor.Text = "";
                ContentEditor.IsEnabled = false;
            }
        }

        /// <summary>
        /// Applies the 2px left accent border to a selected tab item.
        /// </summary>
        private static void ApplyActiveHighlight(ListBoxItem item)
        {
            if (item.Content is Border border)
            {
                border.BorderBrush = AccentBrush;
                border.Background = System.Windows.Media.Brushes.Transparent; // Clear hover if active
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
                    TabList.ScrollIntoView(item);
                    return;
                }
            }
        }

        // ─── Content Save ───────────────────────────────────────────────────────

        /// <summary>
        /// Saves current content from the editor to the active tab model and database.
        /// No-op if no changes or no active tab. Fire-and-forget for database persistence.
        /// </summary>
        private void SaveCurrentTabContent()
        {
            if (_activeTab == null) return;
            string currentContent = ContentEditor.Text;
            if (currentContent == _activeTab.Content) return; // No change

            _activeTab.Content = currentContent;
            _activeTab.CursorPosition = ContentEditor.CaretIndex;
            _activeTab.UpdatedAt = DateTime.Now;
            _ = DatabaseService.UpdateNoteContentAsync(_activeTab.Id, currentContent);

            // Refresh display label in case content changed the fallback
            UpdateTabItemDisplay(_activeTab);
        }

        // ─── Tab Creation ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new empty tab, inserts it into the database, adds to the tab list,
        /// selects it, and focuses the editor (TABS-08).
        /// </summary>
        public async Task CreateNewTabAsync()
        {
            SaveCurrentTabContent();

            int maxSort = await DatabaseService.GetMaxSortOrderAsync(_desktopGuid);
            long newId = await DatabaseService.InsertNoteAsync(
                _desktopGuid, null, "", false, maxSort + 1);

            var newTab = new NoteTab
            {
                Id = newId,
                DesktopGuid = _desktopGuid,
                Content = "",
                Pinned = false,
                SortOrder = maxSort + 1,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _tabs.Add(newTab);

            var item = CreateTabListItem(newTab);
            TabList.Items.Add(item);
            TabList.SelectedItem = item;
            TabList.ScrollIntoView(item);
            Keyboard.Focus(ContentEditor);
        }

        /// <summary>
        /// Creates a new empty tab and focuses it.
        /// Called by App via IPC routing for new-tab commands.
        /// </summary>
        public void RequestNewTab()
        {
            LogService.Info($"RequestNewTab called for desktop {_desktopGuid}");
            _ = CreateNewTabAsync();
        }

        // ─── Search ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Real-time search filtering. Rebuilds the tab list hiding non-matches (TABS-11).
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text;
            SearchPlaceholder.Visibility = SearchBox.Text.Length > 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            RebuildTabList();
        }

        /// <summary>
        /// Escape clears search and returns focus to editor (TABS-12).
        /// </summary>
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = "";
                _searchText = "";
                SearchPlaceholder.Visibility = Visibility.Visible;
                RebuildTabList();
                Keyboard.Focus(ContentEditor);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Tests whether a tab matches the current search text (case-insensitive).
        /// Matches against DisplayLabel and Content.
        /// </summary>
        private bool MatchesSearch(NoteTab tab)
        {
            if (string.IsNullOrEmpty(_searchText)) return true;
            return tab.DisplayLabel.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || tab.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        // ─── Keyboard Shortcuts ─────────────────────────────────────────────────

        /// <summary>
        /// Window-level keyboard shortcut handler.
        /// Ctrl+W: delete active tab, Ctrl+T: new tab, Ctrl+F: focus search,
        /// Ctrl+Tab/Ctrl+Shift+Tab: cycle tabs, Ctrl+P: pin/unpin, Ctrl+K: clone, F2: rename.
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+W: Delete active tab (TDEL-01)
            if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null)
                {
                    _ = DeleteTabAsync(_activeTab);
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+P: Pin/unpin toggle (TABS-10)
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null)
                {
                    _ = TogglePinAsync(_activeTab);
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+K: Clone tab (TABS-09)
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null)
                {
                    _ = CloneTabAsync(_activeTab);
                    e.Handled = true;
                }
                return;
            }

            // F2: Rename active tab (TABS-06)
            if (e.Key == Key.F2 && TabList.SelectedItem is ListBoxItem f2Item && f2Item.Tag is NoteTab f2Tab)
            {
                BeginRename(f2Item, f2Tab);
                e.Handled = true;
                return;
            }

            // Ctrl+T: New tab (TABS-08)
            if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = CreateNewTabAsync();
                e.Handled = true;
                return;
            }

            // Ctrl+F: Focus search (TABS-11)
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }

            // Ctrl+Tab / Ctrl+Shift+Tab: Cycle tabs (TABS-13)
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                int count = TabList.Items.Count;
                if (count <= 1) { e.Handled = true; return; }

                int current = TabList.SelectedIndex;
                if (current < 0) current = 0;

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Previous tab: skip separator items
                    int next = current;
                    do { next = (next - 1 + count) % count; }
                    while (next != current && TabList.Items[next] is ListBoxItem li && li.Tag is not NoteTab);
                    TabList.SelectedIndex = next;
                }
                else
                {
                    // Next tab: skip separator items
                    int next = current;
                    do { next = (next + 1) % count; }
                    while (next != current && TabList.Items[next] is ListBoxItem li && li.Tag is not NoteTab);
                    TabList.SelectedIndex = next;
                }

                e.Handled = true;
                return;
            }
        }

        /// <summary>
        /// Tab list keyboard handler. F2 triggers rename (TABS-06).
        /// </summary>
        private void TabList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && TabList.SelectedItem is ListBoxItem selItem && selItem.Tag is NoteTab selTab)
            {
                BeginRename(selItem, selTab);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Double-click on tab triggers rename (TABS-06).
        /// </summary>
        private void TabList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TabList.SelectedItem is ListBoxItem item && item.Tag is NoteTab tab)
            {
                BeginRename(item, tab);
                e.Handled = true;
            }
        }

        // ─── New Tab Button ─────────────────────────────────────────────────────

        private void NewTabButton_Click(object sender, RoutedEventArgs e)
        {
            _ = CreateNewTabAsync();
        }

        // ─── Inline Rename (TABS-06, TABS-07) ──────────────────────────────────

        /// <summary>
        /// Starts inline rename for a tab. Hides the label TextBlock and shows a TextBox.
        /// </summary>
        private void BeginRename(ListBoxItem item, NoteTab tab)
        {
            if (_activeRename != null) CommitRename();
            if (_isDragging) return;

            // Find the rename TextBox and label in this item's visual tree
            var renameBox = FindDescendant<TextBox>(item);
            if (renameBox?.Tag is not TextBlock labelBlock) return;

            labelBlock.Visibility = Visibility.Collapsed;
            renameBox.Text = tab.Name ?? "";
            renameBox.Visibility = Visibility.Visible;
            renameBox.SelectAll();
            Keyboard.Focus(renameBox);

            _activeRename = (item, tab, renameBox, labelBlock);

            renameBox.PreviewKeyDown += RenameBox_PreviewKeyDown;
            renameBox.LostFocus += RenameBox_LostFocus;
        }

        /// <summary>
        /// Commits the rename: updates the model, persists to database, refreshes display.
        /// TABS-07: Empty/whitespace clears custom name, reverts to content fallback.
        /// </summary>
        private void CommitRename()
        {
            if (_activeRename == null) return;
            var (item, tab, box, labelBlock) = _activeRename.Value;

            box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
            box.LostFocus -= RenameBox_LostFocus;

            string newName = box.Text.Trim();
            tab.Name = string.IsNullOrWhiteSpace(newName) ? null : newName;

            box.Visibility = Visibility.Collapsed;
            labelBlock.Visibility = Visibility.Visible;

            _activeRename = null;

            UpdateTabItemDisplay(tab);
            _ = DatabaseService.UpdateNoteNameAsync(tab.Id, tab.Name);
        }

        /// <summary>
        /// Cancels the rename, restoring the original label without saving.
        /// </summary>
        private void CancelRename()
        {
            if (_activeRename == null) return;
            var (_, _, box, labelBlock) = _activeRename.Value;

            box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
            box.LostFocus -= RenameBox_LostFocus;

            box.Visibility = Visibility.Collapsed;
            labelBlock.Visibility = Visibility.Visible;

            _activeRename = null;
        }

        private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        }

        private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitRename();
        }

        // ─── Pin/Unpin Toggle (TABS-10) ─────────────────────────────────────────

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
            SelectTabByNote(tab);
        }

        // ─── Clone Tab (TABS-09) ────────────────────────────────────────────────

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

        // ─── Drag-to-Reorder (TABS-05) ─────────────────────────────────────────

        private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeRename != null) return; // No drag during rename
            if (!string.IsNullOrEmpty(_searchText)) return; // No drag during search

            _dragStartPoint = e.GetPosition(TabList);
            _dragItem = sender as ListBoxItem;
            _dragTab = _dragItem?.Tag as NoteTab;
        }

        private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || _dragTab == null)
                return;

            System.Windows.Point current = e.GetPosition(TabList);
            Vector diff = _dragStartPoint - current;

            // Minimum 5px distance before drag starts
            if (Math.Abs(diff.Y) < 5 && Math.Abs(diff.X) < 5) return;

            if (!_isDragging)
            {
                _isDragging = true;
                _dragItem.Opacity = 0.6;
                Mouse.Capture(TabList);
            }

            UpdateDropIndicator(current);
        }

        private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CompleteDrag();
        }

        /// <summary>
        /// Updates the drop indicator position during drag.
        /// Enforces zone boundaries: pinned tabs stay in pinned zone, unpinned in unpinned.
        /// </summary>
        private void UpdateDropIndicator(System.Windows.Point mousePos)
        {
            RemoveDropIndicator();

            _dragInsertIndex = -1;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < TabList.Items.Count; i++)
            {
                if (TabList.Items[i] is not ListBoxItem candidate || candidate.Tag is not NoteTab candidateTab)
                    continue;

                // Zone enforcement: only allow drop between same-zone items
                if (candidateTab.Pinned != _dragTab!.Pinned) continue;

                try
                {
                    var transform = candidate.TransformToAncestor(TabList);
                    var itemPos = transform.Transform(new System.Windows.Point(0, 0));

                    // Check distance to top edge of item
                    double distTop = Math.Abs(mousePos.Y - itemPos.Y);
                    if (distTop < bestDistance)
                    {
                        bestDistance = distTop;
                        _dragInsertIndex = i;
                    }

                    // Check distance to bottom edge of item
                    double distBottom = Math.Abs(mousePos.Y - (itemPos.Y + candidate.ActualHeight));
                    if (distBottom < bestDistance)
                    {
                        bestDistance = distBottom;
                        _dragInsertIndex = i + 1;
                    }
                }
                catch
                {
                    // TransformToAncestor can fail if item isn't in visual tree
                }
            }

            // Show visual indicator on the border of the target item
            if (_dragInsertIndex >= 0 && _dragInsertIndex < TabList.Items.Count)
            {
                if (TabList.Items[_dragInsertIndex] is ListBoxItem targetItem && targetItem.Content is Border border)
                {
                    border.BorderThickness = new Thickness(border.BorderThickness.Left, 2, 0, 0);
                    border.BorderBrush = AccentBrush;
                    _dropIndicatorBorder = border;
                }
            }
        }

        /// <summary>
        /// Completes the drag operation: moves the tab in the collection, updates sort orders.
        /// </summary>
        private void CompleteDrag()
        {
            if (!_isDragging) { ResetDragState(); return; }

            Mouse.Capture(null);
            RemoveDropIndicator();

            if (_dragItem != null) _dragItem.Opacity = 1.0;

            if (_dragInsertIndex >= 0 && _dragTab != null)
            {
                int oldIndex = _tabs.IndexOf(_dragTab);
                int newIndex = CalculateCollectionIndex(_dragInsertIndex);

                if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                {
                    // Adjust for removal shifting indexes
                    if (newIndex > oldIndex) newIndex--;

                    _tabs.Move(oldIndex, newIndex);

                    // Reassign sort_order to match new collection order
                    for (int i = 0; i < _tabs.Count; i++)
                        _tabs[i].SortOrder = i;

                    _ = DatabaseService.UpdateNoteSortOrdersAsync(
                        _tabs.Select(t => (t.Id, t.SortOrder)));

                    RebuildTabList();
                    SelectTabByNote(_dragTab);
                }
            }

            ResetDragState();
        }

        /// <summary>
        /// Maps a ListBoxItem index (which may include separator items) to a _tabs collection index.
        /// </summary>
        private int CalculateCollectionIndex(int listBoxIndex)
        {
            int collectionIndex = 0;
            for (int i = 0; i < listBoxIndex && i < TabList.Items.Count; i++)
            {
                if (TabList.Items[i] is ListBoxItem item && item.Tag is NoteTab)
                    collectionIndex++;
            }
            return Math.Min(collectionIndex, _tabs.Count);
        }

        /// <summary>
        /// Removes the visual drop indicator, restoring the border to its original state.
        /// </summary>
        private void RemoveDropIndicator()
        {
            if (_dropIndicatorBorder != null)
            {
                _dropIndicatorBorder.BorderThickness = new Thickness(2, 0, 0, 0);
                // Restore border brush based on selection state
                var parentItem = _dropIndicatorBorder.Parent as ListBoxItem;
                _dropIndicatorBorder.BorderBrush = parentItem != null && TabList.SelectedItem == parentItem
                    ? AccentBrush
                    : System.Windows.Media.Brushes.Transparent;
                _dropIndicatorBorder = null;
            }
        }

        private void ResetDragState()
        {
            _isDragging = false;
            _dragItem = null;
            _dragTab = null;
            _dragInsertIndex = -1;
        }

        // ─── Visual Tree Helper ─────────────────────────────────────────────────

        /// <summary>
        /// Walks the visual tree to find the first descendant of type T.
        /// </summary>
        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        // ─── Deletion Engine (Phase 5: TDEL-02, TDEL-05, TDEL-06, TOST-02, TOST-03, TOST-04, TOST-05) ─

        /// <summary>
        /// Hard-deletes all tabs in the current pending deletion from the database.
        /// Called before creating a new pending deletion (TOST-04) or after the 4s timer fires.
        /// Safe to call when _pendingDeletion is null.
        /// </summary>
        private async Task CommitPendingDeletionAsync()
        {
            if (_pendingDeletion == null) return;

            // Capture and clear before any awaits to prevent double-dispose races
            var pending = _pendingDeletion;
            _pendingDeletion = null;

            pending.Cts.Cancel();
            pending.Cts.Dispose();

            foreach (var tab in pending.Tabs)
                await DatabaseService.DeleteNoteAsync(tab.Id);
        }

        /// <summary>
        /// Soft-deletes a single tab: removes from UI immediately (TDEL-02), shows undo toast,
        /// and schedules a hard-delete after 4 seconds. Commits any previous pending deletion first.
        /// </summary>
        private async Task DeleteTabAsync(NoteTab tab)
        {
            SaveCurrentTabContent();
            await CommitPendingDeletionAsync();

            int originalIndex = _tabs.IndexOf(tab);
            bool wasActive = (_activeTab?.Id == tab.Id);

            _tabs.Remove(tab);
            RebuildTabList();

            if (wasActive)
                await ApplyFocusCascadeAsync(originalIndex);

            var cts = new CancellationTokenSource();
            _pendingDeletion = new PendingDeletion([tab], [originalIndex], cts);
            ShowToast(isBulk: false, label: tab.DisplayLabel);
            _ = StartDismissTimerAsync(cts.Token);
        }

        /// <summary>
        /// Soft-deletes multiple tabs at once, skipping pinned tabs (TDEL-06, TOST-05).
        /// Shows "N notes deleted" toast and schedules a bulk hard-delete after 4 seconds.
        /// </summary>
        private async Task DeleteMultipleAsync(IEnumerable<NoteTab> candidates)
        {
            var toDelete = candidates.Where(t => !t.Pinned).ToList();
            if (toDelete.Count == 0) return;

            SaveCurrentTabContent();
            await CommitPendingDeletionAsync();

            // Capture original indexes before any removal
            var originalIndexes = toDelete.Select(t => _tabs.IndexOf(t)).ToList();

            bool wasActive = _activeTab != null && toDelete.Any(t => t.Id == _activeTab.Id);
            int activeOriginalIndex = wasActive ? _tabs.IndexOf(_activeTab!) : 0;

            foreach (var tab in toDelete)
                _tabs.Remove(tab);

            RebuildTabList();

            if (wasActive)
                await ApplyFocusCascadeAsync(activeOriginalIndex);

            var cts = new CancellationTokenSource();
            _pendingDeletion = new PendingDeletion(toDelete, originalIndexes, cts);
            ShowToast(isBulk: true, count: toDelete.Count);
            _ = StartDismissTimerAsync(cts.Token);
        }

        /// <summary>
        /// Waits 4 seconds then commits the pending deletion and hides the toast (TOST-02).
        /// Cancellation (via undo or new deletion) silently aborts the timer.
        /// </summary>
        private async Task StartDismissTimerAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(4000, token);
                // Timer fired — commit deletion and slide down the toast
                await CommitPendingDeletionAsync();
                HideToast();
            }
            catch (OperationCanceledException)
            {
                // Cancelled by undo or new deletion — do nothing
            }
        }

        /// <summary>
        /// Undo handler: re-inserts the pending tabs at their original positions (TOST-03).
        /// Cancels the dismiss timer so no hard-delete occurs.
        /// </summary>
        private async Task UndoDeleteAsync()
        {
            if (_pendingDeletion == null) return;

            var pending = _pendingDeletion;
            _pendingDeletion = null;

            // Cancel and dispose the timer CTS — no hard-delete will occur
            pending.Cts.Cancel();
            pending.Cts.Dispose();

            // Re-insert in ascending index order with clamping to handle shifted indexes
            var pairs = pending.Tabs.Zip(pending.OriginalIndexes, (tab, idx) => (tab, idx))
                                    .OrderBy(p => p.idx)
                                    .ToList();

            foreach (var (tab, originalIndex) in pairs)
            {
                int insertAt = Math.Min(originalIndex, _tabs.Count);
                _tabs.Insert(insertAt, tab);
            }

            RebuildTabList();

            // Select the first restored tab
            SelectTabByNote(pending.Tabs[0]);

            HideToast();

            await Task.CompletedTask; // Satisfies async contract; logic is synchronous
        }

        /// <summary>
        /// Focus cascade after deleting the active tab (TDEL-05):
        /// 1. First visible tab at or below the deleted position
        /// 2. Last visible tab (if no tab below)
        /// 3. Clear search and recurse if search is hiding all tabs
        /// 4. Create a new empty tab if no tabs exist at all
        /// </summary>
        private async Task ApplyFocusCascadeAsync(int deletedIndex)
        {
            var visible = _tabs.Where(t => MatchesSearch(t)).ToList();

            if (visible.Count > 0)
            {
                // Find the first visible tab whose _tabs position >= deletedIndex
                NoteTab? target = null;
                foreach (var t in visible)
                {
                    if (_tabs.IndexOf(t) >= deletedIndex)
                    {
                        target = t;
                        break;
                    }
                }
                // Fallback to last visible tab
                target ??= visible[^1];
                SelectTabByNote(target);
            }
            else if (!string.IsNullOrEmpty(_searchText))
            {
                // Search is active and hiding everything — clear it and recurse
                SearchBox.Text = "";
                _searchText = "";
                SearchPlaceholder.Visibility = Visibility.Visible;
                RebuildTabList();
                await ApplyFocusCascadeAsync(0);
            }
            else
            {
                // No tabs at all — auto-create an empty tab
                await CreateNewTabAsync();
            }
        }

        // ─── Toast Overlay (Phase 5: TOST-01 through TOST-06) ──────────────────

        /// <summary>
        /// Sets toast text for a single-tab deletion: e.g. "Note name" deleted
        /// with the name portion in italic. Truncates raw label to 30 chars per TOST-06.
        /// </summary>
        private void UpdateToastContent(string rawLabel)
        {
            string truncated = rawLabel.Length > 30 ? rawLabel[..30] : rawLabel;

            ToastMessageBlock.Inlines.Clear();
            ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201C"));
            var italicRun = new System.Windows.Documents.Run(truncated) { FontStyle = FontStyles.Italic };
            ToastMessageBlock.Inlines.Add(italicRun);
            ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201D deleted"));
        }

        /// <summary>
        /// Sets toast text for a bulk deletion: "{count} notes deleted".
        /// </summary>
        private void UpdateToastContentBulk(int count)
        {
            ToastMessageBlock.Inlines.Clear();
            ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run($"{count} notes deleted"));
        }

        /// <summary>
        /// Animates the Opacity property of a UIElement from one value to another over durationMs.
        /// Used for the tab x icon fade-in/out on hover (TDEL-03).
        /// </summary>
        private static void AnimateOpacity(UIElement element, double from, double to, int durationMs)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs));
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>
        /// Shows the toast with a slide-up animation (TOST-01, TOST-04).
        /// If toast is already visible (TOST-04): only updates content — no re-animation.
        /// </summary>
        private void ShowToast(bool isBulk, string? label = null, int count = 0)
        {
            if (isBulk)
                UpdateToastContentBulk(count);
            else
                UpdateToastContent(label ?? "");

            // TOST-04: If already visible, content swap only — do not re-animate
            if (ToastBorder.Visibility == Visibility.Visible)
                return;

            // Slide up from bottom: Y from 36 to 0 over 150ms with cubic ease-out
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            ToastTranslate.Y = 36;
            ToastBorder.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From = 36,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        /// <summary>
        /// Hides the toast with a slide-down animation.
        /// Sets Visibility = Collapsed and resets Y = 36 on completion.
        /// </summary>
        private void HideToast()
        {
            if (ToastBorder.Visibility != Visibility.Visible) return;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 36,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                ToastBorder.Visibility = Visibility.Collapsed;
                ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ToastTranslate.Y = 36;
            };
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        /// <summary>
        /// Undo button click handler — restores the pending deletion.
        /// </summary>
        private void UndoToast_Click(object sender, MouseButtonEventArgs e)
        {
            _ = UndoDeleteAsync();
        }

        // ─── Window Title ───────────────────────────────────────────────────────

        /// <summary>
        /// Updates the window title based on the current desktop identity.
        /// Title format (per user decision VDSK-06):
        ///   - "JoJot — {desktop name}" when name is known and non-empty
        ///   - "JoJot — Desktop N" when name is empty but index is known (N = index + 1)
        ///   - "JoJot" in fallback mode or when no desktop info available
        /// Uses em-dash (U+2014 —) with spaces, not hyphen (-) or en-dash (U+2013).
        /// </summary>
        public void UpdateDesktopTitle(string? desktopName, int? desktopIndex)
        {
            if (!string.IsNullOrEmpty(desktopName))
            {
                Title = $"JoJot \u2014 {desktopName}";
            }
            else if (desktopIndex.HasValue)
            {
                Title = $"JoJot \u2014 Desktop {desktopIndex.Value + 1}";
            }
            else
            {
                Title = "JoJot";
            }
        }

        // ─── Window Lifecycle ───────────────────────────────────────────────────

        /// <summary>
        /// Flushes content, removes empty tabs, saves window geometry, then closes for real.
        /// Called by App on explicit shutdown (Exit menu, etc.).
        /// Commits any pending soft-delete so data is not lost on shutdown.
        /// </summary>
        public void FlushAndClose()
        {
            LogService.Info($"FlushAndClose called for desktop {_desktopGuid}");
            // Commit pending deletion before closing (fire-and-forget: DB write is fast,
            // process stays alive long enough for it to complete).
            _ = CommitPendingDeletionAsync();
            SaveCurrentTabContent();
            Close();
        }

        /// <summary>
        /// TASK-05: Window close saves content and geometry, then destroys.
        /// The process stays alive (ShutdownMode.OnExplicitShutdown).
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Save active tab content before closing
            SaveCurrentTabContent();

            // Capture geometry while the HWND is still valid
            var geo = WindowPlacementHelper.CaptureGeometry(this);

            // Fire-and-forget: save geometry to database (process stays alive so this completes)
            _ = DatabaseService.SaveWindowGeometryAsync(_desktopGuid, geo);

            LogService.Info($"Window closing for desktop {_desktopGuid} \u2014 geometry saved ({geo.Left},{geo.Top} {geo.Width}x{geo.Height} maximized={geo.IsMaximized})");

            // Do NOT set e.Cancel = true — let the window close and be destroyed
        }
    }
}
