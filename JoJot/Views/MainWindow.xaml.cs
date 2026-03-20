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
using JoJot.ViewModels;

namespace JoJot;

/// <summary>
/// Per-desktop window bound to a virtual desktop GUID.
/// Full tab management UI with tab panel, content editor, search, and keyboard navigation.
/// Windows are created on-demand and destroyed on close (not hidden).
/// The process stays alive via ShutdownMode.OnExplicitShutdown.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// ViewModel backing this window's core state (tabs, active tab, search, desktop identity).
    /// </summary>
    internal MainWindowViewModel ViewModel { get; }

    // Forwarding properties — all partial classes continue using these names unchanged.
    private ObservableCollection<NoteTab> _tabs => ViewModel.Tabs;
    private NoteTab? _activeTab
    {
        get => ViewModel.ActiveTab;
        set => ViewModel.ActiveTab = value;
    }
    private string _searchText
    {
        get => ViewModel.SearchText;
        set => ViewModel.SearchText = value;
    }
    private string _desktopGuid
    {
        get => ViewModel.DesktopGuid;
        set => ViewModel.DesktopGuid = value;
    }

    // ─── Rename state ───────────────────────────────────────────────────────
    private (ListBoxItem Item, NoteTab Tab, TextBox Box, TextBlock Label)? _activeRename;

    // ─── Drag-to-reorder state ──────────────────────────────────────────────
    private bool _isDragging;
    private bool _isCompletingDrag; // Re-entrancy guard for CompleteDrag/LostMouseCapture
    private bool _isTransferringCapture; // Guard for Mouse.Capture transfer during drag start

    // ─── Tab list rebuild guard ────────────────────────
    private bool _isRebuildingTabList;
    private System.Windows.Point _dragStartPoint;
    private ListBoxItem? _dragItem;
    private NoteTab? _dragTab;
    private int _dragInsertIndex = -1;
    private int _dragOriginalListIndex = -1; // Track original position for indicator suppression
    private Border? _dropIndicatorBorder;
    private NoteTab? _fadeInTab; // Tab to fade in after drag-reorder rebuild

    // ─── Context menu state ─────────────────────────────────────────
    private Popup? _activeContextMenu;

    private DateTime _hamburgerClosedAt;

    // ─── Panel state (forwarded to ViewModel) ──────────────────────
    private bool _recoveryPanelOpen
    {
        get => ViewModel.IsRecoveryOpen;
        set => ViewModel.IsRecoveryOpen = value;
    }

    private bool _cleanupPanelOpen
    {
        get => ViewModel.IsCleanupOpen;
        set => ViewModel.IsCleanupOpen = value;
    }

    private bool _preferencesOpen
    {
        get => ViewModel.IsPreferencesOpen;
        set => ViewModel.IsPreferencesOpen = value;
    }
    private int _currentFontSize = 13;
    private System.Windows.Threading.DispatcherTimer? _fontSizeTooltipTimer;

    // ─── Find panel state (forwarded to ViewModel) ─────────────────
    private bool _findPanelOpen
    {
        get => ViewModel.IsFindPanelOpen;
        set => ViewModel.IsFindPanelOpen = value;
    }

    // ─── Window Drag Detection (forwarded to ViewModel) ────────
    private bool _isDragOverlayActive
    {
        get => ViewModel.IsDragOverlayActive;
        set => ViewModel.IsDragOverlayActive = value;
    }
    private string? _dragFromDesktopGuid
    {
        get => ViewModel.DragFromDesktopGuid;
        set => ViewModel.DragFromDesktopGuid = value;
    }
    private string? _dragToDesktopGuid
    {
        get => ViewModel.DragToDesktopGuid;
        set => ViewModel.DragToDesktopGuid = value;
    }
    private string? _dragToDesktopName
    {
        get => ViewModel.DragToDesktopName;
        set => ViewModel.DragToDesktopName = value;
    }
    private bool _isMisplaced
    {
        get => ViewModel.IsMisplaced;
        set => ViewModel.IsMisplaced = value;
    }
    private CancellationTokenSource? _misplacedCheckCts; // debounce rapid desktop switches
    private int _fileDragEnterCount;       // Enter/leave counter for reliable overlay dismiss

    // ─── Soft-delete / toast state ────────────────────────────────
    private record PendingDeletion(
        List<NoteTab> Tabs,
        List<int> OriginalIndexes,
        CancellationTokenSource Cts
    );

    private PendingDeletion? _pendingDeletion;

    // ─── Autosave & Undo ─────────────────────────────────────────
    private readonly AutosaveService _autosaveService = new();
    private readonly System.Windows.Threading.DispatcherTimer _checkpointTimer;
    private readonly System.Windows.Threading.DispatcherTimer _stalenessTimer;
    private bool _suppressTextChanged
    {
        get => ViewModel.IsRestoringContent;
        set => ViewModel.IsRestoringContent = value;
    }
    private string? _lastSaveDirectory;

    // ─── Toast undo action (for Replace All) ────────────────────────
    private Action? _pendingToastUndoAction;

    // ─── Theme-aware brush helper ──
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
        ViewModel = new MainWindowViewModel(desktopGuid);

        // Initialize checkpoint timer before InitializeComponent
        _checkpointTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _checkpointTimer.Tick += CheckpointTimer_Tick;

        _stalenessTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromHours(1)
        };
        _stalenessTimer.Tick += (_, _) => RefreshStalenessIndicators();
        _stalenessTimer.Start();

        InitializeComponent();
        InitializeInputBindings();

        // Register with ThemeService for title bar dark mode tracking
        ThemeService.RegisterWindow(this);
        HelpOverlay.CloseRequested += (_, _) => ViewModel.IsHelpOpen = false;
        CleanupPanel.CloseRequested += (_, _) => HideCleanupPanel();
        RecoveryPanel.CloseRequested += (_, _) => HideRecoveryPanel();
        DragOverlay.KeepHereClicked += (_, _) => DragKeepHere_Handler();
        DragOverlay.MergeClicked += (_, _) => DragMerge_Handler();
        DragOverlay.CancelClicked += (_, _) => DragCancel_Handler();
        CleanupPanel.FilterChanged += (_, _) => { if (_cleanupPanelOpen) RefreshCleanupPreview(); };
        CleanupPanel.DeleteRequested += (_, candidates) =>
        {
            int pinnedCount = candidates.Count(t => t.Pinned);
            string pinnedNote = pinnedCount > 0 ? $" (including {pinnedCount} pinned)" : "";
            string message = $"This will permanently delete {candidates.Count} tab{(candidates.Count == 1 ? "" : "s")}{pinnedNote}. This cannot be undone.";
            ShowConfirmation("Clean up tabs", message, () => _ = ExecuteCleanupDeleteAsync(candidates));
        };
        PreferencesPanel.CloseRequested += (_, _) => HidePreferencesPanel();
        PreferencesPanel.ThemeChangeRequested += (_, theme) => _ = ThemeService.SetThemeAsync(theme);
        PreferencesPanel.FontSizeChangeRequested += (_, delta) => _ = ChangeFontSizeAsync(delta);
        PreferencesPanel.FontSizeResetRequested += (_, _) => _ = SetFontSizeAsync(13);
        PreferencesPanel.HotkeyRecordingChanged += (_, isRecording) =>
        {
            if (isRecording) HotkeyService.PauseHotkey();
            else HotkeyService.ResumeHotkey();
        };
        WireUpFindPanelEvents();

        // Configure autosave service (undo snapshots are pushed per-keystroke in ContentEditor_TextChanged)
        _autosaveService.Configure(
            contentProvider: () => _activeTab is not null ? (_activeTab.Id, ContentEditor.Text) : (0L, ""),
            saveFunc: NoteStore.UpdateNoteContentAsync,
            onSaveCompleted: (tabId) =>
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab is not null)
                {
                    tab.UpdatedAt = DateTime.Now;
                }
            }
        );

        // Wire TextChanged for autosave debounce
        ContentEditor.TextChanged += ContentEditor_TextChanged;

        // Allow file drops to propagate to Window handler; suppress text drops
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

            // Prevent re-entrant cleanup (Mouse.Capture(null) in CompleteDrag can re-fire)
            // Skip when capture is being intentionally transferred to TabList
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
                if (_dragItem is not null)
                {
                    var abortBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
                    if (abortBorder is not null) abortBorder.Opacity = 1.0;
                }
                CompleteDrag();
            }
        };

        // TabList-level mouse move: handles drag start (fast mouse) and tracking between items
        TabList.PreviewMouseMove += (s, e) =>
        {
            if (_dragItem is null || _dragTab is null) return;
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

        // Delete button hover — opacity 0.7 → 1.0
        ToolbarDelete.MouseEnter += (s, e) => DeleteIconText.Opacity = 1.0;
        ToolbarDelete.MouseLeave += (s, e) => DeleteIconText.Opacity = 0.7;

        // Track hamburger close time for toggle behavior
        HamburgerMenu.Closed += (s, e) =>
        {
            _hamburgerClosedAt = DateTime.UtcNow;
            HamburgerMenu.StaysOpen = false;
        };

        // Force-close hamburger menu on any click outside
        // Also commit active rename when clicking outside the rename TextBox
        PreviewMouseDown += (s, e) =>
        {
            if (HamburgerMenu.IsOpen
                && !IsMouseOverPopup(HamburgerMenu)
                && !IsMouseOverElement(HamburgerButton))
            {
                HamburgerMenu.IsOpen = false;
            }

            if (_activeRename is var (_, _, renameBox, _)
                && !IsMouseOverElement(renameBox))
            {
                CommitRename();
            }
        };

        // Commit active rename when clicking outside the window
        Deactivated += (s, e) =>
        {
            if (_activeRename is not null)
                CommitRename();
        };

        // Initial toolbar state — all buttons disabled until tab selected
        UpdateToolbarState();

        // Window drag detection and misplaced check
        VirtualDesktopService.WindowMovedToDesktop += OnWindowMovedToDesktop;
        Activated += OnWindowActivated_CheckMisplaced;
        Activated += (_, _) => RefreshStalenessIndicators();
    }

    /// <summary>
    /// The virtual desktop GUID this window is bound to.
    /// Used by App for registry keying and event routing.
    /// </summary>
    public string DesktopGuid => ViewModel.DesktopGuid;

    // ─── Staleness Refresh ─────────────────────────────────────────────────

    /// <summary>
    /// Notifies all tabs to recalculate their staleness indicator opacity.
    /// Called on window activation and by the hourly timer.
    /// </summary>
    private void RefreshStalenessIndicators()
    {
        foreach (var tab in _tabs)
            tab.RefreshStaleness();
    }

    // ─── Visual Tree Helper ─────────────────────────────────────────────────

    /// <summary>
    /// Walks the visual tree to find the first descendant of type T with the given Name.
    /// Used to locate named elements inside DataTemplate instances.
    /// </summary>
    private static T? FindNamedDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result && result.Name == name) return result;
            var found = FindNamedDescendant<T>(child, name);
            if (found is not null) return found;
        }
        return null;
    }

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
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Finds the ScrollViewer inside a TextBox visual tree.
    /// Used for saving/restoring scroll position on tab switch.
    /// </summary>
    private static ScrollViewer? GetScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var result = GetScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Animates the Opacity property of a UIElement from one value to another over durationMs.
    /// Used for the tab x icon fade-in/out on hover.
    /// </summary>
    private static void AnimateOpacity(UIElement element, double from, double to, int durationMs)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs));
        element.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>
    /// Collapses an element after a short delay (allows fade-out animation to complete).
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

    // ─── Window Title ───────────────────────────────────────────────────────

    /// <summary>
    /// Updates the window title based on the current desktop identity.
    /// Delegates to ViewModel for title formatting.
    /// </summary>
    public void UpdateDesktopTitle(string? desktopName, int? desktopIndex)
    {
        ViewModel.UpdateDesktopInfo(desktopName, desktopIndex);
        Title = ViewModel.WindowTitle;
    }

    // ─── Window Lifecycle ───────────────────────────────────────────────────

    /// <summary>
    /// Flushes content, removes empty tabs, saves window geometry, then closes for real.
    /// Called by App on explicit shutdown (Exit menu, etc.).
    /// Commits any pending soft-delete so data is not lost on shutdown.
    /// </summary>
    public void FlushAndClose()
    {
        LogService.Info("FlushAndClose called for desktop {DesktopGuid}", _desktopGuid);

        // Unsubscribe from drag detection to prevent events firing during close
        VirtualDesktopService.WindowMovedToDesktop -= OnWindowMovedToDesktop;

        // Stop autosave and flush synchronously
        _autosaveService.Stop();
        _checkpointTimer.Stop();

        // Commit pending deletion synchronously before close
        CommitPendingDeletionAsync().GetAwaiter().GetResult();
        SaveCurrentTabContent();
        Close();
    }

    /// <summary>
    /// Window close saves content and geometry, then destroys.
    /// Synchronous flush — no data loss on close.
    /// The process stays alive (ShutdownMode.OnExplicitShutdown).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Remove WndProc hook before HWND is destroyed
        RemoveDesktopActivationHook();

        // Stop autosave timer and flush synchronously
        _autosaveService.Stop();
        _checkpointTimer.Stop();

        // Synchronous flush — block until save completes (no data loss)
        if (_activeTab is not null)
        {
            string content = ContentEditor.Text;
            if (content != _activeTab.Content)
            {
                _activeTab.Content = content;
                _activeTab.CursorPosition = ContentEditor.CaretIndex;
                _activeTab.UpdatedAt = DateTime.Now;
                NoteStore.UpdateNoteContentAsync(_activeTab.Id, content)
                    .GetAwaiter().GetResult(); // Sync flush on close — no data loss
            }
        }

        // Commit pending deletions before close
        CommitPendingDeletionAsync().GetAwaiter().GetResult();

        // Capture geometry while the HWND is still valid
        var geo = WindowPlacementHelper.CaptureGeometry(this);

        // Fire-and-forget: save geometry to database (process stays alive so this completes)
        _ = SessionStore.SaveWindowGeometryAsync(_desktopGuid, geo);

        LogService.Info("Window closing for desktop {DesktopGuid} \u2014 geometry saved ({Left},{Top} {Width}x{Height} maximized={IsMaximized})", _desktopGuid, geo.Left, geo.Top, geo.Width, geo.Height);

        // Do NOT set e.Cancel = true — let the window close and be destroyed
    }

    // ─── Autosave & Undo Helpers ────────────────────────────────────

    /// <summary>
    /// TextChanged handler for autosave debounce trigger and per-keystroke undo snapshots.
    /// Only fires for user-initiated changes (suppressed during programmatic text assignment).
    /// </summary>
    private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _activeTab is null) return;

        // Sync editor text to model so DisplayLabel binding updates live
        _activeTab.Content = ContentEditor.Text;

        // Push per-keystroke undo snapshot (decoupled from autosave)
        UndoManager.Instance.PushSnapshot(_activeTab.Id, _activeTab.Content, ContentEditor.CaretIndex);

        _autosaveService.NotifyTextChanged();

        // Start/reset checkpoint timer on user input
        _checkpointTimer.Stop();
        _checkpointTimer.Start();

        // Re-run find highlights if panel is open
        RefreshFindOnTextChange();
    }

    /// <summary>
    /// 5-minute checkpoint timer tick. Creates a tier-2 checkpoint if content
    /// has changed since the last checkpoint.
    /// </summary>
    private void CheckpointTimer_Tick(object? sender, EventArgs e)
    {
        _checkpointTimer.Stop();
        if (_activeTab is null) return;

        var stack = UndoManager.Instance.GetStack(_activeTab.Id);
        if (stack is not null && stack.ShouldCreateCheckpoint())
        {
            string content = ContentEditor.Text;
            stack.PushCheckpoint(content);
        }

        // Restart for next 5-minute interval
        _checkpointTimer.Start();
    }

    /// <summary>
    /// Performs undo by restoring previous snapshot from the per-tab UndoStack.
    /// Sets _suppressTextChanged to prevent the text assignment from triggering autosave.
    /// </summary>
    private void PerformUndo()
    {
        if (_activeTab is null) return;

        // Capture current editor content so redo can restore it.
        // PushSnapshot deduplicates if content matches the current index.
        UndoManager.Instance.PushSnapshot(_activeTab.Id, ContentEditor.Text, ContentEditor.CaretIndex);

        var entry = UndoManager.Instance.Undo(_activeTab.Id);
        if (entry is null) return;

        _suppressTextChanged = true;
        _activeTab.Content = entry.Value.Content;
        ContentEditor.Text = entry.Value.Content;
        ContentEditor.CaretIndex = Math.Min(entry.Value.CursorPosition, entry.Value.Content.Length);
        _suppressTextChanged = false;

        UpdateToolbarState(); // refresh undo/redo button states
        RefreshFindOnTextChange(); // update find highlights after undo
    }

    /// <summary>
    /// Performs redo by advancing to next snapshot in the per-tab UndoStack.
    /// </summary>
    private void PerformRedo()
    {
        if (_activeTab is null) return;
        var entry = UndoManager.Instance.Redo(_activeTab.Id);
        if (entry is null) return;

        _suppressTextChanged = true;
        _activeTab.Content = entry.Value.Content;
        ContentEditor.Text = entry.Value.Content;
        ContentEditor.CaretIndex = Math.Min(entry.Value.CursorPosition, entry.Value.Content.Length);
        _suppressTextChanged = false;

        UpdateToolbarState(); // refresh undo/redo button states
        RefreshFindOnTextChange(); // update find highlights after redo
    }

    /// <summary>
    /// Opens a Save As dialog for exporting the active note as UTF-8 TXT with BOM.
    /// Remembers the last save directory within the session (resets on app launch).
    /// </summary>
    private void SaveAsTxt()
    {
        if (_activeTab is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = MainWindowViewModel.GetDefaultFilename(_activeTab)
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
}
