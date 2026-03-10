---
phase: quick-2
plan: 01
subsystem: infra
tags: [conditional-compilation, debug-isolation, ipc, sqlite]

# Dependency graph
requires: []
provides:
  - "AppEnvironment static class for centralized debug/release path resolution"
  - "Separate database, logs, IPC pipe, and mutex for debug vs production builds"
affects: [app-startup, ipc, logging, database]

# Tech tracking
tech-stack:
  added: []
  patterns: ["#if DEBUG conditional compilation for environment isolation"]

key-files:
  created:
    - JoJot/Services/AppEnvironment.cs
    - JoJot.Tests/Services/AppEnvironmentTests.cs
  modified:
    - JoJot/App.xaml.cs
    - JoJot/Services/IpcService.cs

key-decisions:
  - "Used #if DEBUG conditional compilation over runtime Assembly.IsDebug checks for zero-cost release path"
  - "Changed IpcService.PipeName/MutexName from const to static property (safe since only used at runtime)"

patterns-established:
  - "AppEnvironment: single source of truth for all environment-dependent paths and identifiers"

requirements-completed: [QUICK-2]

# Metrics
duration: 2min
completed: 2026-03-10
---

# Quick Task 2: Separate Debug and Production Data Paths

**AppEnvironment with #if DEBUG conditional compilation isolating database, logs, IPC pipe, and mutex between debug and release builds**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-10T11:34:52Z
- **Completed:** 2026-03-10T11:37:20Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Debug builds now use `%LocalAppData%\JoJot.Dev\` with `JoJot_IPC.Dev` pipe and `Global\JoJot_SingleInstance.Dev` mutex
- Release builds remain unchanged (`%LocalAppData%\JoJot\`, `JoJot_IPC`, `Global\JoJot_SingleInstance`)
- Debug and production instances can run simultaneously without interference
- 5 new tests verify debug configuration uses correct suffixed names

## Task Commits

Each task was committed atomically:

1. **Task 1: Create AppEnvironment and wire into App.xaml.cs and IpcService** - `fde6d60` (feat)
2. **Task 2: Verify tests still pass and add AppEnvironment test** - `94e471d` (test)

## Files Created/Modified
- `JoJot/Services/AppEnvironment.cs` - Centralized debug/release environment detection with conditional compilation
- `JoJot/App.xaml.cs` - Replaced hardcoded path construction with AppEnvironment.AppDataDirectory and AppEnvironment.DatabasePath
- `JoJot/Services/IpcService.cs` - Changed PipeName/MutexName from const to static properties forwarding to AppEnvironment
- `JoJot.Tests/Services/AppEnvironmentTests.cs` - 5 tests verifying debug suffix on all environment-dependent values

## Decisions Made
- Used `#if DEBUG` conditional compilation rather than runtime detection -- zero overhead in release builds, compile-time guarantee of correct paths
- Changed `IpcService.PipeName` and `MutexName` from `const` to `static` property forwarding to `AppEnvironment` -- safe because these are only used at runtime (never in attribute arguments or switch cases)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Environment isolation is complete and verified
- No blockers

---
*Quick Task: 2-do-not-share-notes-db-between-debug-and-*
*Completed: 2026-03-10*
