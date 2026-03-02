---
phase: 01-foundation
plan: "03"
subsystem: startup
tags: [startup-sequence, mutex, ipc, sqlite, welcome-tab, background-migrations, ready-to-run, app-lifecycle]

# Dependency graph
requires:
  - JoJot/Services/DatabaseService.cs (Plan 01-01) — OpenAsync, EnsureSchemaAsync, VerifyIntegrityAsync, HandleCorruptionAsync, ExecuteNonQueryAsync, RunPendingMigrationsAsync, CloseAsync
  - JoJot/Services/LogService.cs (Plan 01-01) — Initialize, Info, Warn, Error
  - JoJot/Models/IpcMessage.cs (Plan 01-01) — ActivateCommand, NewTabCommand, ShowDesktopCommand
  - JoJot/Services/IpcService.cs (Plan 01-02) — TryAcquireMutex, StartServer, StopServer, SendCommandAsync
  - JoJot/MainWindow.xaml.cs (Plan 01-02) — ActivateFromIpc, FlushAndClose, ShowAndActivate
provides:
  - App.xaml.cs: full 11-step startup sequence (logging → mutex → ShutdownMode → DB open → schema → integrity → pending_moves stub → welcome tab → IPC server → window show → startup timing → background migrations)
  - StartupService: CreateWelcomeTabIfFirstLaunch (inserts welcome note when notes table is empty), RunBackgroundMigrationsAsync (wraps RunPendingMigrationsAsync with catch-and-log)
  - Global unhandled exception handlers (AppDomain, Dispatcher, TaskScheduler) — all exceptions logged, never rethrown to user
  - Single-instance mutex held for process lifetime (GC.KeepAlive pattern)
  - App.xaml: Startup event wiring (no StartupUri — window created in code)
  - JoJot.csproj: PublishReadyToRun=true
affects: [04-notes, 05-tabs, 06-desktop, 07-ui, 08-settings, 09-hotkey, 10-drag]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "async void OnAppStartup with Stopwatch for startup timing measurement"
    - "GC.KeepAlive(_singleInstanceMutex) to prevent mutex GC collection before process exit"
    - "ShutdownMode.OnExplicitShutdown set in code (not XAML) — required for hide-on-close process persistence"
    - "Task.Run(() => ...) for background migrations — fire-and-forget after window is shown"
    - "Global exception handlers: AppDomain.UnhandledException + DispatcherUnhandledException (args.Handled=true) + TaskScheduler.UnobservedTaskException"

key-files:
  created:
    - JoJot/Services/StartupService.cs
  modified:
    - JoJot/App.xaml
    - JoJot/App.xaml.cs
    - JoJot/JoJot.csproj

key-decisions:
  - "GC.KeepAlive(_singleInstanceMutex) is required — mutex is a managed object and the GC may collect it if only stored in a field that appears unused; KeepAlive pins it for the process lifetime"
  - "ShutdownMode.OnExplicitShutdown set in OnAppStartup code — cannot be set in XAML before App.xaml.cs runs, and must be set before the window is created"
  - "Background migrations use Task.Run not Task.Factory.StartNew — simpler idiom, thread pool appropriate for non-UI background work"
  - "DispatcherUnhandledException sets args.Handled=true — prevents WPF from showing its default crash dialog; app degrades gracefully per spec"
  - "Environment.Exit(0) in second-instance path — ensures the process exits even if WPF tries to keep it alive via ShutdownMode"

patterns-established:
  - "Pattern: Startup sequence in async void OnAppStartup with Stopwatch — all future startup changes go here"
  - "Pattern: StartupService for startup-time DB operations — keeps App.xaml.cs orchestration-only"
  - "Pattern: Global exception handlers registered before mutex acquisition — catches all crashes including early startup failures"

requirements-completed: [STRT-01, STRT-02, STRT-03]

# Metrics
duration: ~5min
completed: 2026-03-02
---

# Phase 1 Plan 03: Startup Wiring Summary

**11-step App.xaml.cs startup sequence integrating all Phase 1 services: mutex guard, SQLite open, IPC server, welcome tab, window show, startup timing, and background migrations — with ReadyToRun publish config and global exception handlers**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-02T18:22:35Z
- **Completed:** 2026-03-02T18:27:30Z
- **Tasks:** 2 of 2 (1 auto + 1 human-verify checkpoint)
- **Files modified:** 4

## Accomplishments

- App.xaml.cs rewritten with full 11-step startup sequence: logging init, mutex guard, second-instance IPC path, ShutdownMode.OnExplicitShutdown, DB open+schema+integrity, pending_moves stub (Phase 10), welcome tab, IPC server start, window create+show, startup timing log, and background migrations
- StartupService.CreateWelcomeTabIfFirstLaunch inserts a "Welcome to JoJot" note on first launch (COUNT(*) FROM notes == 0), establishing the first-run experience
- StartupService.RunBackgroundMigrationsAsync wraps RunPendingMigrationsAsync in try/catch — migration failures are logged and swallowed so they never block the user
- Global unhandled exception handlers cover AppDomain, WPF Dispatcher, and TaskScheduler — app never shows a crash dialog and always degrades gracefully
- ReadyToRun publish verified: `dotnet publish -r win-x64 -p:PublishReadyToRun=true` succeeds and the binary starts up

## Task Commits

Each task was committed atomically:

1. **Task 1: Create StartupService, wire App.xaml.cs startup sequence, add R2R config** - `bc66308` (feat)

**Plan metadata:** (this commit — docs)

## Files Created/Modified

- `JoJot/Services/StartupService.cs` — CreateWelcomeTabIfFirstLaunch (first-run welcome note), RunBackgroundMigrationsAsync (catch-and-log wrapper)
- `JoJot/App.xaml` — Removed StartupUri, added Startup="OnAppStartup" event handler
- `JoJot/App.xaml.cs` — Full startup sequence with mutex, DB init, IPC, window creation, timing, background migrations, OnExit cleanup, and global exception handlers
- `JoJot/JoJot.csproj` — Added PublishReadyToRun=true

## Decisions Made

- `GC.KeepAlive(_singleInstanceMutex)` is explicitly called after assignment — the mutex is a managed object and the GC may collect it if the field appears unused between assignments and process exit; KeepAlive pins it
- `ShutdownMode.OnExplicitShutdown` is set in code (not XAML) because XAML parses before OnAppStartup runs and setting it there would not apply to the early process lifecycle; it must be set before the window is created
- Second-instance path uses `Environment.Exit(0)` rather than `Application.Current.Shutdown()` — ensures the process exits immediately even if WPF internals try to keep it running
- `DispatcherUnhandledException` sets `args.Handled = true` — prevents WPF default crash dialog per user spec: "unbreakable, degrade gracefully, never show unhandled exception dialogs"

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## End-to-End Verification

All 4 runtime tests passed (user-approved checkpoint):

1. **First launch** — Database created at `%LOCALAPPDATA%\JoJot\jojot.db`, log file created with startup timing entry, window appears, process stays alive after close
2. **Second instance** — Second `dotnet run` sends IPC activate and exits quickly; first instance window reappears and is focused; log shows "Second instance detected" and "Window activated via IPC"
3. **Database integrity** — Force-killing the process and relaunching leaves the database intact; integrity check passes on next startup
4. **ReadyToRun publish** — `dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 -p:PublishReadyToRun=true` succeeds

## Next Phase Readiness

- Phase 1 is fully complete: all 3 plans executed, all requirements covered (DATA-01 through DATA-07, PROC-01 through PROC-06, STRT-01 through STRT-04)
- The startup sequence is the integration point for all future phases — new services plug in at the appropriate startup step
- Phase 2 (Virtual Desktop Integration) can begin; recommend a `/gsd:research-phase` pass first due to COM GUID differences between Windows 11 23H2 and 24H2

## Self-Check: PASSED

- `FOUND: .planning/phases/01-foundation/01-03-SUMMARY.md`
- `FOUND: bc66308 feat(01-03): wire full startup sequence and add StartupService`

---
*Phase: 01-foundation*
*Completed: 2026-03-02*
