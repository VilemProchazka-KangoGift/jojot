using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private string _desktopGuid;
        private readonly ObservableCollection<NoteTab> _tabs = new();
        private NoteTab? _activeTab;
        private string _searchText = "";

        // ─── Rename state ───────────────────────────────────────────────────────
        private (ListBoxItem Item, NoteTab Tab, TextBox Box, TextBlock Label)? _activeRename;

        // ─── Drag-to-reorder state ──────────────────────────────────────────────
        private bool _isDragging;
        private bool _isCompletingDrag; // R2-DND-01: Re-entrancy guard for CompleteDrag/LostMouseCapture
        private bool _isTransferringCapture; // R3-REORDER-01: Guard for Mouse.Capture transfer during drag start

        // ─── Tab list rebuild guard (BUG-01, BUG-02) ────────────────────────────
        private bool _isRebuildingTabList;
        private System.Windows.Point _dragStartPoint;
        private ListBoxItem? _dragItem;
        private NoteTab? _dragTab;
        private int _dragInsertIndex = -1;
        private int _dragOriginalListIndex = -1; // R2-DND-02: Track original position for indicator suppression
        private Border? _dropIndicatorBorder;

        // ─── Context menu state (Phase 8) ─────────────────────────────────────────
        private Popup? _activeContextMenu;

        // ─── Confirmation dialog state (Phase 8) ──────────────────────────
        private Action? _confirmAction;
        private DateTime _hamburgerClosedAt;

        // ─── Recovery panel state (Phase 8: ORPH-02) ──────────────────────
        private bool _recoveryPanelOpen;

        // ─── Cleanup panel state (Phase 16: CLEANUP-01) ──────────────────────
        private bool _cleanupPanelOpen;

        // ─── Phase 9 state: Preferences, File Drop, Find Bar, Font Size ─────
        private bool _preferencesOpen;
        private bool _recordingHotkey;
        private int _currentFontSize = 13;
        private System.Windows.Threading.DispatcherTimer? _fontSizeTooltipTimer;
        // R2-PREF-01: _debounceInputTimer removed (autosave delay no longer user-configurable)
        private List<int> _findMatches = new();
        private int _currentFindIndex = -1;
        private bool _helpBuilt;

        // ─── Phase 10 state: Window Drag Detection ──────────────────────────────
        private bool _isDragOverlayActive;     // DRAG-08: guard against second drag
        private string? _dragFromDesktopGuid;  // Origin desktop GUID for cancel flow
        private string? _dragToDesktopGuid;    // Target desktop GUID
        private string? _dragToDesktopName;    // Target desktop name for display
        private bool _isMisplaced;            // DRAG-10: window GUID doesn't match desktop
        private int _fileDragEnterCount;       // R2-DROP-01: Enter/leave counter for reliable overlay dismiss

        // ─── Soft-delete / toast state (Phase 5) ────────────────────────────────
        private record PendingDeletion(
            List<NoteTab> Tabs,
            List<int> OriginalIndexes,
            CancellationTokenSource Cts
        );

        private PendingDeletion? _pendingDeletion;

        // ─── Autosave & Undo (Phase 6) ─────────────────────────────────────────
        private readonly AutosaveService _autosaveService = new();
        private readonly System.Windows.Threading.DispatcherTimer _checkpointTimer;
        private bool _suppressTextChanged;
        private string? _lastSaveDirectory;

        // ─── Theme-aware brush helper (Phase 7: replaces hardcoded static brushes) ──
        // Use SetResourceReference for element properties that should auto-update on theme switch.
        // Use GetBrush for one-time assignments or comparisons.
        private SolidColorBrush GetBrush(string key) =>
            (SolidColorBrush)FindResource(key);

        /// <summary>
        /// Creates a MainWindow bound to a specific virtual desktop.
        /// The desktopGuid is used for geometry save/restore and window registry keying.
        /// </summary>
        public MainWindow(string desktopGuid)
        {
            _desktopGuid = desktopGuid;

            // Phase 6: Initialize checkpoint timer before InitializeComponent
            _checkpointTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _checkpointTimer.Tick += CheckpointTimer_Tick;

            InitializeComponent();

            // Phase 6: Configure autosave service
            _autosaveService.Configure(
                contentProvider: () => _activeTab != null ? (_activeTab.Id, ContentEditor.Text) : (0L, ""),
                onSaveCompleted: (tabId) =>
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null)
                    {
                        tab.UpdatedAt = DateTime.Now;
                        UpdateTabItemDisplay(tab);
                    }
                }
            );

            // Phase 6: Wire TextChanged for autosave debounce
            ContentEditor.TextChanged += ContentEditor_TextChanged;

            // Phase 15 R2-DROP-01: Allow file drops to propagate to Window handler; suppress text drops
            ContentEditor.PreviewDragEnter += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                }
            };
            ContentEditor.PreviewDragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                }
            };

            // Handle drag cancellation when mouse capture is lost
            TabList.LostMouseCapture += (s, e) =>
            {

                // R2-DND-01: Prevent re-entrant cleanup (Mouse.Capture(null) in CompleteDrag can re-fire)
                // R3-REORDER-01: Skip when capture is being intentionally transferred to TabList
                if (_isCompletingDrag || _isTransferringCapture) return;

                if (_isDragging)
                {
                    // If mouse button is still pressed, WPF stole capture (e.g. ListBox internal handling).
                    // Re-capture to TabList and continue the drag instead of aborting.
                    if (Mouse.LeftButton == MouseButtonState.Pressed)
                    {
                        _isTransferringCapture = true;
                        try { Mouse.Capture(TabList, CaptureMode.SubTree); }
                        finally { _isTransferringCapture = false; }
                        return;
                    }
                    RemoveDropIndicator();
                    if (_dragItem?.Content is FrameworkElement abortContent) abortContent.Opacity = 1.0;
                    CompleteDrag();
                }
            };

            // TabList-level mouse move: handles drag start (fast mouse) and tracking between items
            TabList.PreviewMouseMove += (s, e) =>
            {
                if (_dragItem == null || _dragTab == null) return;
                if (e.LeftButton != MouseButtonState.Pressed) return;

                if (!_isDragging)
                {
                    // Check distance threshold for drag start
                    var current = e.GetPosition(TabList);
                    var diff = _dragStartPoint - current;
                    if (Math.Abs(diff.Y) < 5 && Math.Abs(diff.X) < 5) return;

                    StartDrag();
                }

                UpdateDropIndicator(e.GetPosition(TabList));
                e.Handled = true;
            };

            // TabList-level mouse up: ensures drag completes even if fired between items
            TabList.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging)
                {
                    e.Handled = true;
                    CompleteDrag();
                }
            };

            // Phase 7: Delete button hover — opacity 0.7 → 1.0 (TOOL-02)
            ToolbarDelete.MouseEnter += (s, e) => DeleteIconText.Opacity = 1.0;
            ToolbarDelete.MouseLeave += (s, e) => DeleteIconText.Opacity = 0.7;

            // Phase 8: Track hamburger close time for toggle behavior (MENU-01)
            HamburgerMenu.Closed += (s, e) =>
            {
                _hamburgerClosedAt = DateTime.UtcNow;
                HamburgerMenu.StaysOpen = false;
            };

            // Phase 13: Force-close hamburger menu on any click outside (WIN-02)
            PreviewMouseDown += (s, e) =>
            {
                if (HamburgerMenu.IsOpen
                    && !IsMouseOverPopup(HamburgerMenu)
                    && !IsMouseOverElement(HamburgerButton))
                {
                    HamburgerMenu.IsOpen = false;
                }
            };

            // Phase 7: Initial toolbar state — all buttons disabled until tab selected
            UpdateToolbarState();

            // Phase 10: Window drag detection (DRAG-01) and misplaced check (DRAG-10)
            VirtualDesktopService.WindowMovedToDesktop += OnWindowMovedToDesktop;
            Activated += OnWindowActivated_CheckMisplaced;
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
            // Phase 12 (TABUX-04): Restore persisted tab panel width
            await RestoreTabPanelWidthAsync();

            // R2-STARTUP-01: Silently delete empty unpinned notes before loading
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
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Name = "OuterBorder"
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: adaptive column layout based on pinned state
            // R2-TAB-01: Both buttons use 22x22 Border for adequate hit targets
            // UNPINNED: Col 0 = title (Star), Col 1 = pin icon (Auto, hidden), Col 2 = delete icon (Auto, hidden)
            // PINNED:   Col 0 = pin icon (Auto, always visible), Col 1 = title (Star), Col 2 = delete icon (Auto, hidden)
            var row0 = new Grid();
            row0.MinHeight = 22; // R2-TAB-01: Prevent vertical jitter when hover icons toggle Visible/Collapsed
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
                    // R2-TAB-01: Unpin glyph (crossed-out pin) instead of multiplication sign
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

                // R2-TAB-02: Hover color change for unpinned pin button
                pinBtn.MouseEnter += (s, e) =>
                {
                    if (_isDragging) return;
                    pinBtnIcon.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("c-accent");
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
                FontSize = 13,  // R2-FONT-02: Fixed size — tabs do NOT scale with font control
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
                FontSize = 13,  // R2-FONT-02: Fixed size to match labelBlock
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
                // R2-TAB-01: Fluent ChromeClose glyph at 12pt (bigger than previous 10pt per user request)
                Text = "\uE711",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeIcon.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");

            // R2-TAB-03: Close button hover color (red) for both pinned and unpinned
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
                HorizontalAlignment = HorizontalAlignment.Left
            };
            createdBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            row1.Children.Add(createdBlock);
            var updatedBlock = new TextBlock
            {
                Text = tab.UpdatedDisplay,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            updatedBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            row1.Children.Add(updatedBlock);
            Grid.SetRow(row1, 1);
            grid.Children.Add(row1);

            // R2-TAB-01/R2-TAB-02: Show/hide pin and close buttons on hover
            outerBorder.MouseEnter += (s, e) =>
            {
                // R2-DND-01: Suppress hover effects during drag to prevent visual artifacts
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
                // R2-DND-01: Suppress hover effects during drag to prevent visual artifacts
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

            // Right-click: show themed context menu (Phase 8: CTXM-01)
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
        /// Applies background highlight to active tab (TABS-04, TABUX-01).
        /// </summary>
        private async void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDragging && !_isCompletingDrag) return;
            if (_isRebuildingTabList) return;

            // R2-BUG-01: Save current editor content to active tab BEFORE flushing or switching
            if (_activeTab != null)
            {
                bool contentChanged = _activeTab.Content != ContentEditor.Text;
                _activeTab.Content = ContentEditor.Text;
                _activeTab.CursorPosition = ContentEditor.CaretIndex;
                if (contentChanged)
                    _activeTab.UpdatedAt = DateTime.Now;
            }

            // Remove background highlight from deselected items and hide buttons
            foreach (var removed in e.RemovedItems)
            {
                if (removed is ListBoxItem oldItem && oldItem.Content is Border oldBorder)
                {
                    oldBorder.Background = System.Windows.Media.Brushes.Transparent;

                    // R2-TAB-01: Hide pin/close buttons when deselected
                    if (oldBorder.Child is Grid oldGrid && oldGrid.Children.Count > 0 && oldGrid.Children[0] is Grid oldRow0)
                    {
                        foreach (var child in oldRow0.Children)
                        {
                            if (child is Border btn && btn.Width == 22 && btn.Height == 22)
                            {
                                // Don't hide pinned tab's always-visible pin icon
                                if (oldItem.Tag is NoteTab oldTab && oldTab.Pinned && Grid.GetColumn(btn) == 0)
                                    continue;

                                btn.Opacity = 0;
                                btn.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                }
            }

            // Phase 8.1 (EDIT-03): Flush autosave service — stops timer, saves content,
            // and updates _lastWriteCompleted to keep write frequency cap accurate
            await _autosaveService.FlushAsync();
            _checkpointTimer.Stop();

            // Phase 6: Save scroll offset for outgoing tab
            if (_activeTab != null)
            {
                var scrollViewer = GetScrollViewer(ContentEditor);
                if (scrollViewer != null)
                    _activeTab.EditorScrollOffset = (int)scrollViewer.VerticalOffset;
            }

            // Apply new selection
            if (TabList.SelectedItem is ListBoxItem newItem && newItem.Tag is NoteTab tab)
            {
                ApplyActiveHighlight(newItem);

                _activeTab = tab;

                // Phase 6: Suppress TextChanged during programmatic text assignment
                _suppressTextChanged = true;
                ContentEditor.Text = tab.Content;
                _suppressTextChanged = false;

                ContentEditor.IsEnabled = true;

                // Restore cursor position (best effort — clamp to content length)
                ContentEditor.CaretIndex = Math.Min(tab.CursorPosition, ContentEditor.Text.Length);

                // Phase 6 (EDIT-05): Restore scroll offset after layout completes
                ContentEditor.Dispatcher.BeginInvoke(() =>
                {
                    var sv = GetScrollViewer(ContentEditor);
                    sv?.ScrollToVerticalOffset(tab.EditorScrollOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Phase 6 (UNDO-07): Bind this tab's UndoStack
                UndoManager.Instance.SetActiveTab(tab.Id);
                var stack = UndoManager.Instance.GetOrCreateStack(tab.Id);
                // Push initial content as first snapshot if stack is empty (UNDO-01)
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

            // Phase 7: Update toolbar enabled states after tab selection change
            UpdateToolbarState();
        }

        /// <summary>
        /// Applies the background highlight to a selected tab item.
        /// </summary>
        private void ApplyActiveHighlight(ListBoxItem item)
        {
            if (item.Content is Border border)
            {
                border.Background = GetBrush("c-selected-bg");

                // R2-TAB-01: Show pin/close buttons on selected tab (no hover needed)
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
                    TabList.ScrollIntoView(item);
                    return;
                }
            }
        }

        // ─── Tab Panel Resize (Phase 12: TABUX-04) ────────────────────────────

        /// <summary>
        /// Restores persisted tab panel width from preferences on startup.
        /// </summary>
        private async Task RestoreTabPanelWidthAsync()
        {
            var saved = await DatabaseService.GetPreferenceAsync("tab_panel_width");
            if (saved != null && double.TryParse(saved, System.Globalization.CultureInfo.InvariantCulture, out double width))
            {
                width = Math.Clamp(width, 120, 400);
                TabPanelColumn.Width = new GridLength(width);
            }
        }

        /// <summary>
        /// Saves the tab panel width to preferences after the user finishes dragging the splitter.
        /// </summary>
        private async void TabPanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            var width = TabPanelColumn.ActualWidth;
            await DatabaseService.SetPreferenceAsync("tab_panel_width",
                width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ─── Content Save ───────────────────────────────────────────────────────

        /// <summary>
        /// Saves current content from the editor to the active tab model and database.
        /// Phase 6: Also saves scroll offset and pushes undo snapshot.
        /// </summary>
        private void SaveCurrentTabContent()
        {
            if (_activeTab == null) return;
            string currentContent = ContentEditor.Text;
            if (currentContent == _activeTab.Content) return; // No change

            _activeTab.Content = currentContent;
            _activeTab.CursorPosition = ContentEditor.CaretIndex;
            _activeTab.UpdatedAt = DateTime.Now;

            // Phase 6: Save scroll offset
            var scrollViewer = GetScrollViewer(ContentEditor);
            if (scrollViewer != null)
                _activeTab.EditorScrollOffset = (int)scrollViewer.VerticalOffset;

            _ = DatabaseService.UpdateNoteContentAsync(_activeTab.Id, currentContent);

            // Phase 6: Push undo snapshot on explicit save
            UndoManager.Instance.PushSnapshot(_activeTab.Id, currentContent);

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
            // Phase 10: Block all keyboard shortcuts while drag overlay is active (DRAG-08)
            if (_isDragOverlayActive)
            {
                e.Handled = true;
                return;
            }

            // R3-RENAME-01: Escape cancels rename — check before panels/overlays
            if (e.Key == Key.Escape && _activeRename != null)
            {
                CancelRename();
                Keyboard.Focus(ContentEditor);
                e.Handled = true;
                return;
            }

            // Phase 8: Confirmation dialog keyboard handling — intercept before all other shortcuts
            if (ConfirmationOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    HideConfirmation();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    var action = _confirmAction;
                    HideConfirmation();
                    action?.Invoke();
                    e.Handled = true;
                }
                else
                {
                    e.Handled = true; // Block all other keyboard shortcuts while dialog is open
                }
                return;
            }

            // Phase 9: Escape closes help overlay if visible
            if (e.Key == Key.Escape && HelpOverlay.Visibility == Visibility.Visible)
            {
                HideHelpOverlay();
                e.Handled = true;
                return;
            }

            // Phase 9: Escape closes editor find bar if visible
            if (e.Key == Key.Escape && EditorFindBar.Visibility == Visibility.Visible)
            {
                HideEditorFindBar();
                e.Handled = true;
                return;
            }

            // Phase 16: Escape closes cleanup panel if visible
            if (e.Key == Key.Escape && _cleanupPanelOpen)
            {
                HideCleanupPanel();
                e.Handled = true;
                return;
            }

            // R2-RECOVER-01: Escape closes recovery sidebar if visible
            if (e.Key == Key.Escape && _recoveryPanelOpen)
            {
                HideRecoveryPanel();
                e.Handled = true;
                return;
            }

            // Phase 9: Escape closes preferences panel if visible
            if (e.Key == Key.Escape && _preferencesOpen)
            {
                HidePreferencesPanel();
                e.Handled = true;
                return;
            }

            // Phase 9: Hotkey recording — capture key combination in preferences panel
            if (_recordingHotkey)
            {
                var mods = Keyboard.Modifiers;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;

                // Ignore lone modifier presses
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift ||
                    key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LWin || key == Key.RWin)
                {
                    e.Handled = true;
                    return;
                }

                // Require at least one modifier
                if (mods == ModifierKeys.None)
                {
                    e.Handled = true;
                    return;
                }

                uint win32Mods = HotkeyService.ModifierKeysToWin32(mods);
                uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

                _ = Task.Run(async () =>
                {
                    bool success = await HotkeyService.UpdateHotkeyAsync(win32Mods, vk);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _recordingHotkey = false;
                        HotkeyRecordText.Text = "Record";
                        HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();
                        if (!success)
                        {
                            ShowInfoToast("Hotkey already in use by another app");
                        }
                    });
                });

                e.Handled = true;
                return;
            }

            // Phase 6 — Ctrl+Z: Undo (UNDO-04) — MUST be first to prevent WPF native undo
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null)
                {
                    PerformUndo();
                }
                e.Handled = true; // Always handle to prevent WPF native undo
                return;
            }

            // Phase 6 — Ctrl+Y or Ctrl+Shift+Z: Redo (UNDO-04)
            if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) ||
                (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                if (_activeTab != null)
                {
                    PerformRedo();
                }
                e.Handled = true;
                return;
            }

            // Phase 6 — Ctrl+C: Enhanced copy — no selection copies entire note (EDIT-06)
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ContentEditor.SelectionLength == 0 && _activeTab != null && !string.IsNullOrEmpty(_activeTab.Content))
                {
                    try
                    {
                        Clipboard.SetText(_activeTab.Content);
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn($"Clipboard access failed: {ex.Message}");
                    }
                    e.Handled = true;
                    return;
                }
                // If there IS a selection, do NOT set e.Handled — let WPF handle normal copy
                return;
            }

            // Phase 6 — Ctrl+S: Save as TXT (EDIT-07)
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeTab != null)
                {
                    SaveAsTxt();
                }
                e.Handled = true;
                return;
            }

            // Phase 9 — Ctrl+= or Ctrl+NumAdd: Increase font size (KEYS-02)
            if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = ChangeFontSizeAsync(1);
                e.Handled = true;
                return;
            }

            // Phase 9 — Ctrl+- or Ctrl+NumSubtract: Decrease font size (KEYS-02)
            if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = ChangeFontSizeAsync(-1);
                e.Handled = true;
                return;
            }

            // Phase 9 — Ctrl+0 or Ctrl+Numpad0: Reset font size to 13pt (KEYS-02)
            if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = SetFontSizeAsync(13);
                e.Handled = true;
                return;
            }

            // Phase 9 — Ctrl+Shift+/ (Ctrl+?): Show help overlay (KEYS-04)
            if (e.Key == Key.OemQuestion && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (HelpOverlay.Visibility == Visibility.Visible)
                    HideHelpOverlay();
                else
                    ShowHelpOverlay();
                e.Handled = true;
                return;
            }

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

            // Ctrl+F: Context-dependent (TABS-11, KEYS-04)
            // If editor is focused → show in-editor find bar; otherwise → focus tab search
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ContentEditor.IsFocused)
                {
                    ShowEditorFindBar();
                }
                else
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }
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
            UpdateToolbarState(); // Phase 7: refresh pin icon
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
            // Drag start and tracking is handled by TabList.PreviewMouseMove (fires first during tunneling).
            // This handler is kept only as a fallback — the TabList handler sets e.Handled during drag.
        }

        /// <summary>
        /// Initiates the drag operation: sets state, fades the tab, captures mouse.
        /// Called from TabList.PreviewMouseMove when distance threshold is exceeded.
        /// </summary>
        private void StartDrag()
        {
            if (_isDragging || _dragItem == null || _dragTab == null) return;

            _isDragging = true;

            // R2-DND-01: Track original index for indicator suppression
            _dragOriginalListIndex = TabList.Items.IndexOf(_dragItem);

            // R3-REORDER-01: Fade original item to 50% opacity in-place (no ghost adorner)
            // Set on content Border (not ListBoxItem) to avoid WPF internal Opacity resets
            if (_dragItem.Content is FrameworkElement dragContent)
            {
                dragContent.Opacity = 0.5;
            }

            // R2-DND-01: SubTree mode keeps events routing to children within TabList
            // R3-REORDER-01: Guard prevents LostMouseCapture from aborting drag during transfer
            _isTransferringCapture = true;
            try
            {
                Mouse.Capture(TabList, CaptureMode.SubTree);
            }
            finally
            {
                _isTransferringCapture = false;
            }
        }

        private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) e.Handled = true;
            CompleteDrag();
        }

        /// <summary>
        /// Updates the drop indicator position during drag.
        /// Enforces zone boundaries: pinned tabs stay in pinned zone, unpinned in unpinned.
        /// Shows a horizontal line at the drop target position.
        /// </summary>
        private void UpdateDropIndicator(System.Windows.Point mousePos)
        {
            RemoveDropIndicator();

            _dragInsertIndex = -1;
            double bestDistance = double.MaxValue;
            int lastSameZoneIndex = -1;

            for (int i = 0; i < TabList.Items.Count; i++)
            {
                if (TabList.Items[i] is not ListBoxItem candidate || candidate.Tag is not NoteTab candidateTab)
                    continue;

                // Zone enforcement: only allow drop between same-zone items
                if (candidateTab.Pinned != _dragTab!.Pinned) continue;

                lastSameZoneIndex = i;

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

            if (_dragInsertIndex < 0) return;

            // R2-DND-02: Suppress indicator at positions that wouldn't change the order
            // Inserting at the original index or the index after it leaves the item in place
            if (_dragOriginalListIndex >= 0 &&
                (_dragInsertIndex == _dragOriginalListIndex || _dragInsertIndex == _dragOriginalListIndex + 1))
            {
                _dragInsertIndex = -1;
                return;
            }

            // Show horizontal-only line at the drop target position
            if (_dragInsertIndex < TabList.Items.Count)
            {
                // R2-DND-01: Handle separator items by scanning to nearest Border item
                if (TabList.Items[_dragInsertIndex] is ListBoxItem targetItem && targetItem.Content is Border targetBorder)
                {
                    targetBorder.BorderThickness = new Thickness(0, 2, 0, 0);
                    targetBorder.BorderBrush = GetBrush("c-accent");
                    _dropIndicatorBorder = targetBorder;
                }
                else
                {
                    // Separator or non-Border: look forward for next real tab item
                    for (int j = _dragInsertIndex + 1; j < TabList.Items.Count; j++)
                    {
                        if (TabList.Items[j] is ListBoxItem nextItem && nextItem.Content is Border nextBorder)
                        {
                            nextBorder.BorderThickness = new Thickness(0, 2, 0, 0);
                            nextBorder.BorderBrush = GetBrush("c-accent");
                            _dropIndicatorBorder = nextBorder;
                            break;
                        }
                    }

                    // If no forward item found, look backward
                    if (_dropIndicatorBorder == null)
                    {
                        for (int j = _dragInsertIndex - 1; j >= 0; j--)
                        {
                            if (TabList.Items[j] is ListBoxItem prevItem && prevItem.Content is Border prevBorder)
                            {
                                prevBorder.BorderThickness = new Thickness(0, 0, 0, 2);
                                prevBorder.BorderBrush = GetBrush("c-accent");
                                _dropIndicatorBorder = prevBorder;
                                break;
                            }
                        }
                    }
                }
            }
            else if (lastSameZoneIndex >= 0)
            {
                // Inserting after the last item -- show bottom border on the last same-zone item
                if (TabList.Items[lastSameZoneIndex] is ListBoxItem lastItem && lastItem.Content is Border lastBorder)
                {
                    lastBorder.BorderThickness = new Thickness(0, 0, 0, 2);
                    lastBorder.BorderBrush = GetBrush("c-accent");
                    _dropIndicatorBorder = lastBorder;
                }
            }
        }

        /// <summary>
        /// Completes the drag operation: moves the tab in the collection, updates sort orders.
        /// </summary>
        private void CompleteDrag()
        {
            if (!_isDragging) { ResetDragState(); return; }

            // R2-DND-01: Re-entrancy guard — Mouse.Capture(null) can re-fire LostMouseCapture
            _isCompletingDrag = true;
            try
            {
                Mouse.Capture(null);
                RemoveDropIndicator();

                // Restore old item opacity (no-move path)
                if (_dragItem?.Content is FrameworkElement oldContent) oldContent.Opacity = 1.0;

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

                        // R3-REORDER-01: Fade-in the moved tab at its new position (150ms)
                        // Animate on content Border to avoid WPF ListBoxItem Opacity interference
                        if (_dragTab != null)
                        {
                            foreach (var obj in TabList.Items)
                            {
                                if (obj is ListBoxItem item && item.Tag == _dragTab
                                    && item.Content is FrameworkElement content)
                                {
                                    // Set initial opacity before animation to prevent flash of full opacity
                                    content.Opacity = 0.5;
                                    var fadeIn = new DoubleAnimation
                                    {
                                        From = 0.5, To = 1.0,
                                        Duration = TimeSpan.FromMilliseconds(200),
                                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                                    };
                                    fadeIn.Completed += (_, _) => { content.Opacity = 1.0; content.BeginAnimation(UIElement.OpacityProperty, null); };
                                    content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                                    break;
                                }
                            }
                        }
                    }
                }

                ResetDragState();
            }
            finally
            {
                _isCompletingDrag = false;
            }
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
                _dropIndicatorBorder.BorderThickness = new Thickness(0);
                _dropIndicatorBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                _dropIndicatorBorder = null;
            }
        }

        private void ResetDragState()
        {
            _isDragging = false;
            if (_dragItem?.Content is FrameworkElement resetContent) resetContent.Opacity = 1.0;
            _dragItem = null;
            _dragTab = null;
            _dragInsertIndex = -1;
            _dragOriginalListIndex = -1;
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
            {
                await DatabaseService.DeleteNoteAsync(tab.Id);
                // Phase 6: Remove undo stack on permanent deletion
                UndoManager.Instance.RemoveStack(tab.Id);
            }
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
        /// R2-TAB-01: Collapses an element after a short delay (allows fade-out animation to complete).
        /// </summary>
        private static void DelayedCollapse(UIElement element)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (element.Opacity == 0)
                    element.Visibility = Visibility.Collapsed;
            };
            timer.Start();
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

            // Phase 10: Unsubscribe from drag detection to prevent events firing during close
            VirtualDesktopService.WindowMovedToDesktop -= OnWindowMovedToDesktop;

            // Phase 6 (EDIT-04): Stop autosave and flush synchronously
            _autosaveService.Stop();
            _checkpointTimer.Stop();

            // Phase 10.1: Commit pending deletion synchronously before close (audit finding #3)
            CommitPendingDeletionAsync().GetAwaiter().GetResult();
            SaveCurrentTabContent();
            Close();
        }

        /// <summary>
        /// TASK-05 + EDIT-04: Window close saves content and geometry, then destroys.
        /// Phase 6: Synchronous flush — no data loss on close.
        /// The process stays alive (ShutdownMode.OnExplicitShutdown).
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Phase 6 (EDIT-04): Stop autosave timer and flush synchronously
            _autosaveService.Stop();
            _checkpointTimer.Stop();

            // Synchronous flush — block until save completes (no data loss)
            if (_activeTab != null)
            {
                string content = ContentEditor.Text;
                if (content != _activeTab.Content)
                {
                    _activeTab.Content = content;
                    _activeTab.CursorPosition = ContentEditor.CaretIndex;
                    _activeTab.UpdatedAt = DateTime.Now;
                    DatabaseService.UpdateNoteContentAsync(_activeTab.Id, content)
                        .GetAwaiter().GetResult(); // Sync flush on close — no data loss
                }
            }

            // Phase 10.1: Commit pending deletions before close (audit finding #1)
            CommitPendingDeletionAsync().GetAwaiter().GetResult();

            // Capture geometry while the HWND is still valid
            var geo = WindowPlacementHelper.CaptureGeometry(this);

            // Fire-and-forget: save geometry to database (process stays alive so this completes)
            _ = DatabaseService.SaveWindowGeometryAsync(_desktopGuid, geo);

            LogService.Info($"Window closing for desktop {_desktopGuid} \u2014 geometry saved ({geo.Left},{geo.Top} {geo.Width}x{geo.Height} maximized={geo.IsMaximized})");

            // Do NOT set e.Cancel = true — let the window close and be destroyed
        }

        // ─── Phase 6: Autosave & Undo Helpers ────────────────────────────────────

        /// <summary>
        /// Phase 6: TextChanged handler for autosave debounce trigger.
        /// Only fires for user-initiated changes (suppressed during programmatic text assignment).
        /// </summary>
        private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged || _activeTab == null) return;
            _autosaveService.NotifyTextChanged();

            // Start/reset checkpoint timer on user input
            _checkpointTimer.Stop();
            _checkpointTimer.Start();
        }

        /// <summary>
        /// Phase 6: 5-minute checkpoint timer tick. Creates a tier-2 checkpoint if content
        /// has changed since the last checkpoint (UNDO-03).
        /// </summary>
        private void CheckpointTimer_Tick(object? sender, EventArgs e)
        {
            _checkpointTimer.Stop();
            if (_activeTab == null) return;

            var stack = UndoManager.Instance.GetStack(_activeTab.Id);
            if (stack != null && stack.ShouldCreateCheckpoint())
            {
                string content = ContentEditor.Text;
                stack.PushCheckpoint(content);
            }

            // Restart for next 5-minute interval
            _checkpointTimer.Start();
        }

        /// <summary>
        /// UNDO-04: Performs undo by restoring previous snapshot from the per-tab UndoStack.
        /// Sets _suppressTextChanged to prevent the text assignment from triggering autosave.
        /// </summary>
        private void PerformUndo()
        {
            if (_activeTab == null) return;
            var content = UndoManager.Instance.Undo(_activeTab.Id);
            if (content == null) return;

            _suppressTextChanged = true;
            _activeTab.Content = content;
            ContentEditor.Text = content;
            ContentEditor.CaretIndex = Math.Min(_activeTab.CursorPosition, content.Length);
            _suppressTextChanged = false;

            UpdateTabItemDisplay(_activeTab);
            UpdateToolbarState(); // Phase 7: refresh undo/redo button states
        }

        /// <summary>
        /// UNDO-04: Performs redo by advancing to next snapshot in the per-tab UndoStack.
        /// </summary>
        private void PerformRedo()
        {
            if (_activeTab == null) return;
            var content = UndoManager.Instance.Redo(_activeTab.Id);
            if (content == null) return;

            _suppressTextChanged = true;
            _activeTab.Content = content;
            ContentEditor.Text = content;
            ContentEditor.CaretIndex = Math.Min(_activeTab.CursorPosition, content.Length);
            _suppressTextChanged = false;

            UpdateTabItemDisplay(_activeTab);
            UpdateToolbarState(); // Phase 7: refresh undo/redo button states
        }

        /// <summary>
        /// EDIT-07: Opens a Save As dialog for exporting the active note as UTF-8 TXT with BOM.
        /// Remembers the last save directory within the session (resets on app launch).
        /// </summary>
        private void SaveAsTxt()
        {
            if (_activeTab == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = GetDefaultFilename(_activeTab)
            };

            if (!string.IsNullOrEmpty(_lastSaveDirectory))
                dialog.InitialDirectory = _lastSaveDirectory;

            if (dialog.ShowDialog(this) == true)
            {
                var utf8Bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                System.IO.File.WriteAllText(dialog.FileName, _activeTab.Content, utf8Bom);
                _lastSaveDirectory = System.IO.Path.GetDirectoryName(dialog.FileName);
            }
        }

        /// <summary>
        /// Generates a default filename for the Save As dialog.
        /// Priority: tab name, first 30 chars of content, "JoJot note YYYY-MM-DD".
        /// </summary>
        private static string GetDefaultFilename(NoteTab tab)
        {
            if (!string.IsNullOrWhiteSpace(tab.Name))
                return SanitizeFilename(tab.Name) + ".txt";

            if (!string.IsNullOrWhiteSpace(tab.Content))
            {
                string preview = tab.Content.Trim();
                if (preview.Length > 30)
                    preview = preview[..30];
                return SanitizeFilename(preview) + ".txt";
            }

            return $"JoJot note {DateTime.Now:yyyy-MM-dd}.txt";
        }

        /// <summary>
        /// Removes characters that are illegal in Windows filenames.
        /// </summary>
        private static string SanitizeFilename(string name)
        {
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var sanitized = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0)
                    sanitized.Append(c);
                else
                    sanitized.Append('_');
            }
            // Trim trailing dots and spaces (Windows doesn't allow them in filenames)
            string result = sanitized.ToString().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(result) ? "JoJot note" : result;
        }

        /// <summary>
        /// Finds the ScrollViewer inside a TextBox visual tree.
        /// Used for saving/restoring scroll position on tab switch (EDIT-05).
        /// </summary>
        private static ScrollViewer? GetScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv) return sv;
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        // ─── Toolbar (Phase 7: TOOL-01, TOOL-02, TOOL-03) ───────────────────────

        private void ToolbarUndo_Click(object sender, RoutedEventArgs e) => PerformUndo();
        private void ToolbarRedo_Click(object sender, RoutedEventArgs e) => PerformRedo();

        private void ToolbarPin_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab != null)
                _ = TogglePinAsync(_activeTab);
        }

        private void ToolbarClone_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab != null)
                _ = CloneTabAsync(_activeTab);
        }

        private void ToolbarCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab == null) return;

            try
            {
                if (ContentEditor.SelectionLength > 0)
                    Clipboard.SetText(ContentEditor.SelectedText);
                else if (!string.IsNullOrEmpty(_activeTab.Content))
                    Clipboard.SetText(_activeTab.Content);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Clipboard access failed: {ex.Message}");
            }
        }

        private void ToolbarPaste_Click(object sender, RoutedEventArgs e)
        {
            ContentEditor.Focus();
            ApplicationCommands.Paste.Execute(null, ContentEditor);
        }

        private void ToolbarSave_Click(object sender, RoutedEventArgs e) => SaveAsTxt();

        private void ToolbarDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab != null)
                _ = DeleteTabAsync(_activeTab);
        }

        /// <summary>
        /// Updates toolbar button enabled states and pin icon based on active tab.
        /// Called from: tab selection change, undo/redo, pin toggle, text change.
        /// </summary>
        private void UpdateToolbarState()
        {
            bool hasTab = _activeTab != null;

            ToolbarUndo.IsEnabled = hasTab && UndoManager.Instance.CanUndo(_activeTab!.Id);
            ToolbarRedo.IsEnabled = hasTab && UndoManager.Instance.CanRedo(_activeTab!.Id);
            ToolbarPin.IsEnabled = hasTab;
            ToolbarClone.IsEnabled = hasTab;
            ToolbarCopy.IsEnabled = hasTab;
            ToolbarPaste.IsEnabled = hasTab;
            ToolbarSave.IsEnabled = hasTab;
            ToolbarDelete.IsEnabled = hasTab;

            // Update pin icon: show Unpin when tab is already pinned
            if (hasTab && _activeTab!.Pinned)
            {
                PinIconText.Text = "\uE77A"; // Unpin
                ToolbarPin.ToolTip = "Unpin (Ctrl+P)";
            }
            else
            {
                PinIconText.Text = "\uE718"; // Pin
                ToolbarPin.ToolTip = "Pin (Ctrl+P)";
            }
        }

        // ─── Hamburger Menu (Phase 8: MENU-01) ──────────────────────────────────

        /// <summary>
        /// Generic hover handler for themed menu item Borders (hover background highlight).
        /// </summary>
        private void MenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b) b.Background = GetBrush("c-hover-bg");
        }

        /// <summary>
        /// Checks if the mouse is currently over any visual within a Popup.
        /// Used for hamburger menu dismiss detection (WIN-02).
        /// </summary>
        private static bool IsMouseOverPopup(Popup popup)
        {
            return popup.Child is FrameworkElement child && child.IsMouseOver;
        }

        private static bool IsMouseOverElement(UIElement element)
        {
            return element.IsMouseOver;
        }

        private void MenuItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            // StaysOpen=False closes the popup before Click fires, so the toggle
            // always sees IsOpen=false. Detect this by checking if it just closed.
            if ((DateTime.UtcNow - _hamburgerClosedAt).TotalMilliseconds < 300)
                return;
            HamburgerMenu.IsOpen = !HamburgerMenu.IsOpen;
        }

        /// <summary>
        /// Phase 16: "Clean up tabs" menu click — opens the cleanup side panel.
        /// </summary>
        private void MenuCleanup_Click(object sender, MouseButtonEventArgs e)
        {
            HamburgerMenu.IsOpen = false;
            ShowCleanupPanel();
        }

        /// <summary>
        /// Recover sessions — opens the orphan recovery flyout panel (ORPH-02, MENU-02).
        /// </summary>
        private void MenuRecover_Click(object sender, MouseButtonEventArgs e)
        {
            HamburgerMenu.IsOpen = false;
            ShowRecoveryPanel();
        }

        // ─── Recovery Flyout Panel (Phase 8: ORPH-02, ORPH-03, ORPH-04) ────────────

        /// <summary>
        /// Opens the recovery flyout panel and populates it with orphaned session cards.
        /// Toggles closed if already open.
        /// </summary>
        private async void ShowRecoveryPanel()
        {
            if (_recoveryPanelOpen)
            {
                HideRecoveryPanel();
                return;
            }

            // R2-RECOVER-01: One-panel-at-a-time — close preferences if open
            if (_preferencesOpen) HidePreferencesPanel();
            if (_cleanupPanelOpen) HideCleanupPanel();

            var orphanGuids = VirtualDesktopService.OrphanedSessionGuids;
            if (orphanGuids.Count == 0)
                return;

            var orphanInfos = await DatabaseService.GetOrphanedSessionInfoAsync(orphanGuids);
            RecoverySessionList.Children.Clear();

            var orphanList = orphanInfos.ToList();
            for (int i = 0; i < orphanList.Count; i++)
            {
                var (guid, desktopName, tabCount, lastUpdated) = orphanList[i];
                // R3-RECOVER-01: Get tab previews (name + content excerpt)
                var tabPreviews = await DatabaseService.GetNotePreviewsForDesktopAsync(guid, 5);
                var totalCount = await DatabaseService.GetNoteCountForDesktopAsync(guid);
                bool isLast = (i == orphanList.Count - 1);
                RecoverySessionList.Children.Add(CreateRecoveryRow(guid, desktopName, tabCount, lastUpdated, tabPreviews, totalCount, isLast));
            }

            if (RecoverySessionList.Children.Count == 0) return;

            _recoveryPanelOpen = true;
            RecoveryPanel.Visibility = Visibility.Visible;

            // R2-RECOVER-01: Slide in from right (matching preferences animation)
            var anim = new DoubleAnimation
            {
                From = 320, To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            RecoveryPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void HideRecoveryPanel()
        {
            if (!_recoveryPanelOpen) return;
            _recoveryPanelOpen = false;

            var anim = new DoubleAnimation
            {
                From = 0, To = 320,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                RecoveryPanel.Visibility = Visibility.Collapsed;
                RecoveryPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                RecoveryPanelTransform.X = 320;
            };
            RecoveryPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void RecoveryClose_Click(object sender, MouseButtonEventArgs e)
        {
            HideRecoveryPanel();
        }

        /// <summary>
        /// R3-RECOVER-01: Creates a flat row for an orphaned session in the recovery panel.
        /// Shows desktop name (bold), tab count + date (muted), individual tab previews
        /// (name + excerpt), "+N more" if excess, and Adopt/Delete buttons.
        /// </summary>
        private FrameworkElement CreateRecoveryRow(string guid, string? desktopName, int tabCount,
            DateTime lastUpdated, List<(string? Name, string Excerpt)> tabPreviews, int totalNoteCount, bool isLast)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 10)
            };

            // Registry fallback for orphaned desktop names not stored in DB
            if (string.IsNullOrEmpty(desktopName) && Guid.TryParse(guid, out var desktopGuid))
            {
                var regName = Interop.VirtualDesktopInterop.GetDesktopNameFromRegistry(desktopGuid);
                if (!string.IsNullOrEmpty(regName))
                    desktopName = regName;
            }

            var finalName = string.IsNullOrEmpty(desktopName) ? "Unknown desktop" : desktopName;

            // Desktop name (bold, primary color)
            var nameBlock = new TextBlock
            {
                Text = finalName,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
            container.Children.Add(nameBlock);

            // Metadata row (tab count + date, muted)
            var metaBlock = new TextBlock
            {
                Text = $"{tabCount} tab{(tabCount == 1 ? "" : "s")} \u00B7 {lastUpdated:MMM d, yyyy}",
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 6)
            };
            metaBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            container.Children.Add(metaBlock);

            // Tab preview lines (one per tab, indented)
            foreach (var (name, excerpt) in tabPreviews)
            {
                string displayExcerpt = excerpt.Length > 50 ? excerpt[..50] + "..." : excerpt;
                // Replace newlines with spaces for single-line display
                displayExcerpt = displayExcerpt.Replace('\n', ' ').Replace('\r', ' ');

                var lineBlock = new TextBlock
                {
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 1)
                };

                if (name != null)
                {
                    lineBlock.Inlines.Add(new System.Windows.Documents.Run(name)
                    {
                        FontWeight = FontWeights.Normal
                    });
                    if (!string.IsNullOrEmpty(displayExcerpt))
                    {
                        var dashRun = new System.Windows.Documents.Run($" \u2014 {displayExcerpt}")
                        {
                            FontStyle = FontStyles.Italic
                        };
                        dashRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
                        lineBlock.Inlines.Add(dashRun);
                    }
                }
                else if (!string.IsNullOrEmpty(displayExcerpt))
                {
                    lineBlock.Text = displayExcerpt;
                }
                else
                {
                    lineBlock.Text = "Empty note";
                    lineBlock.FontStyle = FontStyles.Italic;
                }

                lineBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
                container.Children.Add(lineBlock);
            }

            // "+N more" line (if totalNoteCount > tabPreviews.Count)
            int remaining = totalNoteCount - tabPreviews.Count;
            if (remaining > 0)
            {
                var moreBlock = new TextBlock
                {
                    Text = $"+{remaining} more",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                moreBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
                container.Children.Add(moreBlock);
            }

            // Button row (Adopt left, Delete right)
            var buttonPanel = new DockPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                LastChildFill = false
            };

            Button CreateRowButton(string text, bool isDestructive = false)
            {
                var btn = new Button
                {
                    Content = text,
                    FontSize = 11,
                    MinWidth = 45,
                    Height = 24,
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(8, 2, 8, 2),
                    BorderThickness = new Thickness(1)
                };
                btn.SetResourceReference(Button.BorderBrushProperty, "c-border");
                if (isDestructive)
                {
                    btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
                    btn.Foreground = System.Windows.Media.Brushes.White;
                    btn.BorderThickness = new Thickness(0);
                }
                else
                {
                    btn.Background = System.Windows.Media.Brushes.Transparent;
                    btn.SetResourceReference(Button.ForegroundProperty, "c-text-primary");
                }
                return btn;
            }

            // Adopt — merge tabs into current desktop
            var adoptBtn = CreateRowButton("Adopt");
            DockPanel.SetDock(adoptBtn, Dock.Left);
            adoptBtn.Click += async (s, e) =>
            {
                await DatabaseService.MigrateTabsAsync(guid, _desktopGuid);
                await DatabaseService.DeleteSessionAndNotesAsync(guid);
                RemoveOrphanGuid(guid);
                await RefreshAfterOrphanAction();
            };
            buttonPanel.Children.Add(adoptBtn);

            // Delete — permanently delete session and all its notes
            var deleteBtn = CreateRowButton("Delete", isDestructive: true);
            DockPanel.SetDock(deleteBtn, Dock.Right);
            deleteBtn.Click += async (s, e) =>
            {
                await DatabaseService.DeleteSessionAndNotesAsync(guid);
                RemoveOrphanGuid(guid);
                await RefreshAfterOrphanAction();
            };
            buttonPanel.Children.Add(deleteBtn);

            container.Children.Add(buttonPanel);

            // Wrap row + optional divider in outer container
            if (!isLast)
            {
                var wrapper = new StackPanel();
                wrapper.Children.Add(container);
                var divider = new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                divider.SetResourceReference(Border.BackgroundProperty, "c-border");
                wrapper.Children.Add(divider);
                return wrapper;
            }

            return container;
        }

        /// <summary>
        /// Removes a GUID from the orphaned session list after a recovery action.
        /// </summary>
        private void RemoveOrphanGuid(string guid)
        {
            var list = VirtualDesktopService.OrphanedSessionGuids.ToList();
            list.Remove(guid);
            VirtualDesktopService.SetOrphanedSessionGuids(list);
        }

        /// <summary>
        /// Refreshes recovery panel and badge after an orphan action.
        /// Closes the panel if no orphans remain. Always reloads tabs.
        /// </summary>
        private async Task RefreshAfterOrphanAction()
        {
            UpdateOrphanBadge();

            if (VirtualDesktopService.OrphanedSessionGuids.Count == 0)
            {
                HideRecoveryPanel();
                await LoadTabsAsync();
                return;
            }

            // Refresh rows for remaining orphans
            var orphanInfos = await DatabaseService.GetOrphanedSessionInfoAsync(
                VirtualDesktopService.OrphanedSessionGuids);
            RecoverySessionList.Children.Clear();
            var orphanList = orphanInfos.ToList();
            for (int i = 0; i < orphanList.Count; i++)
            {
                var (guid, name, tabCount, lastUpdated) = orphanList[i];
                var tabPreviews = await DatabaseService.GetNotePreviewsForDesktopAsync(guid, 5);
                var totalCount = await DatabaseService.GetNoteCountForDesktopAsync(guid);
                bool isLast = (i == orphanList.Count - 1);
                RecoverySessionList.Children.Add(CreateRecoveryRow(guid, name, tabCount, lastUpdated, tabPreviews, totalCount, isLast));
            }

            // Reload tabs in case Adopt added new tabs to this desktop
            await LoadTabsAsync();
        }

        /// <summary>
        /// Updates the hamburger badge dot and "Recover sessions" color based on orphan count (ORPH-04).
        /// Badge dot (7px, accent-colored) appears when orphans exist; disappears when all resolved.
        /// </summary>
        public void UpdateOrphanBadge()
        {
            bool hasOrphans = VirtualDesktopService.OrphanedSessionGuids.Count > 0;
            OrphanBadge.Visibility = hasOrphans ? Visibility.Visible : Visibility.Collapsed;
            MenuRecover.Visibility = hasOrphans ? Visibility.Visible : Visibility.Collapsed; // R2-MENU-01: Hide entire menu item
            MenuRecoverText.SetResourceReference(TextBlock.ForegroundProperty,
                hasOrphans ? "c-accent" : "c-text-primary");
        }

        // ─── Cleanup Panel (Phase 16: CLEANUP-01 through CLEANUP-06) ────────────

        /// <summary>
        /// Opens the cleanup side panel and populates it with the default filter.
        /// Toggles closed if already open.
        /// </summary>
        private void ShowCleanupPanel()
        {
            if (_cleanupPanelOpen)
            {
                HideCleanupPanel();
                return;
            }

            // One-panel-at-a-time
            if (_preferencesOpen) HidePreferencesPanel();
            if (_recoveryPanelOpen) HideRecoveryPanel();

            // Reset filter to defaults
            CleanupAgeInput.Text = "7";
            CleanupUnitCombo.SelectedIndex = 0; // "days"
            CleanupIncludePinned.IsChecked = false;

            _cleanupPanelOpen = true;
            CleanupPanel.Visibility = Visibility.Visible;

            // Populate preview list with default filter
            RefreshCleanupPreview();

            var anim = new DoubleAnimation
            {
                From = 320, To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void HideCleanupPanel()
        {
            if (!_cleanupPanelOpen) return;
            _cleanupPanelOpen = false;

            var anim = new DoubleAnimation
            {
                From = 0, To = 320,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                CleanupPanel.Visibility = Visibility.Collapsed;
                CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                CleanupPanelTransform.X = 320;
            };
            CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void CleanupClose_Click(object sender, MouseButtonEventArgs e)
        {
            HideCleanupPanel();
        }

        private void CleanupDelete_Click(object sender, MouseButtonEventArgs e)
        {
            // Implemented in plan 02
        }

        private void CleanupAgeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cleanupPanelOpen) RefreshCleanupPreview();
        }

        private void CleanupUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cleanupPanelOpen) RefreshCleanupPreview();
        }

        private void CleanupIncludePinned_Changed(object sender, RoutedEventArgs e)
        {
            if (_cleanupPanelOpen) RefreshCleanupPreview();
        }

        /// <summary>
        /// Parses the age input and unit to compute a cutoff DateTime.
        /// Returns null if the input is invalid.
        /// </summary>
        private DateTime? GetCleanupCutoffDate()
        {
            if (!int.TryParse(CleanupAgeInput.Text, out int age) || age < 1)
                return null;

            var unit = CleanupUnitCombo.SelectedIndex switch
            {
                0 => TimeSpan.FromDays(age),        // days
                1 => TimeSpan.FromHours(age),       // hours
                2 => TimeSpan.FromDays(age * 7),    // weeks
                3 => TimeSpan.FromDays(age * 30),   // months (approximate)
                _ => TimeSpan.FromDays(age)
            };

            return DateTime.Now - unit;
        }

        /// <summary>
        /// Returns tabs matching the current cleanup filter, in tab panel order.
        /// </summary>
        private List<NoteTab> GetCleanupCandidates()
        {
            var cutoff = GetCleanupCutoffDate();
            if (cutoff == null) return new List<NoteTab>();

            bool includePinned = CleanupIncludePinned.IsChecked == true;

            return _tabs
                .Where(t => t.UpdatedAt < cutoff.Value && (includePinned || !t.Pinned))
                .ToList();
        }

        /// <summary>
        /// Rebuilds the cleanup preview list UI based on current filter criteria.
        /// Called from ShowCleanupPanel and all filter change handlers.
        /// </summary>
        private void RefreshCleanupPreview()
        {
            CleanupPreviewList.Children.Clear();

            var candidates = GetCleanupCandidates();

            // Update delete button text and enabled state
            if (candidates.Count > 0)
            {
                CleanupDeleteText.Text = $"Delete {candidates.Count} tab{(candidates.Count == 1 ? "" : "s")}";
                CleanupDeleteButton.IsEnabled = true;
                CleanupDeleteButton.Opacity = 1.0;
            }
            else
            {
                CleanupDeleteText.Text = "Delete 0 tabs";
                CleanupDeleteButton.IsEnabled = false;
                CleanupDeleteButton.Opacity = 0.5;
            }

            // Empty state
            if (candidates.Count == 0)
            {
                var emptyBlock = new TextBlock
                {
                    Text = "No tabs match this filter",
                    FontSize = 13,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
                CleanupPreviewList.Children.Add(emptyBlock);
                return;
            }

            // Build preview rows
            for (int i = 0; i < candidates.Count; i++)
            {
                var tab = candidates[i];
                bool isLast = (i == candidates.Count - 1);
                CleanupPreviewList.Children.Add(CreateCleanupPreviewRow(tab, isLast));
            }
        }

        /// <summary>
        /// Creates a single cleanup preview row showing tab title, content excerpt, and relative age.
        /// </summary>
        private FrameworkElement CreateCleanupPreviewRow(NoteTab tab, bool isLast)
        {
            var container = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };

            // Title line with optional pin icon
            var titleBlock = new TextBlock
            {
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Pin icon prefix for pinned tabs
            if (tab.Pinned)
            {
                var pinRun = new System.Windows.Documents.Run("\uE718 ")
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 10
                };
                pinRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
                titleBlock.Inlines.Add(pinRun);
            }

            // Tab name
            string displayName = tab.DisplayLabel;
            titleBlock.Inlines.Add(new System.Windows.Documents.Run(displayName)
            {
                FontWeight = FontWeights.Normal
            });

            // Content excerpt suffix (em-dash + italic)
            string excerpt = GetCleanupExcerpt(tab);
            if (!string.IsNullOrEmpty(excerpt))
            {
                var excerptRun = new System.Windows.Documents.Run($" \u2014 {excerpt}")
                {
                    FontStyle = FontStyles.Italic
                };
                excerptRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
                titleBlock.Inlines.Add(excerptRun);
            }

            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
            container.Children.Add(titleBlock);

            // Relative age line (muted, smaller)
            var ageBlock = new TextBlock
            {
                Text = FormatRelativeAge(tab.UpdatedAt),
                FontSize = 11
            };
            ageBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            container.Children.Add(ageBlock);

            // Divider (unless last item)
            if (!isLast)
            {
                var wrapper = new StackPanel();
                wrapper.Children.Add(container);
                var divider = new Separator
                {
                    Margin = new Thickness(0, 2, 0, 0)
                };
                divider.SetResourceReference(Separator.BackgroundProperty, "c-border");
                wrapper.Children.Add(divider);
                return wrapper;
            }

            return container;
        }

        /// <summary>
        /// Extracts ~50 char content excerpt for display in cleanup preview rows.
        /// </summary>
        private static string GetCleanupExcerpt(NoteTab tab)
        {
            if (string.IsNullOrWhiteSpace(tab.Content))
                return "";

            string content = tab.Content.Trim().Replace('\n', ' ').Replace('\r', ' ');

            // If tab has a custom name, show content excerpt
            if (!string.IsNullOrWhiteSpace(tab.Name))
            {
                return content.Length > 50 ? content[..50] + "..." : content;
            }

            // If no custom name (DisplayLabel shows first 30 chars of content),
            // don't repeat it — return empty since title already shows content
            return "";
        }

        /// <summary>
        /// Formats UpdatedAt as relative age (e.g., "3 days ago", "2 hours ago").
        /// </summary>
        private static string FormatRelativeAge(DateTime updatedAt)
        {
            var diff = DateTime.Now - updatedAt;

            if (diff.TotalMinutes < 60)
                return $"{Math.Max(1, (int)diff.TotalMinutes)} min ago";

            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours == 1 ? "" : "s")} ago";

            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} day{((int)diff.TotalDays == 1 ? "" : "s")} ago";

            if (diff.TotalDays < 30)
            {
                int weeks = (int)(diff.TotalDays / 7);
                return $"{weeks} week{(weeks == 1 ? "" : "s")} ago";
            }

            if (diff.TotalDays < 365)
            {
                int months = (int)(diff.TotalDays / 30);
                return $"{months} month{(months == 1 ? "" : "s")} ago";
            }

            return updatedAt.ToString("MMM d, yyyy");
        }

        /// <summary>
        /// Exit — flush all windows and terminate (PROC-06).
        /// Uses Dispatcher.BeginInvoke so the menu closes before shutdown begins.
        /// </summary>
        private void MenuExit_Click(object sender, MouseButtonEventArgs e)
        {
            HamburgerMenu.IsOpen = false;
            if (Application.Current is App app)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ExitApplication(app);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private static void ExitApplication(App app)
        {
            var windows = app.GetAllWindows();
            foreach (var window in windows)
            {
                window.FlushAndClose();
            }
            Environment.Exit(0);
        }

        // ─── Confirmation Overlay (Phase 8: Plan 02 — MENU-03, MENU-04, MENU-05) ──

        /// <summary>
        /// Shows the confirmation overlay with a title and message.
        /// The onConfirm action is called when the user clicks Delete.
        /// </summary>
        private void ShowConfirmation(string title, string message, Action? onConfirm)
        {
            ConfirmTitle.Text = title;
            ConfirmMessage.Text = message;
            _confirmAction = onConfirm;
            ConfirmDeleteButton.Visibility = onConfirm != null ? Visibility.Visible : Visibility.Collapsed;
            ConfirmCancelButton.Content = onConfirm != null ? "Cancel" : "OK";
            ConfirmationOverlay.Visibility = Visibility.Visible;
            ConfirmCancelButton.Focus();
        }

        private void HideConfirmation()
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmAction = null;
        }

        /// <summary>
        /// Backdrop click dismisses the confirmation overlay without deleting.
        /// </summary>
        private void ConfirmOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
        {
            HideConfirmation();
        }

        /// <summary>
        /// Cancel button hides the confirmation overlay without deleting.
        /// </summary>
        private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            HideConfirmation();
        }

        /// <summary>
        /// Delete button executes the confirmed bulk delete action.
        /// </summary>
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            var action = _confirmAction;
            HideConfirmation();
            action?.Invoke();
        }

        // ─── Tab Context Menu (Phase 8: CTXM-01, CTXM-02) ──────────────────────

        /// <summary>
        /// Builds a themed context menu Popup for a tab. Matches hamburger menu styling (CTXM-01).
        /// Uses Popup instead of WPF ContextMenu for consistent theming.
        /// </summary>
        private Popup BuildTabContextMenu(NoteTab tab, ListBoxItem item)
        {
            // Close any existing context popup
            if (_activeContextMenu != null) _activeContextMenu.IsOpen = false;

            var popup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.MousePoint,
                HorizontalOffset = 0,
                VerticalOffset = 0
            };

            var border = new Border
            {
                Background = GetBrush("c-sidebar-bg"),
                BorderBrush = GetBrush("c-border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                MinWidth = 200,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12, Opacity = 0.25, ShadowDepth = 4, Direction = 270
                }
            };

            var stack = new StackPanel();

            // Local helper to create a styled menu item Border
            Border CreateCtxItem(string icon, string text, string? shortcut, Action onClick)
            {
                var b = new Border
                {
                    Padding = new Thickness(8, 6, 8, 6),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent
                };
                b.MouseEnter += MenuItem_MouseEnter;
                b.MouseLeave += MenuItem_MouseLeave;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (shortcut != null)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBlock = new TextBlock
                {
                    Text = icon,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-toolbar-icon");
                Grid.SetColumn(iconBlock, 0);
                grid.Children.Add(iconBlock);

                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
                Grid.SetColumn(textBlock, 1);
                grid.Children.Add(textBlock);

                if (shortcut != null)
                {
                    var shortcutBlock = new TextBlock
                    {
                        Text = shortcut,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(16, 0, 0, 0)
                    };
                    shortcutBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
                    Grid.SetColumn(shortcutBlock, 2);
                    grid.Children.Add(shortcutBlock);
                }

                b.Child = grid;
                b.MouseLeftButtonDown += (s, e) => { popup.IsOpen = false; onClick(); };
                return b;
            }

            // Rename
            stack.Children.Add(CreateCtxItem("\uE8AC", "Rename", "F2", () =>
            {
                StartRename(item, tab);
            }));

            // Pin/Unpin (dynamic text based on tab state)
            string pinText = tab.Pinned ? "Unpin" : "Pin";
            string pinIcon = tab.Pinned ? "\uE77A" : "\uE718";
            stack.Children.Add(CreateCtxItem(pinIcon, pinText, "Ctrl+P", () =>
            {
                _activeTab = tab;
                SelectTabByNote(tab);
                ToolbarPin_Click(this, new RoutedEventArgs());
            }));

            // Clone to new tab
            stack.Children.Add(CreateCtxItem("\uF413", "Clone to new tab", "Ctrl+K", () =>
            {
                _activeTab = tab;
                SelectTabByNote(tab);
                ToolbarClone_Click(this, new RoutedEventArgs());
            }));

            // Save as TXT
            stack.Children.Add(CreateCtxItem("\uE74E", "Save as TXT", "Ctrl+S", () =>
            {
                _activeTab = tab;
                SelectTabByNote(tab);
                ToolbarSave_Click(this, new RoutedEventArgs());
            }));

            // Separator
            var sep = new Separator { Margin = new Thickness(4, 2, 4, 2) };
            sep.SetResourceReference(Separator.BackgroundProperty, "c-border");
            stack.Children.Add(sep);

            // Delete
            stack.Children.Add(CreateCtxItem("\uE74D", "Delete", "Ctrl+W", () =>
            {
                _ = DeleteTabAsync(tab);
            }));

            // Delete all below (CTXM-02)
            stack.Children.Add(CreateCtxItem("\uE75C", "Delete all below", null, () =>
            {
                int tabIndex = _tabs.IndexOf(tab);
                if (tabIndex < 0) return;
                var belowTabs = _tabs.Skip(tabIndex + 1).ToList();
                if (belowTabs.Count == 0) return;
                _ = DeleteMultipleAsync(belowTabs); // Skips pinned tabs internally (TDEL-06)
            }));

            border.Child = stack;
            popup.Child = border;
            _activeContextMenu = popup;
            return popup;
        }

        /// <summary>
        /// Starts inline rename for a tab item via context menu action.
        /// Delegates to BeginRename (same as F2 / double-click).
        /// </summary>
        private void StartRename(ListBoxItem item, NoteTab tab)
        {
            BeginRename(item, tab);
        }

        // ─── Phase 9: File Drop (DROP-01 through DROP-07) ───────────────────────

        /// <summary>
        /// DragEnter handler — shows drop overlay when files are dragged over the content area (DROP-05).
        /// </summary>
        private void OnFileDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                _fileDragEnterCount++;
                e.Effects = DragDropEffects.Copy;
                FileDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// DragOver handler — maintains copy cursor while dragging over content area.
        /// </summary>
        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// DragLeave handler — hides drop overlay when drag leaves the content area (DROP-05).
        /// Only hides when the mouse truly leaves the content area bounds.
        /// </summary>
        private void OnFileDragLeave(object sender, DragEventArgs e)
        {
            // R2-DROP-01: Enter/leave counter for reliable overlay dismiss across child boundaries
            _fileDragEnterCount--;
            if (_fileDragEnterCount <= 0)
            {
                _fileDragEnterCount = 0;
                FileDropOverlay.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Drop handler — processes dropped files and creates tabs for valid text files (DROP-01).
        /// </summary>
        private void OnFileDrop(object sender, DragEventArgs e)
        {
            _fileDragEnterCount = 0;
            FileDropOverlay.Visibility = Visibility.Collapsed;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                _ = ProcessDroppedFilesAsync(files);
            }
            e.Handled = true;
        }

        /// <summary>
        /// Processes dropped files: validates, creates tabs, shows errors via toast (DROP-01 to DROP-07).
        /// </summary>
        private async Task ProcessDroppedFilesAsync(string[] filePaths)
        {
            var summary = await FileDropService.ProcessDroppedFilesAsync(filePaths);

            if (summary.ValidFiles.Count > 0)
            {
                // R2-DROP-01: Insert at first position below pinned tabs
                int pinnedCount = _tabs.Count(t => t.Pinned);

                // Shift all unpinned tabs' sort orders down to make room
                for (int i = pinnedCount; i < _tabs.Count; i++)
                    _tabs[i].SortOrder += summary.ValidFiles.Count;

                if (_tabs.Count > pinnedCount)
                {
                    _ = DatabaseService.UpdateNoteSortOrdersAsync(
                        _tabs.Skip(pinnedCount).Select(t => (t.Id, t.SortOrder)));
                }

                int insertOffset = 0;
                foreach (var result in summary.ValidFiles)
                {
                    int sortOrder = pinnedCount + insertOffset;
                    long newId = await DatabaseService.InsertNoteAsync(
                        _desktopGuid, result.FileName, result.Content!, false, sortOrder);

                    var newTab = new NoteTab
                    {
                        Id = newId,
                        DesktopGuid = _desktopGuid,
                        Name = result.FileName,
                        Content = result.Content!,
                        Pinned = false,
                        SortOrder = sortOrder,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    _tabs.Insert(pinnedCount + insertOffset, newTab);
                    insertOffset++;
                }

                // Rebuild and select the first dropped tab
                RebuildTabList();
                var firstDropped = _tabs.FirstOrDefault(t =>
                    t.Name == summary.ValidFiles[0].FileName && !t.Pinned);
                if (firstDropped != null)
                    SelectTabByNote(firstDropped);
            }

            // Show error toast for invalid files (DROP-06)
            if (summary.ErrorCount > 0 && summary.CombinedErrorMessage != null)
            {
                ShowInfoToast(summary.CombinedErrorMessage);
            }
        }

        /// <summary>
        /// Shows an info-only toast (no undo button) that auto-dismisses after 4 seconds.
        /// Used for file drop errors and hotkey conflict notifications.
        /// </summary>
        public void ShowInfoToast(string message)
        {
            ToastMessageBlock.Text = message;
            UndoButton.Visibility = Visibility.Collapsed;

            // If toast already visible, just update content
            if (ToastBorder.Visibility == Visibility.Visible)
                return;

            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            ToastTranslate.Y = 36;
            ToastBorder.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From = 36, To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, anim);

            // Auto-dismiss after 4 seconds
            _ = Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    HideToast();
                    UndoButton.Visibility = Visibility.Visible;
                });
            });
        }

        /// <summary>
        /// Shows a toast for hotkey registration failure on startup.
        /// </summary>
        public void ShowHotkeyConflictToast()
        {
            var combo = HotkeyService.GetHotkeyDisplayString();
            ShowInfoToast($"Global hotkey ({combo}) is in use by another app. Change it in Preferences.");
        }

        // ─── Phase 9: Preferences Panel (PREF-01 through PREF-05) ──────────────

        /// <summary>
        /// Initializes preferences values from database. Called during tab loading.
        /// Loads font size, debounce interval, and updates UI elements.
        /// </summary>
        public async Task InitializePreferencesAsync()
        {
            // Load font size
            var savedFontSize = await DatabaseService.GetPreferenceAsync("font_size");
            _currentFontSize = int.TryParse(savedFontSize, out var fs) ? Math.Clamp(fs, 8, 32) : 13;
            ContentEditor.FontSize = _currentFontSize;
            FontSizeDisplay.Text = FontSizeToPercent(_currentFontSize);

            // Update theme toggle highlight
            UpdateThemeToggleHighlight(ThemeService.CurrentSetting);

            // Update hotkey display
            HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();
        }

        /// <summary>
        /// Preferences menu click — toggle the slide-in panel (PREF-01).
        /// Replaces the Phase 8 stub.
        /// </summary>
        private void MenuPreferences_Click(object sender, MouseButtonEventArgs e)
        {
            HamburgerMenu.IsOpen = false;
            if (_preferencesOpen)
                HidePreferencesPanel();
            else
                ShowPreferencesPanel();
        }

        private void ShowPreferencesPanel()
        {
            // R2-RECOVER-01: One-panel-at-a-time — close recovery if open
            if (_recoveryPanelOpen) HideRecoveryPanel();
            if (_cleanupPanelOpen) HideCleanupPanel();

            _preferencesOpen = true;
            PreferencesPanel.Visibility = Visibility.Visible;

            // Refresh values
            FontSizeDisplay.Text = FontSizeToPercent(_currentFontSize);
            UpdateThemeToggleHighlight(ThemeService.CurrentSetting);
            HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();

            // Slide in from right
            var anim = new DoubleAnimation
            {
                From = 300, To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void HidePreferencesPanel()
        {
            _preferencesOpen = false;
            if (_recordingHotkey)
            {
                _recordingHotkey = false;
                HotkeyRecordText.Text = "Record";
                HotkeyService.ResumeHotkey(); // R2-PREF-02: Re-register if closing during recording
            }

            var anim = new DoubleAnimation
            {
                From = 0, To = 300,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                PreferencesPanel.Visibility = Visibility.Collapsed;
                PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                PrefPanelTransform.X = 300;
            };
            PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void ClosePreferences_Click(object sender, MouseButtonEventArgs e)
        {
            HidePreferencesPanel();
        }

        // ── Theme toggle handlers (PREF-02) ──

        private void ThemeLight_Click(object sender, MouseButtonEventArgs e)
        {
            _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.Light);
            UpdateThemeToggleHighlight(ThemeService.AppTheme.Light);
        }

        private void ThemeSystem_Click(object sender, MouseButtonEventArgs e)
        {
            _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.System);
            UpdateThemeToggleHighlight(ThemeService.AppTheme.System);
        }

        private void ThemeDark_Click(object sender, MouseButtonEventArgs e)
        {
            _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.Dark);
            UpdateThemeToggleHighlight(ThemeService.AppTheme.Dark);
        }

        private void UpdateThemeToggleHighlight(ThemeService.AppTheme active)
        {
            var accentBrush = GetBrush("c-accent");
            var defaultBrush = new SolidColorBrush(Colors.Transparent);

            ThemeLightBtn.Background = active == ThemeService.AppTheme.Light ? accentBrush : defaultBrush;
            ThemeSystemBtn.Background = active == ThemeService.AppTheme.System ? accentBrush : defaultBrush;
            ThemeDarkBtn.Background = active == ThemeService.AppTheme.Dark ? accentBrush : defaultBrush;
        }

        // ── Font size handlers (PREF-03, KEYS-02) ──

        private void FontSizeIncrease_Click(object sender, MouseButtonEventArgs e) => _ = ChangeFontSizeAsync(1);
        private void FontSizeDecrease_Click(object sender, MouseButtonEventArgs e) => _ = ChangeFontSizeAsync(-1);
        private void FontSizeReset_Click(object sender, MouseButtonEventArgs e) => _ = SetFontSizeAsync(13);

        private async Task ChangeFontSizeAsync(int delta)
        {
            int newSize = Math.Clamp(_currentFontSize + delta, 8, 32);
            await SetFontSizeAsync(newSize);
        }

        private static string FontSizeToPercent(int size) => $"{Math.Round(size * 100.0 / 13)}%";

        private async Task SetFontSizeAsync(int size)
        {
            _currentFontSize = size;
            ContentEditor.FontSize = size;
            FontSizeDisplay.Text = FontSizeToPercent(size);
            await DatabaseService.SetPreferenceAsync("font_size", size.ToString());
            ShowFontSizeTooltip(size);
            // R2-FONT-02: RebuildTabList removed — tab labels use fixed sizes, no rebuild needed
        }

        private void ShowFontSizeTooltip(int size)
        {
            FontSizeTooltipText.Text = FontSizeToPercent(size);
            FontSizeTooltip.Visibility = Visibility.Visible;

            _fontSizeTooltipTimer?.Stop();
            _fontSizeTooltipTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _fontSizeTooltipTimer.Tick += (_, _) =>
            {
                FontSizeTooltip.Visibility = Visibility.Collapsed;
                _fontSizeTooltipTimer.Stop();
            };
            _fontSizeTooltipTimer.Start();
        }

        // R2-PREF-01: DebounceInput_TextChanged removed (autosave delay no longer user-configurable)

        // ── Hotkey picker (PREF-05) ──

        private void HotkeyRecord_Click(object sender, MouseButtonEventArgs e)
        {
            if (_recordingHotkey)
            {
                // Cancel recording — re-register the original hotkey
                _recordingHotkey = false;
                HotkeyRecordText.Text = "Record";
                HotkeyService.ResumeHotkey(); // R2-PREF-02
            }
            else
            {
                // Start recording — unregister so the key combo doesn't trigger the hotkey
                HotkeyService.PauseHotkey(); // R2-PREF-02
                _recordingHotkey = true;
                HotkeyRecordText.Text = "Press keys...";
            }
        }

        // ─── Phase 9: Keyboard Shortcuts (KEYS-02, KEYS-03, KEYS-04) ───────────

        /// <summary>
        /// Ctrl+Scroll over editor changes font size; over tab list scrolls normally (KEYS-03).
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            // Hit-test: only change font size if mouse is over the editor area
            var mousePos = e.GetPosition(ContentEditor);
            if (mousePos.X >= 0 && mousePos.X <= ContentEditor.ActualWidth &&
                mousePos.Y >= 0 && mousePos.Y <= ContentEditor.ActualHeight)
            {
                int delta = e.Delta > 0 ? 1 : -1;
                _ = ChangeFontSizeAsync(delta);
                e.Handled = true; // Prevent scroll
            }
            // If not over editor, don't handle — let tab list scroll normally
        }

        // ─── Phase 9: In-Editor Find Bar ────────────────────────────────────────

        private void ShowEditorFindBar()
        {
            EditorFindBar.Visibility = Visibility.Visible;
            EditorFindInput.Focus();
            EditorFindInput.SelectAll();
        }

        private void HideEditorFindBar()
        {
            EditorFindBar.Visibility = Visibility.Collapsed;
            _findMatches.Clear();
            _currentFindIndex = -1;
            EditorFindCount.Text = "";
            ContentEditor.Focus();
        }

        private void EditorFindInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = EditorFindInput.Text;
            _findMatches.Clear();
            _currentFindIndex = -1;

            if (string.IsNullOrEmpty(query) || _activeTab == null)
            {
                EditorFindCount.Text = "";
                return;
            }

            // Case-insensitive search within current note
            string content = ContentEditor.Text;
            int index = 0;
            while ((index = content.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                _findMatches.Add(index);
                index += query.Length;
            }

            if (_findMatches.Count > 0)
            {
                _currentFindIndex = 0;
                HighlightFindMatch();
            }

            EditorFindCount.Text = _findMatches.Count > 0
                ? $"{_currentFindIndex + 1}/{_findMatches.Count}"
                : "No matches";
        }

        private void HighlightFindMatch()
        {
            if (_currentFindIndex < 0 || _currentFindIndex >= _findMatches.Count) return;

            int pos = _findMatches[_currentFindIndex];
            ContentEditor.Select(pos, EditorFindInput.Text.Length);
            var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
            if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);

            EditorFindCount.Text = $"{_currentFindIndex + 1}/{_findMatches.Count}";
        }

        private void EditorFindNext_Click(object sender, MouseButtonEventArgs e)
        {
            if (_findMatches.Count == 0) return;
            _currentFindIndex = (_currentFindIndex + 1) % _findMatches.Count;
            HighlightFindMatch();
        }

        private void EditorFindPrevious_Click(object sender, MouseButtonEventArgs e)
        {
            if (_findMatches.Count == 0) return;
            _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
            HighlightFindMatch();
        }

        private void EditorFindClose_Click(object sender, MouseButtonEventArgs e) => HideEditorFindBar();

        private void EditorFindInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideEditorFindBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    if (_findMatches.Count > 0)
                    {
                        _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
                        HighlightFindMatch();
                    }
                }
                else
                {
                    if (_findMatches.Count > 0)
                    {
                        _currentFindIndex = (_currentFindIndex + 1) % _findMatches.Count;
                        HighlightFindMatch();
                    }
                }
                e.Handled = true;
            }
        }

        // ─── Phase 9: Help Overlay (Ctrl+?) ─────────────────────────────────────

        private void ShowHelpOverlay()
        {
            if (!_helpBuilt)
            {
                BuildHelpContent();
                _helpBuilt = true;
            }
            HelpOverlay.Visibility = Visibility.Visible;
        }

        private void HideHelpOverlay()
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        }

        private void BuildHelpContent()
        {
            var shortcuts = new (string section, (string key, string desc)[] items)[]
            {
                ("TABS", new[]
                {
                    ("Ctrl+T", "New tab"),
                    ("Ctrl+W", "Delete tab"),
                    ("Ctrl+Tab", "Next tab"),
                    ("Ctrl+Shift+Tab", "Previous tab"),
                    ("F2", "Rename tab"),
                    ("Ctrl+P", "Pin / Unpin"),
                    ("Ctrl+K", "Clone tab"),
                }),
                ("EDITOR", new[]
                {
                    ("Ctrl+Z", "Undo"),
                    ("Ctrl+Y", "Redo"),
                    ("Ctrl+Shift+Z", "Redo (alt)"),
                    ("Ctrl+C", "Copy (all if no selection)"),
                    ("Ctrl+V", "Paste"),
                    ("Ctrl+X", "Cut"),
                    ("Ctrl+A", "Select all"),
                    ("Ctrl+S", "Save as TXT"),
                    ("Ctrl+F", "Find in editor / Search tabs"),
                }),
                ("VIEW", new[]
                {
                    ("Ctrl+=", "Increase font size"),
                    ("Ctrl+-", "Decrease font size"),
                    ("Ctrl+0", "Reset font size (100%)"),
                    ("Ctrl+Scroll", "Zoom (over editor)"),
                }),
                ("GLOBAL", new[]
                {
                    (HotkeyService.GetHotkeyDisplayString(), "Focus / minimize JoJot"),
                    ("Ctrl+Shift+/", "Show this help"),
                }),
            };

            foreach (var (section, items) in shortcuts)
            {
                var sectionHeader = new TextBlock
                {
                    Text = section,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 6)
                };
                sectionHeader.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
                HelpContent.Children.Add(sectionHeader);

                foreach (var (key, desc) in items)
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Margin = new Thickness(0, 2, 0, 2);

                    var keyBlock = new TextBlock
                    {
                        Text = key,
                        FontSize = 12,
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontWeight = FontWeights.SemiBold
                    };
                    keyBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-accent");
                    Grid.SetColumn(keyBlock, 0);
                    row.Children.Add(keyBlock);

                    var descBlock = new TextBlock
                    {
                        Text = desc,
                        FontSize = 12
                    };
                    descBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
                    Grid.SetColumn(descBlock, 1);
                    row.Children.Add(descBlock);

                    HelpContent.Children.Add(row);
                }
            }
        }

        private void HelpOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            HideHelpOverlay();
        }

        private void HelpCard_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent click-through to overlay background
        }

        private void HelpClose_Click(object sender, MouseButtonEventArgs e)
        {
            HideHelpOverlay();
        }

        // ─── Phase 10: Window Drag Resolution (DRAG-01 through DRAG-10) ─────────

        /// <summary>
        /// Handles window drag detection from VirtualDesktopService (DRAG-01).
        /// Only processes the event if this window's HWND matches the moved window.
        /// </summary>
        private void OnWindowMovedToDesktop(IntPtr movedHwnd, string fromGuid, string toGuid, string toName)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (myHwnd != movedHwnd) return;

                await ShowDragOverlayAsync(fromGuid, toGuid, toName);
            });
        }

        /// <summary>
        /// Shows the lock overlay for window drag resolution (DRAG-02, DRAG-03).
        /// Writes pending_moves row immediately, then configures buttons based on conflict type.
        /// </summary>
        private async Task ShowDragOverlayAsync(string fromGuid, string toGuid, string toName)
        {
            // DRAG-08 / R2-MOVE-01: Context-aware re-entry handling
            if (_isDragOverlayActive)
            {
                // Moved back to original desktop -- auto-dismiss
                if (toGuid.Equals(_dragFromDesktopGuid, StringComparison.OrdinalIgnoreCase))
                {
                    await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                    _isMisplaced = false;
                    if (Title.Contains(" (misplaced)"))
                        Title = Title.Replace(" (misplaced)", "");
                    await HideDragOverlayAsync();
                    return;
                }
                // Same target desktop -- no-op
                if (toGuid.Equals(_dragToDesktopGuid, StringComparison.OrdinalIgnoreCase))
                    return;
                // Different target desktop -- update overlay in-place (fall through)
                _dragToDesktopGuid = toGuid;
                _dragToDesktopName = toName;
                // Update pending_moves to new target
                await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                await DatabaseService.InsertPendingMoveAsync(_desktopGuid, _dragFromDesktopGuid!, toGuid);
                // Fall through to update UI below
            }
            else
            {
                _isDragOverlayActive = true;
                _dragFromDesktopGuid = fromGuid;
                _dragToDesktopGuid = toGuid;
                _dragToDesktopName = toName;

                // Phase 10.1: Flush unsaved content before entering drag state (audit finding #2)
                await _autosaveService.FlushAsync();

                // DRAG-02: Write pending_moves row immediately
                await DatabaseService.InsertPendingMoveAsync(_desktopGuid, fromGuid, toGuid);
            }

            // Determine if target desktop has an existing JoJot session
            var app = System.Windows.Application.Current as App;
            bool targetHasSession = app?.HasWindowForDesktop(toGuid) ?? false;

            // R2-MOVE-01: Show source desktop name from live COM (not stale DB)
            string sourceLabel;
            try
            {
                // Use live COM data, not stale DB
                var sourceDesktops = VirtualDesktopService.GetAllDesktops();
                var sourceDesktop = sourceDesktops.FirstOrDefault(d =>
                    d.Id.ToString().Equals(_desktopGuid, StringComparison.OrdinalIgnoreCase));
                if (sourceDesktop != null && !string.IsNullOrEmpty(sourceDesktop.Name))
                {
                    sourceLabel = sourceDesktop.Name;
                }
                else if (sourceDesktop != null)
                {
                    sourceLabel = $"Desktop {sourceDesktop.Index + 1}";
                }
                else
                {
                    sourceLabel = "Unknown desktop";
                }
            }
            catch
            {
                sourceLabel = "Unknown desktop"; // best-effort
            }
            DragOverlaySourceName.Text = $"From: {sourceLabel}";

            // Configure overlay content with name fallback
            string displayName;
            if (!string.IsNullOrEmpty(toName))
            {
                displayName = toName;
            }
            else
            {
                var targetDesktops = VirtualDesktopService.GetAllDesktops();
                var targetDesktop = targetDesktops.FirstOrDefault(d =>
                    d.Id.ToString().Equals(toGuid, StringComparison.OrdinalIgnoreCase));
                displayName = targetDesktop != null
                    ? $"Desktop {targetDesktop.Index + 1}"
                    : "another desktop";
            }
            DragOverlayTitle.Text = $"Moved to {displayName}";

            if (targetHasSession)
            {
                DragOverlayMessage.Text = "This desktop already has a JoJot window. What would you like to do?";
                DragMergeBtn.Visibility = Visibility.Visible;
                DragKeepHereBtn.Visibility = Visibility.Collapsed; // R2-MOVE-02: Hide "keep here" — target already has window
            }
            else
            {
                DragOverlayMessage.Text = "Keep your notes on this desktop, or go back?";
                DragMergeBtn.Visibility = Visibility.Collapsed;
                DragKeepHereBtn.Visibility = Visibility.Visible; // R2-MOVE-02: Show "keep here" — no conflict
            }

            // Reset cancel failure state
            DragCancelBtn.Content = "Go back";
            DragCancelFailureText.Visibility = Visibility.Collapsed;

            // Show with 150ms fade-in animation
            DragOverlay.Opacity = 0;
            DragOverlay.Visibility = Visibility.Visible;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            DragOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// DRAG-04: Reparent — re-scope window and all notes to the new desktop.
        /// </summary>
        private async void DragKeepHere_Click(object sender, RoutedEventArgs e)
        {
            if (_dragToDesktopGuid is null) return;

            string oldGuid = _desktopGuid;
            string newGuid = _dragToDesktopGuid;

            // Re-check at click time: another window may have been created for the target
            // desktop (via IPC) while the overlay was showing.
            var app = System.Windows.Application.Current as App;
            if (app?.HasWindowForDesktop(newGuid) == true)
            {
                // Refresh overlay with keep-here hidden and merge visible
                DragOverlayMessage.Text = "Another window was opened on this desktop. You can merge or go back.";
                DragKeepHereBtn.Visibility = Visibility.Collapsed;
                DragMergeBtn.Visibility = Visibility.Visible;
                return;
            }

            // Update notes in database to new desktop
            await DatabaseService.MigrateNotesDesktopGuidAsync(oldGuid, newGuid);

            // Update this window's desktop GUID
            _desktopGuid = newGuid;

            // Update window registry in App (reuse app from guard above)
            app ??= System.Windows.Application.Current as App;
            app?.ReparentWindow(oldGuid, newGuid);

            // Update window title to new desktop name (use fresh COM name, not stale _dragToDesktopName)
            var desktops = VirtualDesktopService.GetAllDesktops();
            var targetInfo = desktops.FirstOrDefault(d =>
                d.Id.ToString().Equals(newGuid, StringComparison.OrdinalIgnoreCase));
            string name = targetInfo?.Name ?? _dragToDesktopName ?? "";
            UpdateDesktopTitle(name, targetInfo?.Index);

            // R2-MOVE-01: Update app_state session with full metadata (guid + name + index)
            string targetName = targetInfo?.Name ?? name;
            int? targetIndex = targetInfo?.Index;
            await DatabaseService.UpdateSessionDesktopAsync(oldGuid, newGuid, targetName, targetIndex);

            // Clear pending move
            await DatabaseService.DeletePendingMoveAsync(oldGuid);

            // Clear misplaced state
            _isMisplaced = false;

            // Hide overlay with fade-out
            await HideDragOverlayAsync();

            LogService.Info($"Reparented window from {oldGuid} to {newGuid}");
        }

        /// <summary>
        /// DRAG-05: Merge — append tabs to existing window on target desktop, close this window.
        /// </summary>
        private async void DragMerge_Click(object sender, RoutedEventArgs e)
        {
            if (_dragToDesktopGuid is null || _dragFromDesktopGuid is null) return;

            string sourceGuid = _desktopGuid;
            string targetGuid = _dragToDesktopGuid;

            // Migrate tabs preserving pin state (unlike orphan recovery which unpins)
            await DatabaseService.MigrateTabsPreservePinsAsync(sourceGuid, targetGuid);

            // Clear pending move
            await DatabaseService.DeletePendingMoveAsync(sourceGuid);

            // Notify target window to reload tabs
            var app = System.Windows.Application.Current as App;
            app?.ReloadWindowTabs(targetGuid);

            // Show toast on target window
            int tabCount = _tabs.Count;
            string fromName = Title.Replace("JoJot \u2014 ", "").Replace(" (misplaced)", "");
            app?.ShowMergeToast(targetGuid, tabCount, fromName);

            // Hide overlay and close this window
            _isDragOverlayActive = false;
            DragOverlay.Visibility = Visibility.Collapsed;

            // Unsubscribe from events before closing
            VirtualDesktopService.WindowMovedToDesktop -= OnWindowMovedToDesktop;

            FlushAndClose();

            LogService.Info($"Merged {tabCount} tabs from {sourceGuid} to {targetGuid}");
        }

        /// <summary>
        /// DRAG-06: Cancel — move window back to original desktop.
        /// DRAG-07: On failure, replace Go back with Retry + instruction text.
        /// </summary>
        private async void DragCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_dragFromDesktopGuid is null) return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            bool success = VirtualDesktopService.TryMoveWindowToDesktop(hwnd, _dragFromDesktopGuid);

            if (success)
            {
                // Clear pending move
                await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                _isMisplaced = false;

                // Remove "(misplaced)" badge from title
                if (Title.Contains(" (misplaced)"))
                    Title = Title.Replace(" (misplaced)", "");

                // Hide overlay with fade-out
                await HideDragOverlayAsync();

                LogService.Info($"Cancel: moved window back to {_dragFromDesktopGuid}");
            }
            else
            {
                // DRAG-07: Cancel failed — show retry + instruction
                DragCancelBtn.Content = "Retry";
                DragCancelFailureText.Visibility = Visibility.Visible;

                LogService.Warn($"Cancel failed: could not move window back to {_dragFromDesktopGuid}");
            }
        }

        /// <summary>
        /// Fades out the drag overlay over 150ms, then collapses it and resets state.
        /// </summary>
        private async Task HideDragOverlayAsync()
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                }
            };

            var tcs = new TaskCompletionSource<bool>();
            fadeOut.Completed += (_, _) => tcs.SetResult(true);
            DragOverlay.BeginAnimation(OpacityProperty, fadeOut);
            await tcs.Task;

            DragOverlay.Visibility = Visibility.Collapsed;
            _isDragOverlayActive = false;
            _dragFromDesktopGuid = null;
            _dragToDesktopGuid = null;
            _dragToDesktopName = null;
        }

        /// <summary>
        /// DRAG-10: When a misplaced window gains focus, auto-show the lock overlay.
        /// A window is misplaced when its stored desktop GUID doesn't match the desktop
        /// it's currently on (detected via COM).
        /// </summary>
        private async void OnWindowActivated_CheckMisplaced(object? sender, EventArgs e)
        {
            // Don't skip when overlay active -- ShowDragOverlayAsync handles re-entry
            // (auto-dismiss on return, update on third desktop, no-op on same)
            if (!VirtualDesktopService.IsAvailable) return;

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                Guid currentDesktop = Interop.VirtualDesktopInterop.GetWindowDesktopId(hwnd);
                string currentGuid = currentDesktop.ToString();

                if (!currentGuid.Equals(_desktopGuid, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_isMisplaced)
                    {
                        _isMisplaced = true;
                        // Update title with "(misplaced)" badge
                        string currentTitle = Title;
                        if (!currentTitle.Contains("(misplaced)"))
                        {
                            Title = currentTitle + " (misplaced)";
                        }
                    }

                    // Auto-show lock overlay
                    string toName = "";
                    var desktops = VirtualDesktopService.GetAllDesktops();
                    var targetInfo = desktops.FirstOrDefault(d =>
                        d.Id.ToString().Equals(currentGuid, StringComparison.OrdinalIgnoreCase));
                    toName = targetInfo?.Name ?? "";

                    await ShowDragOverlayAsync(_desktopGuid, currentGuid, toName);
                }
                else if (_isMisplaced)
                {
                    // Window is now on correct desktop — clear misplaced state
                    _isMisplaced = false;
                    string currentTitle = Title;
                    if (currentTitle.Contains(" (misplaced)"))
                    {
                        Title = currentTitle.Replace(" (misplaced)", "");
                    }

                    // Dismiss the move overlay if it's still showing
                    if (_isDragOverlayActive)
                    {
                        await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                        await HideDragOverlayAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"Misplaced check failed: {ex.Message}");
            }
        }

    }
}

