---
phase: 15-review-round-2-ui-bug-fixes
plan: 11
subsystem: interop, ui
tags: [virtual-desktop, registry, COM, overlay, WPF]

# Dependency graph
requires:
  - phase: 15-09
    provides: "Move overlay with live COM names and auto-dismiss logic"
provides:
  - "Registry-based desktop name fallback for Win11 25H2 when COM GetName() returns empty"
  - "Move overlay auto-dismissal when window returns to its original desktop"
affects: [virtual-desktop, window-title, move-overlay]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Registry fallback for COM API gaps (HKCU VirtualDesktops key)"

key-files:
  created: []
  modified:
    - "JoJot/Interop/VirtualDesktopInterop.cs"
    - "JoJot/MainWindow.xaml.cs"

key-decisions:
  - "Used fully-qualified Microsoft.Win32.Registry.CurrentUser to avoid using conflicts"
  - "Guid.ToString('B') format for registry key path matching Windows convention"

patterns-established:
  - "Registry fallback: when COM returns empty, read from HKCU registry as canonical source"

requirements-completed: [R2-MOVE-01, R2-MOVE-02]

# Metrics
duration: 3min
completed: 2026-03-05
---

# Phase 15 Plan 11: Desktop Name Registry Fallback and Move Overlay Dismiss Summary

**Registry-based desktop name fallback for Win11 25H2 COM GetName() gap, plus move overlay auto-dismiss on window return**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-05T11:46:51Z
- **Completed:** 2026-03-05T11:49:31Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Desktop names now resolve from Windows registry when COM GetName() returns empty (fixes Win11 25H2 build 26200+)
- Window titles and overlay labels show actual named desktop names instead of generic "Desktop N"
- Move overlay auto-dismisses when window is moved back to its original desktop
- Pending moves DB entries cleaned up on overlay dismissal

## Task Commits

Each task was committed atomically:

1. **Task 1: Add registry-based desktop name fallback in VirtualDesktopInterop** - `db6e1b5` (fix)
2. **Task 2: Dismiss move overlay when window returns to correct desktop** - `5cdb952` (fix)

## Files Created/Modified
- `JoJot/Interop/VirtualDesktopInterop.cs` - Added GetDesktopNameFromRegistry helper and registry fallback in GetCurrentDesktop() and GetAllDesktopsInternal()
- `JoJot/MainWindow.xaml.cs` - Added HideDragOverlayAsync and DeletePendingMoveAsync calls in else-if _isMisplaced branch

## Decisions Made
- Used fully-qualified `Microsoft.Win32.Registry.CurrentUser` to avoid namespace conflicts with existing usings
- Used `Guid.ToString("B")` format (braced) for registry key path, matching the Windows convention for virtual desktop GUIDs
- Placed registry read in a separate helper method for clean separation from COM code

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All Phase 15 gap closure plans (15-10 and 15-11) now complete
- Desktop name display and move overlay behavior fully resolved
- Ready for Phase 14 (Installer) or final verification

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-05*
