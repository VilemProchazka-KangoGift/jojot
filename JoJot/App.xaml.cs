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
    /// ShutdownMode is OnExplicitShutdown — the process stays alive when the window is closed.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Held for the full process lifetime to satisfy the single-instance mutex.
        /// Must be kept here (not a local variable) to prevent GC collection between checks.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        private readonly CancellationTokenSource _appShutdownCts = new();
        private MainWindow? _mainWindow;

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
                // Second instance detected (PROC-03): activate the first and exit
                LogService.Info("Second instance detected — sending activate command");
                await IpcService.SendCommandAsync(new ActivateCommand());
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
                LogService.Error("Database integrity check failed — initiating corruption recovery");
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

            // ── Step 9: Create and show window ────────────────────────────────
            _mainWindow = new MainWindow();
            _mainWindow.Show();

            // ── Step 9.5: Set window title and subscribe to rename events (VDSK-06) ──
            if (VirtualDesktopService.IsAvailable)
            {
                var desktopInfo = VirtualDesktopService.GetCurrentDesktopInfo();
                _mainWindow.UpdateDesktopTitle(
                    VirtualDesktopService.CurrentDesktopName,
                    desktopInfo.Index);
            }
            else
            {
                _mainWindow.UpdateDesktopTitle(null, null); // Shows "JoJot"
            }

            // Live title updates when desktop is renamed in Windows Settings
            VirtualDesktopService.DesktopRenamed += (desktopGuid, newName) =>
            {
                if (desktopGuid.Equals(VirtualDesktopService.CurrentDesktopGuid, StringComparison.OrdinalIgnoreCase)
                    && _mainWindow is not null)
                {
                    var info = VirtualDesktopService.GetCurrentDesktopInfo();
                    Dispatcher.InvokeAsync(() =>
                    {
                        _mainWindow.UpdateDesktopTitle(newName, info.Index);
                    });
                }
            };

            // Log desktop switches (Phase 3 will handle showing/hiding windows per desktop)
            VirtualDesktopService.CurrentDesktopChanged += (oldGuid, newGuid) =>
            {
                LogService.Info($"Desktop switched: {oldGuid} -> {newGuid}");
                // Phase 3 will handle multi-window visibility per desktop
            };

            // ── Step 10: Log startup timing (STRT-01) ────────────────────────
            sw.Stop();
            LogService.Info($"Startup complete in {sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[JoJot] Startup: {sw.ElapsedMilliseconds}ms");

            // ── Step 11: Background migrations (STRT-03) ─────────────────────
            _ = Task.Run(() => StartupService.RunBackgroundMigrationsAsync());
        }

        /// <summary>
        /// Routes incoming IPC commands to the appropriate handler.
        /// Always called on the UI Dispatcher (IpcService ensures this).
        /// </summary>
        private void HandleIpcCommand(IpcMessage message)
        {
            switch (message)
            {
                case ActivateCommand:
                    if (_mainWindow is not null)
                    {
                        LogService.Info("IPC: activate command received — showing window");
                        _mainWindow.ActivateFromIpc();
                    }
                    break;

                case NewTabCommand:
                    LogService.Info("IPC: new-tab command received (not implemented until Phase 4)");
                    break;

                case ShowDesktopCommand:
                    LogService.Info("IPC: show-desktop command received (not implemented until Phase 2)");
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
