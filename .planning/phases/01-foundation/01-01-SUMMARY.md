---
phase: 01-foundation
plan: "01"
subsystem: database
tags: [sqlite, wal, logging, ipc, json-polymorphism, source-gen]

# Dependency graph
requires: []
provides:
  - Microsoft.Data.Sqlite 10.0.3 in JoJot.csproj
  - DatabaseService: single SQLite connection with WAL mode, 4-table schema, integrity check, corruption recovery, write serialization
  - LogService: dual-output logging (file + System.Diagnostics.Debug) with 5MB rotation
  - IpcMessage type hierarchy with JSON polymorphic serialization (activate, new-tab, show-desktop)
  - IpcMessageContext: source-generated JsonSerializerContext for all IPC types
affects: [02-ipc, 03-startup, 04-notes, 05-tabs, 06-desktop, 07-ui, 08-settings]

# Tech tracking
tech-stack:
  added: [Microsoft.Data.Sqlite 10.0.3]
  patterns:
    - "Static service classes with process-lifetime singletons (DatabaseService, LogService)"
    - "SemaphoreSlim(1,1) for write serialization across all DB operations"
    - "JsonPolymorphic + JsonDerivedType for IPC message type discrimination"
    - "Source-generated JsonSerializerContext for AOT-friendly JSON serialization"
    - "Log rotation by file size check on Initialize (5MB threshold)"

key-files:
  created:
    - JoJot/Services/DatabaseService.cs
    - JoJot/Services/LogService.cs
    - JoJot/Models/IpcMessage.cs
  modified:
    - JoJot/JoJot.csproj

key-decisions:
  - "Do NOT use Cache=Shared with WAL mode (official Microsoft.Data.Sqlite docs warning)"
  - "Single SqliteConnection per process (process-lifetime singleton) — no connection pool needed with WAL"
  - "SemaphoreSlim(1,1) instead of lock for write serialization — async-compatible"
  - "PRAGMA quick_check over integrity_check at startup (O(N) vs O(NlogN), sufficient for table existence verification)"
  - "All DB exceptions in non-schema methods are caught, logged, and re-thrown so callers can handle"
  - "RunPendingMigrationsAsync is a no-op stub in Phase 1; runs in background after window show in later phases"

patterns-established:
  - "Pattern: Static service singleton — DatabaseService and LogService are static classes, not injected"
  - "Pattern: All DB writes go through ExecuteNonQueryAsync with SemaphoreSlim guard"
  - "Pattern: LogService.Info/Warn/Error + Debug.WriteLine dual output on every log call"
  - "Pattern: IpcMessage records use sealed record for all derived types"

requirements-completed: [DATA-01, DATA-02, DATA-03, DATA-04, DATA-05, DATA-06, DATA-07, STRT-04]

# Metrics
duration: 2min
completed: 2026-03-02
---

# Phase 1 Plan 01: Foundation Data Layer Summary

**SQLite WAL-mode database with 4-table schema, write-serialized connection, integrity/corruption recovery, dual-output log rotation, and JSON-polymorphic IPC message type hierarchy**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-02T18:16:45Z
- **Completed:** 2026-03-02T18:18:43Z
- **Tasks:** 2 of 2
- **Files modified:** 4

## Accomplishments

- DatabaseService opens/creates SQLite at any path, enables WAL+NORMAL+foreign_keys, creates all 4 tables idempotently, verifies integrity on launch, and recovers from corruption by backing up and recreating
- LogService provides Info/Warn/Error logging to both jojot.log file (with 5MB rotation) and System.Diagnostics.Debug in a thread-safe, silent-failure design
- IpcMessage type hierarchy with JsonPolymorphic discriminator "action" supports activate, new-tab, and show-desktop commands; IpcMessageContext source-gen context enables AOT-friendly serialization

## Task Commits

Each task was committed atomically:

1. **Task 1: Add SQLite package, LogService, and IPC message types** - `758bae0` (feat)
2. **Task 2: Create DatabaseService** - `e4bbdbe` (feat)

## Files Created/Modified

- `JoJot/JoJot.csproj` - Added Microsoft.Data.Sqlite 10.0.3 PackageReference
- `JoJot/Services/LogService.cs` - Dual-output log service with file rotation and thread-safe write
- `JoJot/Models/IpcMessage.cs` - Abstract IpcMessage base + ActivateCommand, NewTabCommand, ShowDesktopCommand + IpcMessageContext
- `JoJot/Services/DatabaseService.cs` - Single SqliteConnection singleton with WAL setup, schema creation, integrity check, corruption recovery, serialized write operations

## Decisions Made

- Used `SemaphoreSlim(1,1)` (not `lock`) for write serialization because all DB methods are async — `lock` cannot be held across `await` boundaries
- All non-schema DB methods catch, log, and re-throw exceptions — callers (startup sequence) need to handle and decide on recovery strategy
- `RunPendingMigrationsAsync` is a no-op stub that logs "No pending migrations" — this satisfies the contract for Phase 1 while establishing the interface Plan 02+ will use
- `ExecuteReaderAsync` uses the write lock for consistency; this prevents a read from observing a partially-written state during concurrent writes

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- DatabaseService, LogService, and IpcMessage are all ready for use by Plan 02 (IPC transport) and Plan 03 (startup sequence)
- App.xaml.cs startup wiring is NOT done yet — that is Plan 03's responsibility
- The database is not yet opened on app start; DatabaseService.OpenAsync must be called from the startup sequence

---
*Phase: 01-foundation*
*Completed: 2026-03-02*
