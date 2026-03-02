---
phase: 01-foundation
plan: "02"
subsystem: ipc
tags: [named-pipe, single-instance, mutex, p-invoke, window-activation, ipc, proc-lifecycle]

# Dependency graph
requires:
  - JoJot/Services/LogService.cs (Plan 01-01)
  - JoJot/Models/IpcMessage.cs (Plan 01-01)
provides:
  - IpcService: named pipe server (first instance) and client (second instance), global mutex, zombie kill
  - WindowActivationHelper: AttachThreadInput + SetForegroundWindow P/Invoke window focus helper
  - MainWindow.ActivateFromIpc: IPC-triggered window activation (visible + hidden states)
  - MainWindow.OnClosing: hides instead of closing (PROC-05 — process stays alive)
  - MainWindow.FlushAndClose: Phase 1 stub for graceful shutdown (PROC-06)
affects: [03-startup, 09-hotkey]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Named pipe IPC: NamedPipeServerStream with PipeOptions.Asynchronous + async listen loop"
    - "Global mutex for single-instance: new Mutex(true, 'Global\\JoJot_SingleInstance', out bool createdNew)"
    - "AttachThreadInput + SetForegroundWindow for reliable cross-process window focus"
    - "DispatcherPriority.ApplicationIdle for IPC-triggered window activation"
    - "OnClosing cancel + Hide() for PROC-05 process-alive-after-window-close"

key-files:
  created:
    - JoJot/Services/IpcService.cs
    - JoJot/Services/WindowActivationHelper.cs
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "PipeOptions.Asynchronous is mandatory on server stream — enables CancellationToken in WaitForConnectionAsync"
  - "Client Connect(timeoutMs) wrapped in Task.Run because synchronous overload blocks; async overload lacks timeout"
  - "DispatcherPriority.ApplicationIdle for ActivateFromIpc — yields to pending WPF layout before focusing"
  - "FlushAndClose is a Phase 1 stub — full implementation deferred until tabs and content exist in later phases"
  - "OnClosing sets e.Cancel=true and hides — the app only truly exits when FlushAndClose calls the real Close()"

requirements-completed: [PROC-01, PROC-02, PROC-03, PROC-04, PROC-05, PROC-06]

# Metrics
duration: 4min
completed: 2026-03-02
---

# Phase 1 Plan 02: IPC and Process Lifecycle Summary

**Named pipe IPC server/client with global single-instance mutex, AttachThreadInput P/Invoke window activation, and hide-on-close process persistence**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-03-02T18:18:43Z
- **Completed:** 2026-03-02T18:22:35Z
- **Tasks:** 2 of 2
- **Files modified:** 4

## Accomplishments

- IpcService provides named pipe server (async listen loop, PipeOptions.Asynchronous, dispatches to UI Dispatcher), named pipe client (Connect with 500ms timeout, zombie kill on TimeoutException), global single-instance mutex helper, and KillExistingInstances for zombie cleanup
- WindowActivationHelper uses six user32.dll P/Invoke declarations; ActivateWindow attaches to the foreground thread before calling SetForegroundWindow, then detaches — the standard pattern for reliable cross-process focus on Windows
- MainWindow updated: Title="JoJot", ActivateFromIpc handles both visible and hidden window states, OnClosing hides instead of closing (PROC-05), FlushAndClose stub for PROC-06, ShowAndActivate for IPC-triggered reveal

## Task Commits

Each task was committed atomically:

1. **Task 1: IpcService and WindowActivationHelper** - `a22bca7` (feat)
2. **Task 2: MainWindow IPC handler, hide-on-close, FlushAndClose** - `8e4fd4f` (feat)

## Files Created/Modified

- `JoJot/Services/IpcService.cs` — Named pipe server/client/mutex, zombie kill logic
- `JoJot/Services/WindowActivationHelper.cs` — P/Invoke declarations + ActivateWindow
- `JoJot/MainWindow.xaml` — Title set to "JoJot"
- `JoJot/MainWindow.xaml.cs` — ActivateFromIpc, ShowAndActivate, FlushAndClose stub, OnClosing (hide)

## Decisions Made

- `PipeOptions.Asynchronous` is required on `NamedPipeServerStream` — without it, `WaitForConnectionAsync` does not honour the `CancellationToken` on Windows
- The synchronous `Connect(int timeout)` overload is wrapped in `Task.Run` because the async overload does not accept a timeout parameter; this gives us timeout semantics without blocking the calling thread
- `DispatcherPriority.ApplicationIdle` in `ActivateFromIpc` lets any pending WPF rendering complete before raising the window, reducing flicker on first activation
- `FlushAndClose` is an intentional Phase 1 stub — the full sequence (flush content, delete empty tabs, persist geometry) requires tabs and persistence infrastructure added in later phases

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None.

## Next Phase Readiness

- IpcService and WindowActivationHelper are ready for Plan 03 (startup sequence) to wire up
- App.xaml.cs will: acquire mutex, call IpcService.StartServer or SendCommandAsync, set ShutdownMode.OnExplicitShutdown
- The IPC command router in App.xaml.cs will call ActivateFromIpc on the main window when ActivateCommand arrives

---
*Phase: 01-foundation*
*Completed: 2026-03-02*
