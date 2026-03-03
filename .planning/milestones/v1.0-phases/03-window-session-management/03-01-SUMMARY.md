---
phase: 03-window-session-management
plan: 01
subsystem: window-management
tags: [wpf, pinvoke, sqlite, geometry, winforms]

requires:
  - phase: 02-virtual-desktop-integration
    provides: "Desktop GUID identity and session matching for per-desktop state"
provides:
  - "WindowGeometry immutable record for per-desktop window position/size/maximized state"
  - "DatabaseService geometry CRUD (GetWindowGeometryAsync, SaveWindowGeometryAsync)"
  - "Idempotent schema migration adding window_state column to app_state"
  - "WindowPlacementHelper with P/Invoke GetWindowPlacement/SetWindowPlacement wrappers"
  - "Off-screen recovery via Screen.AllScreens intersection check"
affects: [03-02, 04, 10]

tech-stack:
  added: [System.Windows.Forms (Screen.AllScreens only), user32.dll P/Invoke]
  patterns: [P/Invoke struct marshaling, idempotent schema migration, off-screen clamp]

key-files:
  created:
    - JoJot/Models/WindowGeometry.cs
    - JoJot/Services/WindowPlacementHelper.cs
  modified:
    - JoJot/Services/DatabaseService.cs
    - JoJot/JoJot.csproj

key-decisions:
  - "UseWindowsForms=true with global using exclusion to avoid WPF Application ambiguity"
  - "ColumnExistsAsync via PRAGMA table_info for idempotent migrations (SQLite lacks IF NOT EXISTS for columns)"
  - "ClampToNearestScreen checks 50x50px region intersection, snaps to nearest screen by Manhattan distance"

patterns-established:
  - "P/Invoke struct layout: LayoutKind.Sequential for Win32 interop"
  - "Schema migration pattern: ColumnExistsAsync check before ALTER TABLE"
  - "Geometry uses workspace coordinates (GetWindowPlacement/SetWindowPlacement) not WPF coordinates"

requirements-completed: [TASK-04]

duration: 3min
completed: 2026-03-02
---

# Phase 3 Plan 01: Geometry Infrastructure Summary

**WindowGeometry model, DatabaseService geometry CRUD with parameterized queries, idempotent window_state migration, and WindowPlacementHelper P/Invoke with off-screen recovery**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-02T21:21:16Z
- **Completed:** 2026-03-02T21:23:53Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments
- WindowGeometry immutable record with Left, Top, Width, Height (double) and IsMaximized (bool)
- DatabaseService.GetWindowGeometryAsync and SaveWindowGeometryAsync with parameterized queries
- Idempotent migration adding window_state column via ColumnExistsAsync helper
- WindowPlacementHelper with CaptureGeometry, ApplyGeometry, ClampToNearestScreen
- UseWindowsForms enabled for Screen.AllScreens with WPF ambiguity resolved

## Task Commits

Each task was committed atomically:

1. **Task 1: WindowGeometry model and csproj update** - `dd53f72` (feat)
2. **Task 2: Geometry CRUD and schema migration** - `f4c353d` (feat)
3. **Task 3: WindowPlacementHelper with P/Invoke** - `0fbcc9b` (feat)

## Files Created/Modified
- `JoJot/Models/WindowGeometry.cs` - Immutable record for per-desktop geometry
- `JoJot/Services/WindowPlacementHelper.cs` - P/Invoke capture/apply/clamp
- `JoJot/Services/DatabaseService.cs` - Geometry CRUD + migration + ColumnExistsAsync
- `JoJot/JoJot.csproj` - UseWindowsForms=true with global using exclusion

## Decisions Made
- [03-01]: UseWindowsForms=true requires `<Using Remove="System.Windows.Forms" />` in csproj to avoid ambiguity between WPF and WinForms Application class
- [03-01]: ColumnExistsAsync uses PRAGMA table_info to check column existence — SQLite lacks ALTER TABLE ADD COLUMN IF NOT EXISTS
- [03-01]: ClampToNearestScreen checks 50x50px top-left region visibility and snaps to nearest screen by Manhattan distance

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] WPF/WinForms Application ambiguity**
- **Found during:** Task 1 (csproj update)
- **Issue:** Adding UseWindowsForms=true caused CS0104 ambiguity between System.Windows.Forms.Application and System.Windows.Application
- **Fix:** Added `<Using Remove="System.Windows.Forms" />` to csproj to exclude WinForms global using while keeping the assembly reference
- **Files modified:** JoJot/JoJot.csproj
- **Verification:** Build succeeds with zero errors
- **Committed in:** dd53f72 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Essential fix for WPF+WinForms coexistence. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Geometry infrastructure complete, ready for Plan 03-02 (window lifecycle)
- WindowPlacementHelper.CaptureGeometry and ApplyGeometry ready for MainWindow.OnClosing integration
- DatabaseService.GetWindowGeometryAsync and SaveWindowGeometryAsync ready for window factory

## Self-Check: PASSED

---
*Phase: 03-window-session-management*
*Completed: 2026-03-02*
