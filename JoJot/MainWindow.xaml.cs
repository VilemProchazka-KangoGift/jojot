using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

            // Hover effect
            outerBorder.MouseEnter += (s, e) =>
            {
                if (item != TabList.SelectedItem)
                    outerBorder.Background = HoverBrush;
            };
            outerBorder.MouseLeave += (s, e) =>
            {
                if (item != TabList.SelectedItem)
                    outerBorder.Background = System.Windows.Media.Brushes.Transparent;
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

            outerBorder.Child = grid;
            item.Content = outerBorder;

            // Wire mouse events for drag-to-reorder (Phase 4 Plan 03 fills in drag logic)
            item.PreviewMouseLeftButtonDown += TabItem_PreviewMouseLeftButtonDown;
            item.PreviewMouseMove += TabItem_PreviewMouseMove;
            item.PreviewMouseLeftButtonUp += TabItem_PreviewMouseLeftButtonUp;

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
        /// Ctrl+T: new tab, Ctrl+F: focus search, Ctrl+Tab/Ctrl+Shift+Tab: cycle tabs.
        /// Additional shortcuts (Ctrl+P, Ctrl+K, F2) added in Plan 04-03.
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
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
        /// Tab list keyboard handler. F2 triggers rename (Plan 04-03).
        /// </summary>
        private void TabList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // F2 and other tab-specific keys handled in Plan 04-03
        }

        /// <summary>
        /// Double-click on tab triggers rename (Plan 04-03 fills in implementation).
        /// </summary>
        private void TabList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Rename implementation in Plan 04-03
        }

        // ─── New Tab Button ─────────────────────────────────────────────────────

        private void NewTabButton_Click(object sender, RoutedEventArgs e)
        {
            _ = CreateNewTabAsync();
        }

        // ─── Drag-to-Reorder Stubs (Plan 04-03) ────────────────────────────────

        private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Drag start tracking — Plan 04-03
        }

        private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Drag in progress — Plan 04-03
        }

        private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Drag complete — Plan 04-03
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
        /// </summary>
        public void FlushAndClose()
        {
            LogService.Info($"FlushAndClose called for desktop {_desktopGuid}");
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
