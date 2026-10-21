using System.Diagnostics;
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

    /// <summary>Stores the mutex acquired by Program.Main for the process lifetime.</summary>
    internal static void SetSingleInstanceMutex(Mutex mutex)
    {
        _singleInstanceMutex = mutex;
        GC.KeepAlive(_singleInstanceMutex);
    }

    /// <summary>
    /// Re-acquires the single-instance mutex after a zombie first instance was killed, so this
    /// process can continue as the primary. Returns false if ownership could not be obtained
    /// (another instance won the race), in which case the caller should exit.
    /// </summary>
    internal static bool TryReacquireMutexAsPrimary()
    {
        try
        {
            _singleInstanceMutex?.Dispose();
            var mutex = new Mutex(true, IpcService.MutexName, out bool createdNew);
            if (!createdNew)
            {
                // The named mutex still exists (the killed process may have abandoned it) — try to
                // take ownership. An abandoned mutex grants ownership via AbandonedMutexException.
                try { createdNew = mutex.WaitOne(TimeSpan.FromSeconds(1)); }
                catch (AbandonedMutexException) { createdNew = true; }
            }
            _singleInstanceMutex = mutex;
            GC.KeepAlive(_singleInstanceMutex);
            return createdNew;
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to re-acquire single-instance mutex after zombie kill", ex);
            return false;
        }
    }

    /// <summary>Tracks the previous phase's elapsed time for startup delta logging.</summary>
    private static long _lastPhaseMs;

    /// <summary>Startup stopwatch, started at the top of OnAppStartup.</summary>
    private static Stopwatch _startupStopwatch = new();

    /// <summary>
    /// True only while OnAppStartup runs. CreateWindowShell / CompleteWindowInitAsync are also
    /// used later (IPC/hotkey windows), where startup phase logging must stay silent.
    /// </summary>
    private static bool _startupPhaseLoggingActive;

    /// <summary>Logs one startup phase with cumulative and delta milliseconds.</summary>
    private static void LogPhase(string phase)
    {
        if (!_startupPhaseLoggingActive) return;
        long now = _startupStopwatch.ElapsedMilliseconds;
        LogService.Info("Startup phase {Phase} at {TotalMs}ms (+{DeltaMs}ms)", phase, now, now - _lastPhaseMs);
        _lastPhaseMs = now;
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
            _startupStopwatch = Stopwatch.StartNew();
            _startupPhaseLoggingActive = true;

            // Window title + session matching share one COM desktop enumeration during startup.
            VirtualDesktopService.CacheDesktopsDuringStartup = true;

            // UI-thread guard: DatabaseCore logs a one-time warning per caller for DB work on the dispatcher.
            DatabaseCore.IsUiThread = () => Dispatcher.CheckAccess();

            // Explicit shutdown mode — process stays alive when all windows are closed
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Handle OS shutdown/logoff so JoJot does not block Windows session end
            SessionEnding += OnSessionEnding;

            // Kick off the entire DB pipeline immediately; it runs while the window appears.
            var dbInitTask = InitializeDatabaseAsync();

            // Fast path: snapshot seeds the preference cache so language/theme resolve instantly.
            bool haveSnapshot = StartupSnapshot.TryLoad();
            if (haveSnapshot)
            {
                PreferenceStore.PreloadProvisional(StartupSnapshot.Preferences);
            }
            else
            {
                // First launch (or lost snapshot): preferences must come from the DB before any UI.
                await dbInitTask;
            }
            LogPhase("db-kickoff+snapshot");

            var savedLogLevel = await PreferenceStore.GetPreferenceAsync("log_level");
            var parsedLevel = ParseLogLevel(savedLogLevel);
            if (parsedLevel is not null)
            {
                LogService.SetMinimumLevel(parsedLevel.Value);
            }

            await LanguageService.InitializeAsync();   // reads "language" from cache — instant on fast path
            await ThemeService.InitializeAsync();       // reads "theme" from cache — instant on fast path
            await VirtualDesktopService.InitializeAsync();
            VirtualDesktopService.SubscribeNotifications();
            LogPhase("prefs+language+theme+vd-init");
            if (VirtualDesktopService.IsAvailable)
            {
                LogService.Info("Virtual desktop: {DesktopGuid} ({DesktopName})", VirtualDesktopService.CurrentDesktopGuid, VirtualDesktopService.CurrentDesktopName);
            }
            else
            {
                LogService.Info("Virtual desktop: fallback mode (single-notepad)");
            }

            // Window appears NOW — before the database is ready on the fast path.
            var currentDesktopGuid = VirtualDesktopService.CurrentDesktopGuid;
            var mainWindow = CreateWindowShell(currentDesktopGuid);
            LogPhase("window-shell-shown");

            await dbInitTask;
            LogPhase("db-ready");

            // Reconcile provisional (snapshot) preferences against database truth.
            var dbPrefs = await PreferenceStore.InitializeCacheAsync();
            ReconcileSnapshotPreferences(dbPrefs);
            LogPhase("prefs-cache");

            // ── Critical path: only what decides WHICH notes this desktop shows, then load them. ──

            // Session matching can re-home a stored session onto this desktop (Tier 2/3, when the
            // desktop's GUID changed). Only then must it run before the load — a desktop whose GUID
            // already has a session (the normal start) defers matching until the editor is live.
            bool matchedBeforeLoad = false;
            if (VirtualDesktopService.IsAvailable
                && !await SessionStore.SessionExistsAsync(currentDesktopGuid))
            {
                await VirtualDesktopService.MatchSessionsAsync();
                matchedBeforeLoad = true;
                LogPhase("session-match");
            }

            // Keep the app_state row present for this desktop before any geometry save (INSERT OR IGNORE).
            await VirtualDesktopService.EnsureCurrentDesktopSessionAsync();

            // Schema migrations add columns the note query selects — must precede the load. A single
            // PRAGMA read when the schema stamp is current (every start but the first after an upgrade).
            await StartupService.RunBackgroundMigrationsAsync();

            // Auto-delete (only does DB work when enabled) and the O(1) welcome check stay ahead of the
            // load: deleting a note the user may already be typing in, or seeding the welcome note
            // after the empty-desktop auto-tab exists, would both be wrong.
            await RunAutoDeleteAsync();
            await StartupService.CreateWelcomeTabIfFirstLaunch();
            LogPhase("session-ensure+migrations+autodelete+welcome");

            // Tabs load, editor enabled and focused. Logs "tabs-loaded".
            await CompleteWindowInitAsync(mainWindow, currentDesktopGuid);

            // Start IPC server
            IpcService.StartServer(HandleIpcCommand, _appShutdownCts.Token);

            // Register the global hotkey once, on a dedicated message-only window owned by the app
            // (not any MainWindow) so it keeps working after all windows are closed.
            await HotkeyService.InitializeAsync(OnGlobalHotkeyPressed);
            LogPhase("ipc+hotkey");

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

            VirtualDesktopService.CurrentDesktopChanged += (oldGuid, newGuid) =>
                LogService.Info("Desktop switched: {OldGuid} -> {NewGuid}", oldGuid, newGuid);

            // ── Deferred tail: the editor is already live; none of this decides what it shows now. ──
            await RunDeferredStartupWorkAsync(mainWindow, matchedBeforeLoad);

            // Log startup timing
            _startupStopwatch.Stop();
            LogService.Info("Tab containers realized during startup: {Count}", JoJot.MainWindow.TabContainersLoadedCount);
            LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds);
            _startupPhaseLoggingActive = false;
        }
        catch (Exception ex)
        {
            _startupPhaseLoggingActive = false;
            LogService.Error("Fatal error during application startup", ex);
        }
        finally
        {
            // Startup is over (or failed): GetAllDesktops goes back to live COM enumeration.
            VirtualDesktopService.CacheDesktopsDuringStartup = false;
        }
    }

    /// <summary>
    /// Startup work that runs after the editor is live: session matching (unless it had to run
    /// before the load), crash-recovery of pending moves, the orphan badge and recovery toast.
    /// If recovery moved notes, the window reloads its tabs (merge-preserving, focus untouched).
    /// Failures are logged and never undo the already-usable window.
    /// </summary>
    private async Task RunDeferredStartupWorkAsync(MainWindow mainWindow, bool matchedBeforeLoad)
    {
        try
        {
            if (!matchedBeforeLoad)
            {
                await VirtualDesktopService.MatchSessionsAsync();
                LogPhase("session-match");
            }

            bool recovered = await ResolvePendingMovesAsync();
            LogPhase("recovery");

            mainWindow.UpdateOrphanBadge();

            if (recovered)
            {
                // Recovery migrated notes into/out of this desktop after its tabs were shown.
                await mainWindow.LoadTabsAsync();
                mainWindow.ShowInfoToast(JoJot.Resources.Strings.Toast_Recovery);
                LogPhase("recovery-reload");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Deferred startup work failed", ex);
        }
    }

    // ─── Window Factory ─────────────────────────────────────────────────

    /// <summary>
    /// Creates, positions, titles, registers, and shows a MainWindow for the given desktop.
    /// Uses ONLY snapshot geometry — no database access — so it can run before the DB is open.
    /// </summary>
    private MainWindow CreateWindowShell(string desktopGuid)
    {
        var window = new MainWindow(desktopGuid);
        LogPhase("window-ctor");

        window.Closed += (_, _) =>
        {
            _windows.Remove(desktopGuid);
            LogService.Info("Window removed from registry for desktop {DesktopGuid} ({WindowCount} windows remaining)", desktopGuid, _windows.Count);
        };

        var geo = StartupSnapshot.GetGeometry(desktopGuid);
        WindowPlacementHelper.ApplyGeometry(window, geo);

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
        LogPhase("window-shown");

        if (geo is not null)
        {
            WindowPlacementHelper.ApplyGeometry(window, geo); // SetWindowPlacement needs the HWND
        }

        if (VirtualDesktopService.IsAvailable)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            VirtualDesktopService.TryMoveWindowToDesktop(hwnd, desktopGuid);
        }
        LogPhase("window-moved");

        return window;
    }

    /// <summary>
    /// Completes DB-dependent window setup: geometry fallback for desktops the snapshot has
    /// never seen, preferences panel (font size), activation, then tab loading — last, so the
    /// load's editor focus is not undone by ActivateWindow's window.Focus().
    /// Requires the database to be open.
    /// </summary>
    private async Task CompleteWindowInitAsync(MainWindow window, string desktopGuid)
    {
        if (StartupSnapshot.GetGeometry(desktopGuid) is null)
        {
            var geo = await SessionStore.GetWindowGeometryAsync(desktopGuid);
            if (geo is not null)
            {
                WindowPlacementHelper.ApplyGeometry(window, geo);
                StartupSnapshot.UpdateGeometry(desktopGuid, geo);
            }
        }

        await window.InitializePreferencesAsync(); // preference-cache hits: font size before first paint of text
        LogPhase("prefs-panel");

        WindowActivationHelper.ActivateWindow(window);

        await window.LoadTabsAsync();
        LogPhase("tabs-loaded");
    }

    /// <summary>
    /// Creates a fully initialized window. Used by IPC/hotkey/orphan paths where the
    /// database is already open.
    /// </summary>
    private async Task<MainWindow> CreateWindowForDesktop(string desktopGuid)
    {
        var window = CreateWindowShell(desktopGuid);
        await CompleteWindowInitAsync(window, desktopGuid);
        return window;
    }

    /// <summary>
    /// Open → schema → integrity/corruption recovery → EF warmup, as one awaitable unit.
    /// Every step runs on the DB worker thread. ConfigureAwait(false) keeps the chain from
    /// bouncing through the dispatcher between steps (it touches no UI), so it progresses while
    /// the UI thread is busy building the window.
    /// </summary>
    private static async Task InitializeDatabaseAsync()
    {
        var dbPath = AppEnvironment.DatabasePath;
        await DatabaseCore.OpenAsync(dbPath).ConfigureAwait(false);
        await DatabaseCore.EnsureSchemaAsync().ConfigureAwait(false);

        bool healthy = await DatabaseCore.VerifyIntegrityAsync().ConfigureAwait(false);
        if (!healthy)
        {
            LogService.Error("Database integrity check failed — initiating corruption recovery");
            await DatabaseCore.HandleCorruptionAsync(dbPath).ConfigureAwait(false);
        }

        await DatabaseCore.WarmupModelAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// After the DB cache loads, corrects anything the provisional snapshot got wrong.
    /// Theme is re-applied live; language cannot hot-swap (needs restart) so it only logs;
    /// log level is re-applied. Finally the snapshot file is refreshed to DB truth.
    /// </summary>
    private static void ReconcileSnapshotPreferences(IReadOnlyDictionary<string, string> dbPrefs)
    {
        var dbTheme = ThemeService.ParseThemePreference(dbPrefs.GetValueOrDefault("theme"));
        if (dbTheme != ThemeService.CurrentSetting)
        {
            LogService.Warn("Snapshot theme differed from database — re-applying {Theme}", dbTheme);
            ThemeService.ApplyTheme(dbTheme);
        }

        var dbLevel = ParseLogLevel(dbPrefs.GetValueOrDefault("log_level"));
        if (dbLevel is not null)
        {
            LogService.SetMinimumLevel(dbLevel.Value);
        }

        StartupSnapshot.ReplacePreferences(dbPrefs);
    }

    /// <summary>
    /// Global hotkey handler: on the current desktop, activate the existing window and add a note,
    /// or create a window if none exists. Marshaled to the UI thread.
    /// </summary>
    private void OnGlobalHotkeyPressed()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var currentGuid = VirtualDesktopService.CurrentDesktopGuid;
                if (_windows.TryGetValue(currentGuid, out var w))
                {
                    w.WindowState = WindowState.Normal;
                    WindowActivationHelper.ActivateWindow(w);
                    w.CreateNewTab();
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
    /// Called in the deferred startup tail (after the first tab load). Returns true when at least
    /// one move was recovered, i.e. notes changed desktop and open windows may need a reload.
    /// </summary>
    private static async Task<bool> ResolvePendingMovesAsync()
    {
        var moves = await PendingMoveStore.GetPendingMovesAsync();
        if (moves.Count == 0)
        {
            LogService.Info("Pending moves check: none found");
            return false;
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

        LogService.Info("Pending moves check: recovery complete");
        return recovered;
    }

    /// <summary>
    /// Reads the auto_delete_days preference and deletes unpinned notes older than that many days.
    /// Called once during startup, before the first tab load. Returns the number deleted (0 when
    /// the preference is unset — a preference-cache hit with no DB work).
    /// </summary>
    private static async Task<int> RunAutoDeleteAsync()
    {
        var saved = await PreferenceStore.GetPreferenceAsync("auto_delete_days").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(saved) || !int.TryParse(saved, out int days) || days <= 0)
            return 0;

        var cutoff = DateTime.Now.AddDays(-days);
        int deleted = await NoteStore.DeleteOldNotesAsync(cutoff);
        if (deleted > 0)
            LogService.Info("Startup auto-delete: {DeletedCount} note(s) older than {Days} days removed", deleted, days);
        return deleted;
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

        // Persist irreplaceable note content for EVERY window first — before any slower per-window
        // teardown (geometry, pending-deletion commits, close animations). If Windows cuts the
        // shutdown budget short, note data is already safe even if later close work doesn't finish.
        foreach (var window in windows)
        {
            try { window.FlushContentForShutdown(); }
            catch (Exception ex) { LogService.Error("Shutdown content flush failed", ex); }
        }

        foreach (var window in windows)
        {
            window.FlushAndClose();
        }

        BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

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
        VirtualDesktopService.Shutdown();
        ThemeService.Shutdown();

        bool drained = BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        if (!drained)
        {
            LogService.Warn("Shutdown: background writes did not drain within 5s");
        }

        // Synchronous close in exit path — no async available here
        DatabaseCore.CloseAsync().GetAwaiter().GetResult();

        ReleaseSingleInstanceMutex();

        _appShutdownCts.Dispose();

        LogService.Shutdown();

        base.OnExit(e);
    }
}
