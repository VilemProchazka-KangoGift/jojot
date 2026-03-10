---
phase: 15-review-round-2-ui-bug-fixes
plan: 04
subsystem: ui
tags: [wpf, drag-drop, adorner, visual-brush, file-drop]

requires:
  - phase: 4-tab-management
    provides: Drag-to-reorder state, UpdateDropIndicator, CompleteDrag
  - phase: 9-hotkey-preferences
    provides: File drop handling, ProcessDroppedFilesAsync
provides:
  - DragAdorner inner class with 50% opacity VisualBrush ghost (R2-DND-01)
  - Smart indicator suppression at original and adjacent positions (R2-DND-02)
  - Full-window file drop via root Grid AllowDrop (R2-DROP-01)
  - Dropped files inserted at first position below pinned tabs (R2-DROP-01)
affects: [drag-reorder, file-import]

tech-stack:
  added: []
  patterns: [wpf-adorner-ghost, indicator-suppression]

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs
    - JoJot/MainWindow.xaml

key-decisions:
  - "DragAdorner uses VisualBrush at 0.5 opacity for ghost rendering"
  - "Original item becomes invisible (Opacity=0) but preserves space"
  - "Indicator suppressed at _dragOriginalListIndex and _dragOriginalListIndex+1"
  - "FileDropOverlay moved to root Grid with ColumnSpan=3 for full-window coverage"
  - "Dropped files shift existing unpinned sort orders up to make room at pinnedCount"

patterns-established:
  - "WPF adorner ghost pattern: VisualBrush of original element rendered via Adorner.OnRender"
  - "Full-window drop zone: AllowDrop on root Grid, overlay at root level with ColumnSpan"

requirements-completed: [R2-DND-01, R2-DND-02, R2-DROP-01]

duration: 12min
completed: 2026-03-04
---

# Plan 15-04: Drag Ghost Adorner + Smart Indicators + Full-Window File Drop Summary

**Added adorner-based drag ghost, suppressed useless drop indicators, and expanded file drop to entire window with first-position insertion**

## Performance

- **Duration:** 12 min
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Tab drag now shows a semi-transparent ghost following the cursor via WPF adorner layer
- Original tab becomes invisible but preserves its space in the list
- Drop indicators no longer appear at positions that wouldn't change order
- File drop works over the entire window (not just content area)
- Dropped files appear as first unpinned tabs instead of at the end

## Task Commits

1. **Task 1: DragAdorner + invisible original** - `55cb898` (feat)
2. **Task 2: Smart indicators + full-window drop + first-position insertion** - `55cb898` (feat, same commit)

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - DragAdorner class, drag state fields, adorner creation/cleanup, indicator suppression, ProcessDroppedFilesAsync rewrite
- `JoJot/MainWindow.xaml` - AllowDrop on root Grid, FileDropOverlay moved to root level with ColumnSpan=3

## Decisions Made
None - followed plan as specified

## Deviations from Plan
None - plan executed exactly as written

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Drag-and-drop UX significantly improved
- File drop now works from any part of the window
- Ready for Plan 05 (recovery sidebar)

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-04*
