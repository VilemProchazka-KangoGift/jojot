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

namespace JoJot;

/// <summary>
/// Per-desktop window bound to a virtual desktop GUID.
/// Full tab management UI with tab panel, content editor, search, and keyboard navigation.
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

    // ─── Context menu state ─────────────────────────────────────────
    private Popup? _activeContextMenu;

    // ─── Confirmation dialog state ──────────────────────
    private Action? _confirmAction;
    private DateTime _hamburgerClosedAt;

    // ─── Recovery panel state ──────────────────────
    private bool _recoveryPanelOpen;

    // ─── Cleanup panel state ──────────────────────
    private bool _cleanupPanelOpen;

    // ─── Preferences, File Drop, Find Bar, Font Size ─────
    private bool _preferencesOpen;
    private bool _recordingHotkey;
    private int _currentFontSize = 13;
    private System.Windows.Threading.DispatcherTimer? _fontSizeTooltipTimer;
    private List<int> _findMatches = [];
    private int _currentFindIndex = -1;
    private bool _helpBuilt;

    // ─── Window Drag Detection ──────────────────────────────
    private bool _isDragOverlayActive;     // guard against second drag
    private string? _dragFromDesktopGuid;  // Origin desktop GUID for cancel flow
    private string? _dragToDesktopGuid;    // Target desktop GUID
    private string? _dragToDesktopName;    // Target desktop name for display
    private bool _isMisplaced;            // window GUID doesn't match desktop
    private System.Threading.CancellationTokenSource? _misplacedCheckCts; // debounce rapid desktop switches
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
    private bool _suppressTextChanged;
    private string? _lastSaveDirectory;

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
        _desktopGuid = desktopGuid;

        // Initialize checkpoint timer before InitializeComponent
        _checkpointTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _checkpointTimer.Tick += CheckpointTimer_Tick;

        InitializeComponent();

        // Configure autosave service
        _autosaveService.Configure(
            contentProvider: () => _activeTab is not null ? (_activeTab.Id, ContentEditor.Text) : (0L, ""),
            onSaveCompleted: (tabId) =>
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab is not null)
                {
                    tab.UpdatedAt = DateTime.Now;
                    UpdateTabItemDisplay(tab);
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
                if (_dragItem?.Content is FrameworkElement abortContent) abortContent.Opacity = 1.0;
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
    }

    /// <summary>
    /// The virtual desktop GUID this window is bound to.
    /// Used by App for registry keying and event routing.
    /// </summary>
    public string DesktopGuid => _desktopGuid;

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
        element.BeginAnimation(UIElement.OpacityProperty, anim);
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
                DatabaseService.UpdateNoteContentAsync(_activeTab.Id, content)
                    .GetAwaiter().GetResult(); // Sync flush on close — no data loss
            }
        }

        // Commit pending deletions before close
        CommitPendingDeletionAsync().GetAwaiter().GetResult();

        // Capture geometry while the HWND is still valid
        var geo = WindowPlacementHelper.CaptureGeometry(this);

        // Fire-and-forget: save geometry to database (process stays alive so this completes)
        _ = DatabaseService.SaveWindowGeometryAsync(_desktopGuid, geo);

        LogService.Info("Window closing for desktop {DesktopGuid} \u2014 geometry saved ({Left},{Top} {Width}x{Height} maximized={IsMaximized})", _desktopGuid, geo.Left, geo.Top, geo.Width, geo.Height);

        // Do NOT set e.Cancel = true — let the window close and be destroyed
    }

    // ─── Autosave & Undo Helpers ────────────────────────────────────

    /// <summary>
    /// TextChanged handler for autosave debounce trigger.
    /// Only fires for user-initiated changes (suppressed during programmatic text assignment).
    /// </summary>
    private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _activeTab is null) return;
        _autosaveService.NotifyTextChanged();

        // Start/reset checkpoint timer on user input
        _checkpointTimer.Stop();
        _checkpointTimer.Start();
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
        var content = UndoManager.Instance.Undo(_activeTab.Id);
        if (content is null) return;

        _suppressTextChanged = true;
        _activeTab.Content = content;
        ContentEditor.Text = content;
        ContentEditor.CaretIndex = Math.Min(_activeTab.CursorPosition, content.Length);
        _suppressTextChanged = false;

        UpdateTabItemDisplay(_activeTab);
        UpdateToolbarState(); // refresh undo/redo button states
    }

    /// <summary>
    /// Performs redo by advancing to next snapshot in the per-tab UndoStack.
    /// </summary>
    private void PerformRedo()
    {
        if (_activeTab is null) return;
        var content = UndoManager.Instance.Redo(_activeTab.Id);
        if (content is null) return;

        _suppressTextChanged = true;
        _activeTab.Content = content;
        ContentEditor.Text = content;
        ContentEditor.CaretIndex = Math.Min(_activeTab.CursorPosition, content.Length);
        _suppressTextChanged = false;

        UpdateTabItemDisplay(_activeTab);
        UpdateToolbarState(); // refresh undo/redo button states
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
}
