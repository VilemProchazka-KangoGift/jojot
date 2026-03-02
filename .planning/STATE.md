# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.
**Current focus:** Phase 2 — Virtual Desktop Integration

## Current Position

Phase: 2 of 10 (Virtual Desktop Integration)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-03-02 — Phase 1 verified (17/17 must-haves passed); advanced to Phase 2

Progress: [███░░░░░░░] 10%

## Performance Metrics

**Velocity:**
- Total plans completed: 3
- Average duration: ~3.7 min
- Total execution time: ~11 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1. Foundation | 3 | ~11 min | ~3.7 min |

**Recent Trend:**
- Last 5 plans: 01-01 (~2 min), 01-02 (~4 min), 01-03 (~5 min)
- Trend: Stable

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Pre-Phase 1]: PublishAot=true is incompatible with WPF; use PublishReadyToRun=true instead (confirmed blocker, resolved in requirements)
- [Pre-Phase 1]: Custom undo/redo required; WPF native TextBox undo clears on tab switch and cannot be used
- [Pre-Phase 1]: Single process + named pipe IPC; not multi-process — one SQLite connection, consistent state
- [01-01]: Do NOT use Cache=Shared with WAL mode (Microsoft.Data.Sqlite docs explicit warning)
- [01-01]: SemaphoreSlim(1,1) for write serialization — async-compatible unlike lock; all DB operations go through ExecuteNonQueryAsync
- [01-01]: PRAGMA quick_check over integrity_check at startup — O(N) vs O(NlogN), sufficient for table existence verification
- [01-02]: PipeOptions.Asynchronous required on NamedPipeServerStream — without it WaitForConnectionAsync ignores CancellationToken on Windows
- [01-02]: Client Connect(timeoutMs) wrapped in Task.Run — async overload lacks timeout parameter; synchronous overload blocks
- [01-02]: DispatcherPriority.ApplicationIdle for ActivateFromIpc — yields to pending WPF rendering before raising window
- [01-02]: OnClosing hides instead of closing (e.Cancel=true); only FlushAndClose calls real Close() for PROC-05/PROC-06
- [01-03]: GC.KeepAlive(_singleInstanceMutex) required — managed mutex can be collected if field appears unused; KeepAlive pins it for process lifetime
- [01-03]: ShutdownMode.OnExplicitShutdown set in code not XAML — must be set before window creation; XAML parses before OnAppStartup
- [01-03]: DispatcherUnhandledException sets args.Handled=true — prevents WPF crash dialog per "degrade gracefully" spec
- [01-03]: Environment.Exit(0) in second-instance path — ensures clean exit even if WPF internals try to keep process alive

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 2 risk]: Virtual desktop COM GUIDs differ between Windows 11 23H2 and 24H2 — must research correct GUIDs before Phase 2 implementation; consider /gsd:research-phase before starting Phase 2
- [Phase 9 risk]: RegisterHotKey and SetForegroundWindow have STA thread and focus-stealing gotchas — worth a focused research pass before Phase 9 implementation
- [Phase 1 action]: Measure actual startup time against ReadyToRun binary on a clean machine; baseline must be established in Phase 1 before feature work begins

## Session Continuity

Last session: 2026-03-02
Stopped at: Phase 2 context gathered
Resume file: .planning/phases/02-virtual-desktop-integration/02-CONTEXT.md
