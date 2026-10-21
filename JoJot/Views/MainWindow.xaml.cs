using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Resources;
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

    // ─── Rename state ───────────────────────────────────────────────────
    // One shared TextBox (MainWindow.Rename.cs) is inserted into the renamed item, replacing
    // its TitlePanel while active; Title is that hidden TitlePanel.
    private (ListBoxItem Item, NoteTab Tab, TextBox Box, FrameworkElement Title)? _activeRename;

    // ─── Drag-to-reorder state ──────────────────────────────────────────
    private bool _isDragging;
    private bool _isCompletingDrag; // Re-entrancy guard for CompleteDrag/LostMouseCapture
    private bool _isTransferringCapture; // Guard for Mouse.Capture transfer during drag start

    // ─── Tab list rebuild guard ─────────────────────────────────────────
    private bool _isRebuildingTabList;
    private System.Windows.Point _dragStartPoint;
    private ListBoxItem? _dragItem;
    private NoteTab? _dragTab;
    private int _dragInsertIndex = -1;
    private int _dragOriginalListIndex = -1; // Track original position for indicator suppression
    private Border? _dropIndicatorBorder;
    private NoteTab? _fadeInTab; // Tab to fade in after drag-reorder rebuild

    // ─── Context menu state ─────────────────────────────────────────────
    private Popup? _activeContextMenu;

    private DateTime _hamburgerClosedAt;

    // ─── Panel state (forwarded to ViewModel) ───────────────────────────
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

    // ─── Find panel state (forwarded to ViewModel) ──────────────────────
    private bool _findPanelOpen
    {
        get => ViewModel.IsFindPanelOpen;
        set => ViewModel.IsFindPanelOpen = value;
    }

    // ─── Window Drag Detection (forwarded to ViewModel) ─────────────────
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

    // ─── Soft-delete / toast state ──────────────────────────────────────
    private record PendingDeletion(
        List<NoteTab> Tabs,
        List<int> OriginalIndexes,
        CancellationTokenSource Cts
    );

    private PendingDeletion? _pendingDeletion;

    // ─── Autosave & Undo ────────────────────────────────────────────────
    private readonly AutosaveService _autosaveService = new();
    private readonly System.Windows.Threading.DispatcherTimer _checkpointTimer;
    private readonly System.Windows.Threading.DispatcherTimer _stalenessTimer;
    private bool _suppressTextChanged
    {
        get => ViewModel.IsRestoringContent;
        set => ViewModel.IsRestoringContent = value;
    }
    private string? _lastSaveDirectory;
    private static readonly System.Text.Encoding Utf8Bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    // ─── Toast undo action (for Replace All) ────────────────────────────
    private Action? _pendingToastUndoAction;

    // ─── Theme-aware brush helper ───────────────────────────────────────
    // Use SetResourceReference for element properties that should auto-update on theme switch.
    // Use GetBrush for one-time assignments or comparisons.
    private SolidColorBrush GetBrush(string key) =>
        (TryFindResource(key) as SolidColorBrush) ?? (SolidColorBrush)System.Windows.Media.Brushes.Black;

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
        // Side panels/overlays are created lazily on first use (MainWindow.LazyPanels.cs),
        // which also wires their events. Only the editor-side find adorner hooks go here.
        WireUpFindAdornerEvents();

        // Configure autosave service (undo snapshots are pushed per-keystroke in ContentEditor_TextChanged)
        _autosaveService.Configure(
            contentProvider: () => _activeTab is not null ? (_activeTab.Id, ContentEditor.Text) : (0L, ""),
            saveFunc: NoteStore.UpdateNoteContentAsync,
            onSaveCompleted: (tabId) =>
            {
                // FlushAsync / FlushInBackground complete on a background thread (the save ran on
                // the DB thread); _tabs and the bound UpdatedAt are UI-affine. InvokeAsync, never
                // Invoke: a UI thread sync-blocked on the close path must not deadlock this.
                var savedAt = DateTime.Now;
                void Apply()
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab is not null)
                    {
                        tab.UpdatedAt = savedAt;
                    }
                }

                if (Dispatcher.CheckAccess())
                    Apply();
                else
                    Dispatcher.InvokeAsync(Apply);
            }
        );

        // Wire TextChanged for autosave debounce
        ContentEditor.TextChanged += ContentEditor_TextChanged;

        // Triple-click selects the whole logical line. handledEventsToo: the TextBox's internal
        // TextEditor may mark mouse events handled before instance handlers run — we must see the
        // click regardless. The matching mouse-up is suppressed so the editor's own up-processing
        // can't collapse the selection we just made.
        ContentEditor.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ContentEditor_TripleClickSelectLine), handledEventsToo: true);
        ContentEditor.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ContentEditor_TripleClickMouseUp), handledEventsToo: true);

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

        // Surface background autosave failures as a toast
        BackgroundWriter.WriteFailed += OnBackgroundWriteFailed;
    }

    /// <summary>
    /// The virtual desktop GUID this window is bound to.
    /// Used by App for registry keying and event routing.
    /// </summary>
    public string DesktopGuid => ViewModel.DesktopGuid;

    // ─── Staleness Refresh ──────────────────────────────────────────────

    /// <summary>
    /// Notifies all tabs to recalculate their staleness indicator opacity.
    /// Called on window activation and by the hourly timer.
    /// </summary>
    private void RefreshStalenessIndicators()
    {
        foreach (var tab in _tabs)
            tab.RefreshStaleness();
    }

    // ─── Visual Tree Helper ─────────────────────────────────────────────

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
    /// Returns the cached ScrollViewer for ContentEditor, walking the visual tree only once.
    /// </summary>
    private ScrollViewer? GetContentScrollViewer()
    {
        return _cachedContentScrollViewer ??= FindDescendant<ScrollViewer>(ContentEditor);
    }

    // Cached frozen animations for tab button hover (avoids allocation per hover cycle)
    private static readonly DoubleAnimation FadeIn100ms = CreateFrozenAnim(0, 1, 100);
    private static readonly DoubleAnimation FadeOut100ms = CreateFrozenAnim(1, 0, 100);

    private static DoubleAnimation CreateFrozenAnim(double from, double to, int ms)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
        a.Freeze();
        return a;
    }

    /// <summary>
    /// Animates the Opacity property of a UIElement from one value to another over durationMs.
    /// Uses cached frozen animations for the common 100ms fade in/out pattern.
    /// </summary>
    private static void AnimateOpacity(UIElement element, double from, double to, int durationMs)
    {
        // Use cached animation for common hover pattern
        if (durationMs == 100)
        {
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (from == 0 && to == 1) { element.BeginAnimation(OpacityProperty, FadeIn100ms); return; }
            if (from == 1 && to == 0) { element.BeginAnimation(OpacityProperty, FadeOut100ms); return; }
            // ReSharper restore CompareOfFloatsByEqualityOperator
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs));
        element.BeginAnimation(OpacityProperty, anim);
    }

    private System.Windows.Threading.DispatcherTimer? _collapseTimer;
    private readonly List<UIElement> _pendingCollapses = [];

    /// <summary>
    /// Collapses an element after a short delay (allows fade-out animation to complete).
    /// Reuses a single timer for all pending collapses.
    /// </summary>
    private void DelayedCollapse(UIElement element)
    {
        _pendingCollapses.Add(element);

        if (_collapseTimer is null)
        {
            _collapseTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _collapseTimer.Tick += (_, _) =>
            {
                _collapseTimer.Stop();
                foreach (var el in _pendingCollapses)
                {
                    if (el.Opacity == 0)
                        el.Visibility = Visibility.Collapsed;
                }
                _pendingCollapses.Clear();
            };
        }
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    // ─── Window Title ───────────────────────────────────────────────────

    /// <summary>
    /// Updates the window title based on the current desktop identity.
    /// Delegates to ViewModel for title formatting.
    /// </summary>
    public void UpdateDesktopTitle(string? desktopName, int? desktopIndex)
    {
        ViewModel.UpdateDesktopInfo(desktopName, desktopIndex);
        Title = ViewModel.WindowTitle;
    }

    /// <summary>Shows a save-failure toast for background persistence errors. Event arrives
    /// on a background thread; only the active window for the failure's desktop matters, so
    /// every window showing it is acceptable and simple.</summary>
    private void OnBackgroundWriteFailed(string operationName)
    {
        Dispatcher.InvokeAsync(() =>
            ShowInfoToast(string.Format(Strings.Toast_SaveFailed, operationName)));
    }

    // ─── Window Lifecycle ───────────────────────────────────────────────

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
        _contentSyncTimer?.Stop();
        _undoCoalesceTimer?.Stop();
        _tabScrollTimer?.Stop();
        _findDebounceTimer?.Stop();
        CancelPendingSearch();

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

        // Unsubscribe static events to allow GC of this window
        ThemeService.ThemeChanged -= OnThemeChangedAdornerRefresh;
        VirtualDesktopService.WindowMovedToDesktop -= OnWindowMovedToDesktop;
        BackgroundWriter.WriteFailed -= OnBackgroundWriteFailed;

        // Cancel and dispose pending desktop check
        _misplacedCheckCts?.Cancel();
        _misplacedCheckCts?.Dispose();

        // Stop all timers
        _autosaveService.Stop();
        _checkpointTimer.Stop();
        _stalenessTimer.Stop();
        _contentSyncTimer?.Stop();
        _undoCoalesceTimer?.Stop();
        _tabScrollTimer?.Stop();
        _findDebounceTimer?.Stop();
        _findTextChangeDebounce?.Stop();
        _searchDebounceTimer?.Stop();
        _collapseTimer?.Stop();
        CancelPendingSearch();

        // Synchronous flush — block until save completes (no data loss).
        PersistActiveContentSync();

        // Commit pending deletions before close
        CommitPendingDeletionAsync().GetAwaiter().GetResult();

        // Capture geometry while the HWND is still valid
        var geo = WindowPlacementHelper.CaptureGeometry(this);

        // Fire-and-forget: save geometry to database (process stays alive so this completes)
        _ = SessionStore.SaveWindowGeometryAsync(_desktopGuid, geo);
        StartupSnapshot.UpdateGeometry(_desktopGuid, geo);

        LogService.Info("Window closing for desktop {DesktopGuid} \u2014 geometry saved ({Left},{Top} {Width}x{Height} maximized={IsMaximized})", _desktopGuid, geo.Left, geo.Top, geo.Width, geo.Height);

        // Do NOT set e.Cancel = true — let the window close and be destroyed
    }

    /// <summary>
    /// Synchronously persists the active tab's content to the database if there are unsaved
    /// changes. Gated on the autosave dirty flag (NOT an editor-vs-model compare, which
    /// ThrottleContentSync renders useless). Marks the autosave layer clean so a later flush
    /// on the same close won't re-write. Safe to block on — the write runs no UI callbacks.
    /// </summary>
    private void PersistActiveContentSync()
    {
        FlushContentSync();
        if (_activeTab is null || !_autosaveService.IsDirty) return;

        string content = ContentEditor.Text;
        _activeTab.Content = content;
        _activeTab.CursorPosition = ContentEditor.CaretIndex;
        _activeTab.UpdatedAt = DateTime.Now;
        NoteStore.UpdateNoteContentAsync(_activeTab.Id, content).GetAwaiter().GetResult();
        _autosaveService.MarkClean();
    }

    /// <summary>
    /// Persists note content only (no geometry / no window teardown) for the OS-shutdown path.
    /// Called for every window up front so a tight shutdown budget can't cost note data even if
    /// later per-window close work is cut short.
    /// </summary>
    internal void FlushContentForShutdown() => PersistActiveContentSync();

    // ─── Editor Mouse Helpers ───────────────────────────────────────────

    // System double-click time/rectangle (formerly WinForms SystemInformation.DoubleClickTime /
    // DoubleClickSize, which wrap these same Win32 calls). Read once per process.
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    private static readonly long DoubleClickTimeMs = GetDoubleClickTime();
    private static readonly int DoubleClickWidth = GetSystemMetrics(SM_CXDOUBLECLK);
    private static readonly int DoubleClickHeight = GetSystemMetrics(SM_CYDOUBLECLK);

    private bool _suppressNextEditorMouseUp;
    private int _editorClickStreak;
    private long _lastEditorClickTicks;
    private System.Windows.Point _lastEditorClickPos;

    /// <summary>
    /// Triple-click selects the entire logical line under the cursor (up to the newline,
    /// including wrapped segments). WPF TextBox only handles double-click (word) natively.
    /// Clicks are counted MANUALLY: the TextBox captures the mouse during the double-click
    /// word-selection, and that capture change resets MouseDevice's click counter — so
    /// e.ClickCount never reliably reaches 3 on a TextBox. The selection is re-asserted after
    /// the editor's own input processing and the matching mouse-up is suppressed, so nothing
    /// can collapse it back to a word/caret.
    /// </summary>
    private void ContentEditor_TripleClickSelectLine(object sender, MouseButtonEventArgs e)
    {
        // Manual click-streak tracking using the system double-click time and rectangle.
        var pos = e.GetPosition(ContentEditor);
        long now = Environment.TickCount64;
        bool chained = now - _lastEditorClickTicks <= DoubleClickTimeMs
            && Math.Abs(pos.X - _lastEditorClickPos.X) <= DoubleClickWidth
            && Math.Abs(pos.Y - _lastEditorClickPos.Y) <= DoubleClickHeight;
        _editorClickStreak = chained ? _editorClickStreak + 1 : 1;
        _lastEditorClickTicks = now;
        _lastEditorClickPos = pos;

        if (_editorClickStreak < 3 && e.ClickCount < 3) return;
        _editorClickStreak = 0; // consume — the next quick click starts a fresh streak

        string text = ContentEditor.Text;
        if (text.Length == 0) return;

        int caret = ContentEditor.GetCharacterIndexFromPoint(pos, snapToText: true);
        if (caret < 0) caret = ContentEditor.CaretIndex;
        caret = Math.Clamp(caret, 0, text.Length);

        int lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
        int lineEnd = caret < text.Length ? text.IndexOf('\n', caret) : -1;
        if (lineEnd < 0) lineEnd = text.Length;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--; // exclude CR of CRLF

        int length = lineEnd - lineStart;
        ContentEditor.Select(lineStart, length);
        e.Handled = true;
        _suppressNextEditorMouseUp = true;

        // Re-assert after the editor's own mouse handling (which may run despite e.Handled via
        // class handlers registered with handledEventsToo) so the line selection sticks.
        Dispatcher.BeginInvoke(() =>
        {
            if (ContentEditor.SelectionStart != lineStart || ContentEditor.SelectionLength != length)
                ContentEditor.Select(lineStart, length);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Swallows the mouse-up that follows a triple-click so the editor's up-processing
    /// (caret placement / selection anchoring) cannot collapse the line selection.
    /// </summary>
    private void ContentEditor_TripleClickMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_suppressNextEditorMouseUp) return;
        _suppressNextEditorMouseUp = false;
        if (ContentEditor.IsMouseCaptured)
            ContentEditor.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ─── Autosave & Undo Helpers ────────────────────────────────────────

    /// <summary>
    /// TextChanged handler for autosave debounce trigger and per-keystroke undo snapshots.
    /// Only fires for user-initiated changes (suppressed during programmatic text assignment).
    /// </summary>
    private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _activeTab is null) return;

        string text = ContentEditor.Text;
        int cursor = ContentEditor.CaretIndex;

        // Coalesce undo snapshots to ~one per 600ms of typing instead of a full-content copy on
        // every keystroke — the per-keystroke copy is O(n) and makes large-note editing O(n²).
        ScheduleUndoSnapshot(_activeTab.Id, text, cursor);

        // Throttle model sync — DisplayLabel binding doesn't need per-character updates
        ThrottleContentSync(text);

        _autosaveService.NotifyTextChanged();

        // Start/reset checkpoint timer on user input
        _checkpointTimer.Stop();
        _checkpointTimer.Start();

        // Re-run find highlights if panel is open
        RefreshFindOnTextChange();
    }

    private ScrollViewer? _cachedContentScrollViewer;
    private System.Windows.Threading.DispatcherTimer? _contentSyncTimer;
    private string? _pendingContentSync;

    // ─── Undo snapshot coalescing ───────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _undoCoalesceTimer;
    private long _pendingUndoTabId;
    private string? _pendingUndoText;
    private int _pendingUndoCursor;
    private const int UndoCoalesceMs = 600;

    /// <summary>
    /// Records the latest editor state and ensures a coalescing timer is running. The timer is
    /// deliberately NOT reset on each keystroke, so during a continuous typing burst it fires
    /// periodically (~every 600ms) rather than being pushed out to the end of the burst — giving
    /// reasonable undo granularity while capping snapshot frequency. The tab id is captured here so
    /// a late tick can never push a snapshot onto a tab the user has since switched away from.
    /// </summary>
    private void ScheduleUndoSnapshot(long tabId, string text, int cursor)
    {
        _pendingUndoTabId = tabId;
        _pendingUndoText = text;
        _pendingUndoCursor = cursor;

        if (_undoCoalesceTimer is null)
        {
            _undoCoalesceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UndoCoalesceMs)
            };
            _undoCoalesceTimer.Tick += (_, _) => FlushPendingUndoSnapshot();
        }
        if (!_undoCoalesceTimer.IsEnabled)
            _undoCoalesceTimer.Start();
    }

    /// <summary>
    /// Pushes any pending coalesced undo snapshot immediately and stops the timer. Call before
    /// undo/redo, replace, save, and tab switch so the latest typed state is captured and no stale
    /// tick fires afterward (which would corrupt the redo chain or target the wrong tab).
    /// </summary>
    private void FlushPendingUndoSnapshot()
    {
        _undoCoalesceTimer?.Stop();
        if (_pendingUndoText is null) return;
        UndoManager.Instance.PushSnapshot(_pendingUndoTabId, _pendingUndoText, _pendingUndoCursor);
        _pendingUndoText = null;
    }

    /// <summary>
    /// Defers <c>_activeTab.Content</c> assignment (and its PropertyChanged / DisplayLabel
    /// recomputation) so it fires at most once per ~100 ms instead of on every keystroke.
    /// </summary>
    private void ThrottleContentSync(string text)
    {
        _pendingContentSync = text;

        if (_contentSyncTimer is not null && _contentSyncTimer.IsEnabled) return;

        if (_contentSyncTimer is null)
        {
            _contentSyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _contentSyncTimer.Tick += (_, _) =>
            {
                _contentSyncTimer.Stop();
                if (_activeTab is not null && _pendingContentSync is not null)
                {
                    _activeTab.Content = _pendingContentSync;
                    _pendingContentSync = null;
                }
            };
        }
        _contentSyncTimer.Start();
    }

    /// <summary>
    /// Flushes any pending throttled content sync immediately.
    /// Call before operations that need <c>_activeTab.Content</c> to be current
    /// (tab switch, save, export).
    /// </summary>
    private void FlushContentSync()
    {
        _contentSyncTimer?.Stop();
        if (_activeTab is not null && _pendingContentSync is not null)
        {
            _activeTab.Content = _pendingContentSync;
            _pendingContentSync = null;
        }
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
        FlushContentSync();
        FlushPendingUndoSnapshot(); // capture any coalesced typing and stop the sampler first

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

        // Suppressing TextChanged also suppresses the autosave trigger, so mark dirty explicitly —
        // otherwise an undo as the last action is never persisted and silently reverts on reload.
        _autosaveService.NotifyTextChanged();

        UpdateToolbarState(); // refresh undo/redo button states
        RefreshFindOnTextChange(); // update find highlights after undo
    }

    /// <summary>
    /// Performs redo by advancing to next snapshot in the per-tab UndoStack.
    /// </summary>
    private void PerformRedo()
    {
        if (_activeTab is null) return;
        FlushContentSync();
        FlushPendingUndoSnapshot(); // stop the sampler so no stale tick corrupts the redo chain
        var entry = UndoManager.Instance.Redo(_activeTab.Id);
        if (entry is null) return;

        _suppressTextChanged = true;
        _activeTab.Content = entry.Value.Content;
        ContentEditor.Text = entry.Value.Content;
        ContentEditor.CaretIndex = Math.Min(entry.Value.CursorPosition, entry.Value.Content.Length);
        _suppressTextChanged = false;

        // Mark dirty explicitly — the suppressed TextChanged skips the autosave trigger.
        _autosaveService.NotifyTextChanged();

        UpdateToolbarState(); // refresh undo/redo button states
        RefreshFindOnTextChange(); // update find highlights after redo
    }

    /// <summary>
    /// Contextual save: writes directly to file for file-backed tabs,
    /// shows Save As dialog for regular notes.
    /// </summary>
    private void SaveNote()
    {
        if (_activeTab is null) return;
        FlushContentSync();

        if (_activeTab.IsFileBacked)
        {
            try
            {
                System.IO.File.WriteAllText(_activeTab.FilePath!, _activeTab.Content, Utf8Bom);
            }
            catch (Exception ex)
            {
                LogService.Error("Save to file failed: {FilePath}", _activeTab.FilePath!, ex);
                ShowInfoToast(string.Format(Strings.Save_Failed, System.IO.Path.GetFileName(_activeTab.FilePath!)));
            }
        }
        else
        {
            ShowSaveAsDialog();
        }
    }

    /// <summary>
    /// Always shows a Save As dialog. After saving, the tab becomes file-backed.
    /// </summary>
    private void SaveAsDialog()
    {
        if (_activeTab is null) return;
        FlushContentSync();
        ShowSaveAsDialog();
    }

    private void ShowSaveAsDialog()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = _activeTab!.IsFileBacked
                ? System.IO.Path.GetFileName(_activeTab.FilePath!)
                : MainWindowViewModel.GetDefaultFilename(_activeTab)
        };

        if (_activeTab.IsFileBacked)
            dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_activeTab.FilePath!);
        else if (!string.IsNullOrEmpty(_lastSaveDirectory))
            dialog.InitialDirectory = _lastSaveDirectory;

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, _activeTab.Content, Utf8Bom);
        }
        catch (Exception ex)
        {
            LogService.Error("Save to file failed: {FilePath}", dialog.FileName, ex);
            ShowInfoToast(string.Format(Strings.Save_Failed, System.IO.Path.GetFileName(dialog.FileName)));
            return;
        }

        _lastSaveDirectory = System.IO.Path.GetDirectoryName(dialog.FileName);
        _activeTab.FilePath = dialog.FileName;
        _activeTab.Name = System.IO.Path.GetFileName(dialog.FileName);
        _ = NoteStore.UpdateNoteFilePathAndNameAsync(_activeTab.Id, dialog.FileName, _activeTab.Name);
        UpdateToolbarState();
    }
}
