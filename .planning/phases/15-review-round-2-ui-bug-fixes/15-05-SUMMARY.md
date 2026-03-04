---
phase: 15-review-round-2-ui-bug-fixes
plan: 05
subsystem: ui
tags: [wpf, recovery, sidebar, animation, virtual-desktop]

requires:
  - phase: 15-review-round-2-ui-bug-fixes/01
    provides: Fixed tab sizes, stable tab display
  - phase: 8-context-menu-recovery
    provides: RecoveryPanel modal, CreateRecoveryCard, orphan management
  - phase: 10-virtual-desktop
    provides: DragOverlay, ShowDragOverlayAsync, virtual desktop detection
provides:
  - Recovery sidebar with slide animation matching preferences panel (R2-RECOVER-01)
  - Tab name previews on recovery cards via GetNoteNamesForDesktopAsync (R2-RECOVER-01)
  - One-panel-at-a-time: recovery and preferences mutually exclusive
  - Source desktop name in move-to-desktop overlay via GetDesktopNameAsync (R2-MOVE-01)
  - "Keep here" hidden when target already has active JoJot window (R2-MOVE-02)
affects: [recovery, virtual-desktop-move]

tech-stack:
  added: []
  patterns: [sidebar-slide-animation, one-panel-at-a-time]

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/Services/DatabaseService.cs

key-decisions:
  - "Recovery panel Width=320 (slightly wider than preferences Width=300) for card content"
  - "RecoveryPanelTransform uses same slide animation pattern as PrefPanelTransform"
  - "Open button removed from recovery cards (Adopt + Delete only)"
  - "RecoveryBackdrop_Click removed (no backdrop in sidebar mode)"
  - "RecoveryClose_Click signature changed to MouseButtonEventArgs (TextBlock click)"
  - "GetDesktopNameAsync queries app_state table for desktop_name"

patterns-established:
  - "One-panel-at-a-time pattern: opening one sidebar closes the other"
  - "Sidebar slide pattern replicated: Border with TranslateTransform, 250ms ease-out in, 200ms ease-in out"

requirements-completed: [R2-RECOVER-01, R2-MOVE-01, R2-MOVE-02]

duration: 15min
completed: 2026-03-04
---

# Plan 15-05: Recovery Sidebar + Source Desktop Name + Keep-Here Visibility Summary

**Converted recovery from modal to sliding sidebar with tab previews, added source desktop name to move overlay, and hid "Keep here" when target already occupied**

## Performance

- **Duration:** 15 min
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Recovery panel slides in from right like preferences (320px wide)
- Recovery cards show tab name previews (first 5 names, italic, trimmed)
- Open button removed from recovery cards (only Adopt and Delete remain)
- One-panel-at-a-time: opening recovery closes preferences and vice versa
- Escape key closes recovery sidebar
- Move-to-desktop overlay shows "From: {desktop name}" above the title
- "Keep here" button hidden when target desktop already has an active JoJot window

## Task Commits

1. **Task 1: Recovery sidebar conversion** - `bb6b549` (feat)
2. **Task 2: Source name + Keep-here visibility** - `bb6b549` (feat, same commit)

## Files Created/Modified
- `JoJot/MainWindow.xaml` - RecoveryPanel converted to Border with TranslateTransform, DragOverlaySourceName added
- `JoJot/MainWindow.xaml.cs` - ShowRecoveryPanel/HideRecoveryPanel rewritten with animation, CreateRecoveryCard updated, ShowDragOverlayAsync updated
- `JoJot/Services/DatabaseService.cs` - GetNoteNamesForDesktopAsync and GetDesktopNameAsync added

## Decisions Made
None - followed plan as specified

## Deviations from Plan
None - plan executed exactly as written

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All 18 requirements from Phase 15 are complete
- Phase ready for verification

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-04*
