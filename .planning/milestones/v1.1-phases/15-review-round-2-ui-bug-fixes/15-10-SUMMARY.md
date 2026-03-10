---
phase: 15-review-round-2-ui-bug-fixes
plan: 10
subsystem: ui
tags: [wpf, drag-drop, tab-layout, listbox, adorner]

# Dependency graph
requires:
  - phase: 15-review-round-2-ui-bug-fixes
    provides: "Tab hover icons (22x22 Border) and drag ghost adorner from plans 15-03, 15-06, 15-08"
provides:
  - "Jitter-free tab hover with MinHeight = 22 on row0 Grid"
  - "Smooth drag ghost tracking across entire TabList including empty space"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MinHeight on layout Grid to prevent Visibility toggle jitter"
    - "Parent-level PreviewMouseMove fallback for adorner tracking in empty space"

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "MinHeight = 22 matches icon Border height, accommodates 13pt title text (~17-18px)"
  - "Fallback handler is idempotent with TabItem_PreviewMouseMove -- double position update is harmless"

patterns-established:
  - "MinHeight guard on Grid rows containing toggled-visibility children"
  - "TabList-level PreviewMouseMove as catch-all for drag adorner updates"

requirements-completed: [R2-TAB-01, R2-DND-01]

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 15 Plan 10: Tab Hover Height Fix and Drag Ghost Empty-Space Tracking Summary

**MinHeight=22 on tab row Grid eliminates hover jitter; TabList PreviewMouseMove fallback keeps drag ghost smooth in empty space between items**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T11:46:50Z
- **Completed:** 2026-03-05T11:48:30Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Fixed tab hover height jitter by setting row0.MinHeight = 22 so toggling pin/close icon Visibility no longer changes row height
- Added TabList.PreviewMouseMove fallback handler so drag ghost adorner tracks cursor in empty space between ListBoxItems
- Both fixes are minimal, targeted changes with no side effects

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix tab hover height jitter and add drag ghost empty-space tracking** - `d052984` (fix)

**Plan metadata:** pending (docs: complete plan)

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - Added row0.MinHeight = 22, TabList.PreviewMouseMove handler wiring, and TabList_PreviewMouseMove_DragFallback method

## Decisions Made
- MinHeight = 22 chosen to match the 22x22 Border hit targets for pin/close icons, which is the maximum child height in the row
- Fallback handler placed at TabList level fires for all mouse moves during drag; the position update is idempotent so it harmlessly overlaps with TabItem_PreviewMouseMove

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- R2-TAB-01 and R2-DND-01 gap closure requirements now resolved
- Ready for plan 15-11 or final phase verification

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-05*
