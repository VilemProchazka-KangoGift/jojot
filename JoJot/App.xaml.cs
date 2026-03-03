using System.Diagnostics;
using System.IO;
using System.Windows;
using JoJot.Models;
using JoJot.Services;

namespace JoJot
{
    /// <summary>
    /// Application entry point.
    /// Orchestrates the full startup sequence: mutex guard, logging, database initialization,
    /// IPC server start, window creation, startup timing, and background migrations.
    /// ShutdownMode is OnExplicitShutdown — the process stays alive when all windows are closed.
    /// Phase 3: per-desktop window registry replaces single _mainWindow field.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Held for the full process lifetime to satisfy the single-instance mutex.
        /// Must be kept here (not a local variable) to prevent GC collection between checks.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        private readonly CancellationTokenSource _appShutdownCts = new();

        /// <summary>
        /// Per-desktop window registry. Key is desktop GUID string (case-insensitive).
        /// Windows are added on creation and removed on Closed event.
        /// </summary>
        private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

        private async void OnAppStartup(object sender, StartupEventArgs e)
        {
            // ── Global unhandled exception handlers ──────────────────────────
            // Set up before mutex so any crash in startup is captured.
            // Per design spec: "unbreakable, degrade gracefully, never show unhandled exception dialogs"
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

            var sw = Stopwatch.StartNew();

            // ── Step 1: Initialize logging ────────────────────────────────────
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JoJot");
            Directory.CreateDirectory(appDir);
            LogService.Initialize(appDir);
            LogService.Info("JoJot starting...");

            // ── Step 2: Mutex — single-instance enforcement (PROC-01) ─────────
            bool createdNew = IpcService.TryAcquireMutex(out var mutex);
            _singleInstanceMutex = mutex;
            GC.KeepAlive(_singleInstanceMutex);

            if (!createdNew)
            {
                // Second instance detected (PROC-03): send command based on arguments and exit
                bool isNewTab = e.Args.Any(a =>
                    a.Equals("--new-tab", StringComparison.OrdinalIgnoreCase));

                if (isNewTab)
                {
                    LogService.Info("Second instance detected \u2014 sending new-tab command");
                    await IpcService.SendCommandAsync(new NewTabCommand());
                }
                else
                {
                    LogService.Info("Second instance detected \u2014 sending activate command");
                    await IpcService.SendCommandAsync(new ActivateCommand());
                }
                Environment.Exit(0);
                return;
            }

            // ── Step 3: ShutdownMode (PROC-05) ────────────────────────────────
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // ── Step 4: Open database + ensure schema (DATA-01, DATA-07, STRT-04) ──
            string dbPath = Path.Combine(appDir, "jojot.db");
            await DatabaseService.OpenAsync(dbPath);
            await DatabaseService.EnsureSchemaAsync();

            // ── Step 5: Integrity check + corruption recovery ─────────────────
            bool healthy = await DatabaseService.VerifyIntegrityAsync();
            if (!healthy)
            {
                LogService.Error("Database integrity check failed \u2014 initiating corruption recovery");
                await DatabaseService.HandleCorruptionAsync(dbPath);
            }

            // ── Step 5.5: Virtual desktop detection (VDSK-01, VDSK-08) ────
            await VirtualDesktopService.InitializeAsync();
            if (VirtualDesktopService.IsAvailable)
                LogService.Info($"Virtual desktop: {VirtualDesktopService.CurrentDesktopGuid} ({VirtualDesktopService.CurrentDesktopName})");
            else
                LogService.Info("Virtual desktop: fallback mode (single-notepad)");

            // ── Step 5.55: Subscribe to desktop notifications (VDSK-07) ────
            // Must happen on the UI thread (STA requirement for COM callbacks)
            VirtualDesktopService.SubscribeNotifications();

            // ── Step 5.6: Session matching (VDSK-03, VDSK-04, VDSK-05) ────
            await VirtualDesktopService.MatchSessionsAsync();
            await VirtualDesktopService.EnsureCurrentDesktopSessionAsync();

            // ── Step 6: Pending moves check (stub for Phase 10 crash recovery) ─
            // Phase 10: await DatabaseService.ResolvePendingMovesAsync();
            LogService.Info("Pending moves check: skipped (Phase 10)");

            // ── Step 7: Welcome tab on first launch ───────────────────────────
            await StartupService.CreateWelcomeTabIfFirstLaunch();

            // ── Step 8: Start IPC server (PROC-02) ────────────────────────────
            IpcService.StartServer(HandleIpcCommand, _appShutdownCts.Token);

            // ── Step 8.5: Run pending migrations before window restore ────────
            // Migrations must complete before GetWindowGeometryAsync (which reads
            // columns added by migrations like window_state).
            await StartupService.RunBackgroundMigrationsAsync();

            // ── Step 9: Create and show window for current desktop ────────────
            string currentDesktopGuid = VirtualDesktopService.CurrentDesktopGuid;
            await CreateWindowForDesktop(currentDesktopGuid);

            // ── Step 9.5: Wire desktop event handlers ──────────────────────────
            // Live title updates when desktop is renamed in Windows Settings
            VirtualDesktopService.DesktopRenamed += (desktopGuid, newName) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_windows.TryGetValue(desktopGuid, out var w))
                    {
                        // Resolve the index for this desktop
                        var desktops = VirtualDesktopService.GetAllDesktops();
                        var info = desktops.FirstOrDefault(d =>
                            d.Id.ToString().Equals(desktopGuid, StringComparison.OrdinalIgnoreCase));
                        w.UpdateDesktopTitle(newName, info?.Index);
                    }
                });
            };

            // Log desktop switches (no auto-create per user decision)
            VirtualDesktopService.CurrentDesktopChanged += (oldGuid, newGuid) =>
            {
                LogService.Info($"Desktop switched: {oldGuid} -> {newGuid}");
                // Per user decision: no auto-create; windows only appear via explicit taskbar click/launch
            };

            // ── Step 10: Log startup timing (STRT-01) ────────────────────────
            sw.Stop();
            LogService.Info($"Startup complete in {sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[JoJot] Startup: {sw.ElapsedMilliseconds}ms");

            // ── Step 11: Background migrations (STRT-03) ─────────────────────
            // Migrations now run synchronously in Step 8.5 (before window restore).
        }

        // ─── Window Factory ─────────────────────────────────────────────────────

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
                LogService.Info($"Window removed from registry for desktop {desktopGuid} ({_windows.Count} windows remaining)");
            };

            // Restore geometry (must happen before Show for WindowStartupLocation to work)
            var geo = await DatabaseService.GetWindowGeometryAsync(desktopGuid);
            WindowPlacementHelper.ApplyGeometry(window, geo);

            // Set title
            if (VirtualDesktopService.IsAvailable)
            {
                var desktopInfo = VirtualDesktopService.GetCurrentDesktopInfo();
                window.UpdateDesktopTitle(
                    VirtualDesktopService.CurrentDesktopName,
                    desktopInfo.Index);
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

            // Phase 4: Load tabs from database after window is shown
            await window.LoadTabsAsync();

            WindowActivationHelper.ActivateWindow(window);
            return window;
        }

        // ─── IPC Command Routing ─────────────────────────────────────────────────

        /// <summary>
        /// Routes incoming IPC commands to the correct desktop's window.
        /// Resolves the current desktop GUID at handle time (live state, not send time).
        /// Always called on the UI Dispatcher (IpcService ensures this).
        /// </summary>
        private async void HandleIpcCommand(IpcMessage message)
        {
            string desktopGuid = VirtualDesktopService.CurrentDesktopGuid;

            switch (message)
            {
                case ActivateCommand:
                    LogService.Info($"IPC: activate \u2014 desktop {desktopGuid}");
                    if (_windows.TryGetValue(desktopGuid, out var existingWindow))
                    {
                        WindowActivationHelper.ActivateWindow(existingWindow);
                    }
                    else
                    {
                        await CreateWindowForDesktop(desktopGuid);
                    }
                    break;

                case NewTabCommand:
                    LogService.Info($"IPC: new-tab \u2014 desktop {desktopGuid}");
                    if (_windows.TryGetValue(desktopGuid, out var tabWindow))
                    {
                        WindowActivationHelper.ActivateWindow(tabWindow);
                        tabWindow.RequestNewTab();
                    }
                    else
                    {
                        var newWindow = await CreateWindowForDesktop(desktopGuid);
                        newWindow.RequestNewTab();
                    }
                    break;

                case ShowDesktopCommand showCmd:
                    LogService.Info($"IPC: show-desktop {showCmd.DesktopGuid} (Phase 10)");
                    break;

                default:
                    LogService.Warn($"IPC: unknown command type '{message.GetType().Name}'");
                    break;
            }
        }

        /// <summary>
        /// Called when the application exits. Cancels the IPC server, flushes the database,
        /// and releases the single-instance mutex.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("JoJot shutting down...");

            _appShutdownCts.Cancel();
            IpcService.StopServer();
            VirtualDesktopService.Shutdown();

            // Synchronous close in exit path — no async available here
            DatabaseService.CloseAsync().GetAwaiter().GetResult();

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}
