---
phase: 15-review-round-2-ui-bug-fixes
plan: 07
subsystem: ui
tags: [wpf, drag-drop, virtual-desktop, session-management]

# Dependency graph
requires:
  - phase: 15-review-round-2-ui-bug-fixes/15-04
    provides: File drop overlay at root Grid level
  - phase: 15-review-round-2-ui-bug-fixes/15-05
    provides: Move overlay source name, keep-here visibility logic
provides:
  - File drop overlay works over entire window including TextBox editor
  - Reliable overlay dismiss via enter/leave counter pattern
  - Index-based desktop name fallback for un-renamed desktops
  - Full session metadata update (guid + name + index) on reparent
affects: [phase-14-installer]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Enter/leave counter for reliable WPF DragLeave across child boundaries"
    - "PreviewDragEnter/Leave tunneling events on Window element for full coverage"
    - "Index-based fallback for COM desktop names (mirrors Windows Task View)"

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/Services/DatabaseService.cs

key-decisions:
  - "Used Window Preview (tunneling) events instead of Grid bubbling events for drag coverage over TextBox"
  - "Enter/leave counter pattern chosen over position-based boundary check for DragLeave reliability"
  - "Created UpdateSessionDesktopAsync (renamed from UpdateSessionDesktopGuidAsync) to update all three columns with safety DELETE"
  - "VirtualDesktopService.GetAllDesktops index lookup for 'Desktop N' fallback matching Windows Task View behavior"

patterns-established:
  - "Enter/leave counter: Increment on PreviewDragEnter, decrement on PreviewDragLeave, hide overlay when counter reaches 0"
  - "Desktop name fallback: Check COM name first, fall back to 'Desktop {index+1}' from GetAllDesktops"

requirements-completed: [R2-DROP-01, R2-MOVE-01, R2-MENU-01]

# Metrics
duration: 5min
completed: 2026-03-05
---

# Phase 15 Plan 07: Gap Closure -- File Drop, Desktop Name, Session Reparent Summary

**File drop overlay works over entire window via tunneling events and enter/leave counter; move overlay shows 'Desktop N' for un-renamed desktops; session reparent preserves name/index for correct orphan detection**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-05T09:12:46Z
- **Completed:** 2026-03-05T09:17:41Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- File drop overlay now appears when dragging files over ANY part of the window, including the editor TextBox
- Overlay dismiss is reliable via enter/leave counter (replaces position-based boundary check that failed on child transitions)
- Move overlay shows "From: Desktop N" and "Moved to Desktop N" for un-renamed desktops instead of "Unknown desktop" / "another desktop"
- Session reparenting (DragKeepHere) updates desktop_name and desktop_index in app_state so Tier 3 matching detects orphans correctly on restart

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix file drop overlay to cover entire window including editor** - `a785631` (fix)
2. **Task 2: Fix desktop name fallback and session reparenting metadata** - `4021932` (fix)

## Files Created/Modified
- `JoJot/MainWindow.xaml` - Window-level Preview drag events, AllowDrop=False on ContentEditor, removed Grid drag handlers
- `JoJot/MainWindow.xaml.cs` - Enter/leave counter field, updated drag handlers, index-based desktop name fallback in ShowDragOverlayAsync, UpdateSessionDesktopAsync call in DragKeepHere_Click
- `JoJot/Services/DatabaseService.cs` - UpdateSessionDesktopGuidAsync renamed to UpdateSessionDesktopAsync with name/index parameters

## Decisions Made
- Used Window-level Preview (tunneling) events instead of Grid bubbling events to ensure drag events fire even when TextBox intercepts them
- Enter/leave counter pattern chosen over position-based GetPosition boundary check -- the position check was unreliable when crossing child element boundaries
- Created `UpdateSessionDesktopAsync` (replacing `UpdateSessionDesktopGuidAsync`) that updates all three columns (guid, name, index) with a safety DELETE for stale target sessions
- Desktop name fallback uses `VirtualDesktopService.GetAllDesktops()` index lookup to generate "Desktop N" labels matching Windows Task View behavior for un-renamed desktops

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Safety DELETE before UpdateSessionDesktopAsync**
- **Found during:** Task 2 (session reparenting)
- **Issue:** Plan suggested using existing `UpdateSessionAsync` which doesn't DELETE stale target sessions before UPDATE, risking UNIQUE constraint violations if orphaned app_state rows exist for the target GUID
- **Fix:** Created `UpdateSessionDesktopAsync` (enhanced version of `UpdateSessionDesktopGuidAsync`) that preserves the safety DELETE while adding name/index columns to the UPDATE
- **Files modified:** JoJot/Services/DatabaseService.cs
- **Verification:** Build succeeds, method signature matches all callers
- **Committed in:** 4021932 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Essential for database integrity during reparent. No scope creep.

## Issues Encountered
- JoJot.exe was running during initial build verification, causing MSB3027 file lock errors (not compilation errors). Killed process and confirmed clean build with 0 CS errors.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All 7 Phase 15 plans are now complete (15-01 through 15-07)
- All 18 R2 requirements addressed across the phase
- Ready for Phase 14 (Installer) or final verification pass

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-05*
