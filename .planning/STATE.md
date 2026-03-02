# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.
**Current focus:** Phase 1 — Foundation

## Current Position

Phase: 1 of 10 (Foundation)
Plan: 2 of 3 in current phase
Status: In progress
Last activity: 2026-03-02 — Plan 01-02 complete: IpcService, WindowActivationHelper, MainWindow IPC/hide-on-close wiring

Progress: [██░░░░░░░░] 6%

## Performance Metrics

**Velocity:**
- Total plans completed: 2
- Average duration: ~3 min
- Total execution time: ~6 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1. Foundation | 2 | ~6 min | ~3 min |

**Recent Trend:**
- Last 5 plans: 01-01 (~2 min), 01-02 (~4 min)
- Trend: —

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

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 2 risk]: Virtual desktop COM GUIDs differ between Windows 11 23H2 and 24H2 — must research correct GUIDs before Phase 2 implementation; consider /gsd:research-phase before starting Phase 2
- [Phase 9 risk]: RegisterHotKey and SetForegroundWindow have STA thread and focus-stealing gotchas — worth a focused research pass before Phase 9 implementation
- [Phase 1 action]: Measure actual startup time against ReadyToRun binary on a clean machine; baseline must be established in Phase 1 before feature work begins

## Session Continuity

Last session: 2026-03-02
Stopped at: Completed 01-02-PLAN.md — IPC/process lifecycle complete, ready for Plan 01-03 (startup wiring)
Resume file: .planning/phases/01-foundation/01-03-PLAN.md
