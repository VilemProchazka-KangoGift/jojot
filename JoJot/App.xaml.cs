using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using JoJot.Interop;
using JoJot.Models;
using JoJot.Services;
using Serilog.Events;

namespace JoJot;

/// <summary>
/// Application entry point.
/// Orchestrates the full startup sequence: mutex guard, logging, database initialization,
/// IPC server start, window creation, startup timing, and background migrations.
/// ShutdownMode is OnExplicitShutdown — the process stays alive when all windows are closed.
/// Per-desktop window registry maps each virtual desktop to its own MainWindow instance.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Held for the full process lifetime to satisfy the single-instance mutex.
    /// Must be kept here (not a local variable) to prevent GC collection between checks.
    /// </summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>
    /// Releases the single-instance mutex so a restarted process can acquire it.
    /// </summary>
    internal static void ReleaseSingleInstanceMutex()
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    /// <summary>
    /// Cancellation source signaled on application exit to stop the IPC server and other background work.
    /// </summary>
    private readonly CancellationTokenSource _appShutdownCts = new();

    /// <summary>
    /// Per-desktop window registry. Key is desktop GUID string (case-insensitive).
    /// Windows are added on creation and removed on Closed event.
    /// </summary>
    private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Async void startup handler. The ENTIRE body after exception-handler setup is wrapped in
    /// try/catch so no exceptions can escape this async void entry point.
    /// </summary>
    private async void OnAppStartup(object sender, StartupEventArgs e)
    {
        // Global unhandled exception handlers — set up before mutex so any crash in startup is captured.
        // Degrade gracefully, never show unhandled exception dialogs.
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogService.Error("Unhandled exception", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogService.Error("Unhandled dispatcher exception", args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogService.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            var sw = Stopwatch.StartNew();

            // Initialize logging
            var appDir = AppEnvironment.AppDataDirectory;
            Directory.CreateDirectory(appDir);
            LogService.Initialize(appDir);
            LogService.Info("JoJot starting...");

            // Single-instance enforcement via global mutex
            bool createdNew = IpcService.TryAcquireMutex(out var mutex);
            _singleInstanceMutex = mutex;
            GC.KeepAlive(_singleInstanceMutex);

            if (!createdNew)
            {
                // Second instance: query current desktop via COM so the first instance
                // creates a window on the desktop the user is actually on, not wherever
                // its cached CurrentDesktopGuid points (which may already be stale if
                // Windows activated the first instance's window on another desktop).
                string? senderDesktopGuid = null;
                try
                {
                    VirtualDesktopInterop.Initialize();
                    var (id, _, _) = VirtualDesktopInterop.GetCurrentDesktop();
                    senderDesktopGuid = id.ToString();
                }
                catch (Exception ex)
                {
                    LogService.Warn("Second instance: failed to query desktop — first instance will use cached GUID: {Error}", ex.Message);
                }

                LogService.Info("Second instance detected — sending new-tab command (desktop={DesktopGuid})", senderDesktopGuid);
                await IpcService.SendCommandAsync(new NewTabCommand(DesktopGuid: senderDesktopGuid));
                Environment.Exit(0);
                return;
            }

            // Explicit shutdown mode — process stays alive when all windows are closed
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Handle OS shutdown/logoff so JoJot does not block Windows session end
            SessionEnding += OnSessionEnding;

            // Open database and ensure schema
            var dbPath = AppEnvironment.DatabasePath;
            await DatabaseCore.OpenAsync(dbPath);
            await DatabaseCore.EnsureSchemaAsync();

            // Integrity check with corruption recovery (before EF warmup to avoid wasted work)
            bool healthy = await DatabaseCore.VerifyIntegrityAsync();
            if (!healthy)
            {
                LogService.Error("Database integrity check failed — initiating corruption recovery");
                await DatabaseCore.HandleCorruptionAsync(dbPath);
            }

            // Warm up EF model after integrity is confirmed (must complete before first real query)
            await DatabaseCore.WarmupModelAsync();

            // Restore log level from preferences
            var savedLogLevel = await PreferenceStore.GetPreferenceAsync("log_level").ConfigureAwait(false);
            var parsedLevel = ParseLogLevel(savedLogLevel);
            if (parsedLevel is not null)
            {
                LogService.SetMinimumLevel(parsedLevel.Value);
                LogService.Info("Log level restored from preferences: {LogLevel}", parsedLevel.Value);
            }

            // Initialize language (sets CurrentUICulture before any UI is created)
            await LanguageService.InitializeAsync();

            // Initialize theme and virtual desktop detection in parallel (independent of each other)
            var themeTask = ThemeService.InitializeAsync();
            await VirtualDesktopService.InitializeAsync();
            await themeTask;
            if (VirtualDesktopService.IsAvailable)
            {
                LogService.Info("Virtual desktop: {DesktopGuid} ({DesktopName})", VirtualDesktopService.CurrentDesktopGuid, VirtualDesktopService.CurrentDesktopName);
            }
            else
            {
                LogService.Info("Virtual desktop: fallback mode (single-notepad)");
            }

            // Diagnostic telemetry — foreground hook + monotonic clock
            DesktopTelemetry.Initialize();

            // Subscribe to desktop notifications (must happen on UI thread for COM callbacks)
            VirtualDesktopService.SubscribeNotifications();

            // Session matching — associate saved sessions with live desktops
            await VirtualDesktopService.MatchSessionsAsync();
            await VirtualDesktopService.EnsureCurrentDesktopSessionAsync();

            // Resolve pending moves from crash recovery
            await ResolvePendingMovesAsync();

            // Auto-delete old notes if configured
            await RunAutoDeleteAsync();

            // Welcome tab on first launch
            await StartupService.CreateWelcomeTabIfFirstLaunch();

            // Start IPC server
            IpcService.StartServer(HandleIpcCommand, _appShutdownCts.Token);

            // Run pending migrations before window restore.
            // Migrations must complete before GetWindowGeometryAsync (which reads
            // columns added by migrations like window_state).
            await StartupService.RunBackgroundMigrationsAsync();

            // Create and show window for current desktop
            var currentDesktopGuid = VirtualDesktopService.CurrentDesktopGuid;
            var mainWindow = await CreateWindowForDesktop(currentDesktopGuid);

            // Set orphan badge after session matching
            mainWindow.UpdateOrphanBadge();

            // Wire desktop event handlers
            // Live title updates when desktop is renamed in Windows Settings
            VirtualDesktopService.DesktopRenamed += (desktopGuid, newName) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (_windows.TryGetValue(desktopGuid, out var w))
                        {
                            var desktops = VirtualDesktopService.GetAllDesktops();
                            var info = desktops.FirstOrDefault(d =>
                                d.Id.ToString().Equals(desktopGuid, StringComparison.OrdinalIgnoreCase));
                            w.UpdateDesktopTitle(newName, info?.Index);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Error handling desktop rename event", ex);
                    }
                });
            };

            // Redirect: when Windows pulls the user to a JoJot desktop from a non-JoJot desktop
            // (e.g. taskbar click), switch back and create a window on the origin desktop.
            VirtualDesktopService.CurrentDesktopChanged += (oldGuid, newGuid) =>
            {
                LogService.Info("Desktop switched: {OldGuid} -> {NewGuid}", oldGuid, newGuid);

                Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        bool hasOld = _windows.ContainsKey(oldGuid);
                        bool hasNew = _windows.ContainsKey(newGuid);
                        bool crossDesktop = DesktopSwitchDetector.WasCrossDesktopActivation(newGuid);
                        bool kbNav = DesktopSwitchDetector.IsRecentKeyboardNavigation;
                        bool cooldown = DateTime.UtcNow < _redirectCooldownUntil;
                        bool verdict = ShouldRedirect(DateTime.UtcNow, _redirectCooldownUntil,
                            hasOld, hasNew, crossDesktop, kbNav);

                        DesktopTelemetry.LogSnapshot("redirect-decision",
                            "old={OldGuid} new={NewGuid} hasOld={HasOld} hasNew={HasNew} crossDesktop={CrossDesktop} kbNav={KbNav} cooldown={Cooldown} verdict={Verdict}",
                            oldGuid, newGuid, hasOld, hasNew, crossDesktop, kbNav, cooldown, verdict);

                        if (!verdict)
                        {
                            return;
                        }

                        LogService.Info("Redirect: creating window on {DesktopGuid} and switching back", oldGuid);
                        _redirectCooldownUntil = DateTime.UtcNow.AddSeconds(3);
                        await CreateWindowForDesktop(oldGuid);
                        VirtualDesktopService.TrySwitchToDesktop(oldGuid);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Error handling desktop switch redirect", ex);
                    }
                });
            };

            // Log startup timing
            sw.Stop();
            LogService.Info("Startup complete in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            LogService.Error("Fatal error during application startup", ex);
        }
    }

    // ─── Window Factory ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a new MainWindow for the given desktop, restores its geometry,
    /// sets its title, registers it in the window dictionary, and shows it.
    /// </summary>
    private async Task<MainWindow> CreateWindowForDesktop(string desktopGuid)
    {
        var window = new MainWindow(desktopGuid);

        // Register cleanup on close — Closed fires after window is destroyed
        window.Closed += (_, _) =>
        {
            _windows.Remove(desktopGuid);
            LogService.Info("Window removed from registry for desktop {DesktopGuid} ({WindowCount} windows remaining)", desktopGuid, _windows.Count);
        };

        // Restore geometry (must happen before Show for WindowStartupLocation to work)
        var geo = await SessionStore.GetWindowGeometryAsync(desktopGuid);
        WindowPlacementHelper.ApplyGeometry(window, geo);

        // Set title — look up this specific desktop by GUID, not "current"
        if (VirtualDesktopService.IsAvailable)
        {
            var desktops = VirtualDesktopService.GetAllDesktops();
            var info = desktops.FirstOrDefault(d =>
                d.Id.ToString().Equals(desktopGuid, StringComparison.OrdinalIgnoreCase));
            window.UpdateDesktopTitle(info?.Name, info?.Index);
        }
        else
        {
            window.UpdateDesktopTitle(null, null);
        }

        _windows[desktopGuid] = window;
        window.Show();

        // Apply geometry via SetWindowPlacement AFTER Show (needs HWND)
        if (geo is not null)
        {
            WindowPlacementHelper.ApplyGeometry(window, geo);
        }

        // Ensure window lands on the target desktop (e.g. IPC from a different desktop)
        if (VirtualDesktopService.IsAvailable)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            VirtualDesktopService.TryMoveWindowToDesktop(hwnd, desktopGuid);
        }

        // Load tabs from database after window is shown
        await window.LoadTabsAsync();

        // Initialize global hotkey after window has HWND.
        // Only register for the first window (hotkey is system-wide, one registration suffices).
        if (_windows.Count <= 1)
        {
            await HotkeyService.InitializeAsync(window, () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Global hotkey action: activate window and create a new note
                        var currentGuid = VirtualDesktopService.CurrentDesktopGuid;
                        if (_windows.TryGetValue(currentGuid, out var w))
                        {
                            w.WindowState = WindowState.Normal;
                            WindowActivationHelper.ActivateWindow(w);
                            _ = w.CreateNewTabAsync();
                        }
                        else
                        {
                            // No window for this desktop — create one (auto-creates first tab)
                            _ = CreateWindowForDesktop(currentGuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Error handling global hotkey", ex);
                    }
                });
            });
        }

        // Initialize preferences panel data
        await window.InitializePreferencesAsync();

        WindowActivationHelper.ActivateWindow(window);

        // Show recovery toast if crash recovery happened
        if (_pendingRecoveryToast)
        {
            _pendingRecoveryToast = false;
            window.ShowInfoToast(JoJot.Resources.Strings.Toast_Recovery);
        }

        return window;
    }

    // ─── IPC Command Routing ────────────────────────────────────────────

    /// <summary>
    /// Routes incoming IPC commands to the correct desktop's window.
    /// Resolves the current desktop GUID at handle time (live state, not send time).
    /// Always called on the UI Dispatcher (IpcService ensures this).
    /// Entire body is wrapped in try/catch since this is an async void handler.
    /// </summary>
    private async void HandleIpcCommand(IpcMessage message)
    {
        try
        {
            var desktopGuid = VirtualDesktopService.CurrentDesktopGuid;

            switch (message)
            {
                case ActivateCommand:
                    LogService.Info("IPC: activate — desktop {DesktopGuid}", desktopGuid);
                    if (_windows.TryGetValue(desktopGuid, out var existingWindow))
                    {
                        WindowActivationHelper.ActivateWindow(existingWindow);
                    }
                    else
                    {
                        await CreateWindowForDesktop(desktopGuid);
                    }
                    break;

                case NewTabCommand ntc:
                    // Prefer the sender's desktop GUID (queried by second instance via COM)
                    var targetDesktop = ResolveTargetDesktop(ntc.DesktopGuid, desktopGuid);
                    LogService.Info("IPC: new-tab — target desktop {TargetDesktop} (sender={SenderDesktop}, cached={CachedDesktop})", targetDesktop, ntc.DesktopGuid, desktopGuid);
                    if (_windows.TryGetValue(targetDesktop, out var tabWindow))
                    {
                        VirtualDesktopService.TrySwitchToDesktop(targetDesktop);
                        WindowActivationHelper.ActivateWindow(tabWindow);
                        tabWindow.RequestNewTab();
                    }
                    else
                    {
                        var newWindow = await CreateWindowForDesktop(targetDesktop);
                        VirtualDesktopService.TrySwitchToDesktop(targetDesktop);
                        newWindow.RequestNewTab();
                    }
                    break;

                case ShowDesktopCommand showCmd:
                    LogService.Info("IPC: show-desktop {DesktopGuid}", showCmd.DesktopGuid);
                    break;

                default:
                    LogService.Warn("IPC: unknown command type {CommandType}", message.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error handling IPC command", ex);
        }
    }

    /// <summary>
    /// Returns all open JoJot windows. Used by Exit menu to flush all before termination.
    /// </summary>
    public List<MainWindow> GetAllWindows() => [.. _windows.Values];

    // ─── Extracted Pure Logic ───────────────────────────────────────────

    /// <summary>
    /// Parses a stored log level preference string into a LogEventLevel.
    /// Returns null if the value is null or not a valid enum member.
    /// Case-insensitive.
    /// </summary>
    internal static LogEventLevel? ParseLogLevel(string? saved)
    {
        if (saved is not null && Enum.TryParse<LogEventLevel>(saved, true, out var level))
            return level;
        return null;
    }

    /// <summary>
    /// Determines whether a desktop-switch redirect should occur.
    /// Requires ALL conditions:
    ///   1. Cooldown expired
    ///   2. Cross-desktop activation detected (positive signal from WM_ACTIVATE)
    ///   3. No recent keyboard navigation (Alt+Tab, Ctrl+Win+Arrow, Win+Tab)
    ///   4. Switching FROM no-window TO has-window
    /// The cross-desktop activation requirement prevents redirecting on deliberate
    /// desktop switches (Task View, Ctrl+Win+Arrow, touchpad gestures) where the
    /// COM callback fires before WM_ACTIVATE.
    /// </summary>
    internal static bool ShouldRedirect(DateTime now, DateTime cooldownUntil,
        bool hasOldWindow, bool hasNewWindow,
        bool crossDesktopActivation, bool isKeyboardNavigation)
    {
        if (now < cooldownUntil) return false;
        if (!crossDesktopActivation) return false;
        if (isKeyboardNavigation) return false;
        return !hasOldWindow && hasNewWindow;
    }

    /// <summary>
    /// Resolves the target desktop for an IPC new-tab command.
    /// Prefers the sender's desktop GUID (queried by the second instance via COM at launch time)
    /// over the first instance's cached GUID (which may be stale if Windows has already activated
    /// the first instance's window on a different desktop before the IPC arrives).
    /// </summary>
    internal static string ResolveTargetDesktop(string? senderDesktopGuid, string cachedDesktopGuid)
    {
        return senderDesktopGuid ?? cachedDesktopGuid;
    }

    /// <summary>
    /// Determines whether a pending move should be recovered (migrated).
    /// A move is recoverable if toDesktop is non-null and differs from fromDesktop (case-insensitive).
    /// </summary>
    internal static bool ShouldRecoverMove(string? toDesktop, string fromDesktop)
    {
        return toDesktop is not null && !fromDesktop.Equals(toDesktop, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens a new window for an orphaned session GUID.
    /// The orphan already has a desktop_guid — we open a window bound to it.
    /// </summary>
    public async Task OpenWindowForOrphanAsync(string orphanGuid)
    {
        if (_windows.TryGetValue(orphanGuid, out var existing))
        {
            WindowActivationHelper.ActivateWindow(existing);
            return;
        }
        await CreateWindowForDesktop(orphanGuid);
    }

    // ─── Window Drag Helper Methods ─────────────────────────────────────

    /// <summary>
    /// Whether a recovery toast should be shown on the next window creation.
    /// </summary>
    private bool _pendingRecoveryToast;

    /// <summary>
    /// Checks if a window exists for the given desktop GUID (used for merge checks).
    /// </summary>
    public bool HasWindowForDesktop(string desktopGuid)
    {
        return _windows.ContainsKey(desktopGuid);
    }

    /// <summary>
    /// Updates the window registry when a window is reparented to a new desktop.
    /// Removes the old GUID key and adds the new one pointing to the same MainWindow.
    /// </summary>
    public void ReparentWindow(string oldGuid, string newGuid)
    {
        if (_windows.TryGetValue(oldGuid, out var window))
        {
            _windows.Remove(oldGuid);
            _windows[newGuid] = window;
            LogService.Info("Window reparented in registry: {OldGuid} -> {NewGuid}", oldGuid, newGuid);
        }
    }

    /// <summary>
    /// Tells the target window to reload its tabs from database (after a merge).
    /// </summary>
    public void ReloadWindowTabs(string desktopGuid)
    {
        if (_windows.TryGetValue(desktopGuid, out var window))
        {
            _ = window.LoadTabsAsync();
        }
    }

    /// <summary>
    /// Shows a merge completion toast on the target window.
    /// </summary>
    public void ShowMergeToast(string desktopGuid, int tabCount, string fromDesktopName)
    {
        if (_windows.TryGetValue(desktopGuid, out var window))
        {
            window.ShowInfoToast(string.Format(JoJot.Resources.Strings.Toast_Merged, tabCount, fromDesktopName));
        }
    }

    /// <summary>
    /// Resolves any pending_moves rows left by a crash during a window drag.
    /// Reads all pending moves and attempts to restore windows to their origin desktop.
    /// Called during startup after session matching, before window creation.
    /// </summary>
    private async Task ResolvePendingMovesAsync()
    {
        var moves = await PendingMoveStore.GetPendingMovesAsync();
        if (moves.Count == 0)
        {
            LogService.Info("Pending moves check: none found");
            return;
        }

        LogService.Info("Pending moves check: {MoveCount} unresolved move(s) found — recovering", moves.Count);
        bool recovered = false;

        foreach (var move in moves)
        {
            LogService.Info("Recovering pending move: window={WindowId}, from={FromDesktop}, to={ToDesktop}", move.WindowId, move.FromDesktop, move.ToDesktop);
            if (ShouldRecoverMove(move.ToDesktop, move.FromDesktop))
            {
                try
                {
                    await NoteStore.MigrateTabsPreservePinsAsync(move.ToDesktop!, move.FromDesktop);
                    recovered = true;
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to recover pending move {MoveId}: {ErrorMessage}", move.Id, ex.Message);
                }
            }
        }

        await PendingMoveStore.DeleteAllPendingMovesAsync();

        if (recovered)
        {
            _pendingRecoveryToast = true;
        }
        LogService.Info("Pending moves check: recovery complete");
    }

    /// <summary>
    /// Reads the auto_delete_days preference and deletes unpinned notes older than that many days.
    /// Called once during startup before windows are created.
    /// </summary>
    private static async Task RunAutoDeleteAsync()
    {
        var saved = await PreferenceStore.GetPreferenceAsync("auto_delete_days").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(saved) || !int.TryParse(saved, out int days) || days <= 0)
            return;

        var cutoff = DateTime.Now.AddDays(-days);
        int deleted = await NoteStore.DeleteOldNotesAsync(cutoff);
        if (deleted > 0)
            LogService.Info("Startup auto-delete: {DeletedCount} note(s) older than {Days} days removed", deleted, days);
    }

    /// <summary>
    /// Handles Windows OS shutdown or user logoff.
    /// Flushes all open windows synchronously and calls Shutdown() so OnExit
    /// can clean up IPC, hotkeys, VirtualDesktop, ThemeService, database, and mutex.
    /// Does NOT cancel the session (e.Cancel remains false) — Windows must proceed.
    /// </summary>
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        LogService.Info("Windows session ending (reason: {Reason}) -- flushing and shutting down...", e.ReasonSessionEnding);

        // Snapshot the window list — FlushAndClose triggers Closed which removes entries from _windows
        var windows = _windows.Values.ToList();
        foreach (var window in windows)
        {
            window.FlushAndClose();
        }

        Shutdown();
    }

    /// <summary>
    /// Called when the application exits. Cancels the IPC server, flushes the database,
    /// and releases the single-instance mutex. Disposes the shutdown CancellationTokenSource.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Info("JoJot shutting down...");

        _appShutdownCts.Cancel();
        IpcService.StopServer();
        HotkeyService.Shutdown();
        DesktopSwitchDetector.Shutdown();
        DesktopTelemetry.Shutdown();
        VirtualDesktopService.Shutdown();
        ThemeService.Shutdown();

        // Synchronous close in exit path — no async available here
        DatabaseCore.CloseAsync().GetAwaiter().GetResult();

        ReleaseSingleInstanceMutex();

        _appShutdownCts.Dispose();

        LogService.Shutdown();

        base.OnExit(e);
    }
}
