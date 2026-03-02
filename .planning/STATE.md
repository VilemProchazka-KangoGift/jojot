# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.
**Current focus:** Phase 3 — Window & Session Management

## Current Position

Phase: 3 of 10 (Window & Session Management)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-03-02 — Phase 2 verified (9/9 requirements, 5/5 criteria passed); advance to Phase 3

Progress: [██████░░░░] 20%

## Performance Metrics

**Velocity:**
- Total plans completed: 6
- Average duration: ~4.5 min
- Total execution time: ~27 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1. Foundation | 3 | ~11 min | ~3.7 min |
| 2. Virtual Desktop Integration | 3 | ~16 min | ~5.3 min |

**Recent Trend:**
- Last 5 plans: 01-02 (~4 min), 01-03 (~5 min), 02-01 (~15 min), 02-02 (~10 min), 02-03 (~10 min)
- Trend: Stable (Phase 2 plans larger due to COM interop complexity)

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

- [02-01]: Raw COM interop with [ComImport] — no NuGet packages (none support .NET 10); build-specific GUID dictionary with floor-key lookup
- [02-01]: IVirtualDesktopNotificationService is optional — if unavailable, detection still works but live updates disabled
- [02-01]: COM boundary isolation: all COM types in JoJot/Interop/, VirtualDesktopService public API uses string/int/DesktopInfo only
- [02-02]: Tier 2 name match skips ambiguous cases (0 or 2+ desktops share name); Tier 3 requires strict one-to-one match
- [02-02]: Orphaned sessions preserved in DB for Phase 8 recovery panel; never auto-deleted
- [02-02]: UpdateSessionAsync cascades GUID changes to notes.desktop_guid for FK consistency
- [02-03]: COM notification listener implements IVirtualDesktopNotification with [ComVisible(true)]
- [02-03]: Database update from rename callback is fire-and-forget (Task.Run) to avoid blocking COM thread
- [02-03]: Window title uses Unicode escape \u2014 for em-dash to ensure correct encoding

### Pending Todos

None yet.

### Blockers/Concerns

- [RESOLVED Phase 2 risk]: Virtual desktop COM GUIDs differ between Windows 11 23H2 and 24H2 — resolved with build-specific GUID dictionary in ComGuids.cs
- [Phase 9 risk]: RegisterHotKey and SetForegroundWindow have STA thread and focus-stealing gotchas — worth a focused research pass before Phase 9 implementation
- [Phase 1 action]: Measure actual startup time against ReadyToRun binary on a clean machine; baseline must be established in Phase 1 before feature work begins

## Session Continuity

Last session: 2026-03-02
Stopped at: Phase 2 complete, advance to Phase 3
Resume file: .planning/ROADMAP.md
