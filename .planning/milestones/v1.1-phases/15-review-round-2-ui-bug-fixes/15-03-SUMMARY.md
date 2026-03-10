---
phase: 15-review-round-2-ui-bug-fixes
plan: 03
subsystem: ui
tags: [wpf, tabs, buttons, hit-targets, hover]

requires:
  - phase: 4-tab-management
    provides: CreateTabListItem, tab hover/click handlers
provides:
  - Pin/close buttons with 22x22 Border hit targets (R2-TAB-01)
  - Unpinned tabs show pin (left) + close (right) on hover and selection
  - Pinned tabs show pin icon that swaps to X on hover for unpin (R2-TAB-02)
  - Buttons visible on selected tab without hover (R2-TAB-03)
  - DelayedCollapse utility method for fade-then-collapse pattern
affects: [tab-display, drag-reorder]

tech-stack:
  added: []
  patterns: [border-button-hit-target, delayed-collapse]

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "22x22 Border with CornerRadius(3) used as button container for hit targets"
  - "Pin icon uses Segoe Fluent Icons E718, close X uses unicode 00D7 at FontSize 14"
  - "Pinned tab pin button always visible; unpinned tab buttons hidden by default"
  - "ApplyActiveHighlight walks visual tree to find 22x22 Border buttons"

patterns-established:
  - "Border-as-button pattern: 22x22 Border wrapping TextBlock icon for consistent hit targets"
  - "DelayedCollapse: timer-based Visibility.Collapsed after opacity fade-out completes"

requirements-completed: [R2-TAB-01, R2-TAB-02, R2-TAB-03]

duration: 10min
completed: 2026-03-04
---

# Plan 15-03: Tab Button Layout Redesign Summary

**Redesigned tab pin/close buttons with 22x22 hit targets, hover-to-unpin for pinned tabs, and selection-aware visibility**

## Performance

- **Duration:** 10 min
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- Unpinned tabs have pin button (left) and close X (right) with 22x22 hit targets
- Pinned tabs show pin icon that swaps to red X on hover for intuitive unpin
- Selected tab shows both buttons without requiring hover
- Deselected tabs hide buttons (except pinned tab's always-visible pin icon)
- Added DelayedCollapse utility for clean fade-then-collapse animation

## Task Commits

1. **Task 1: Redesign tab button layout** - `42acb5a` (feat)
2. **Task 2: Show buttons on selected tabs** - `42acb5a` (feat, same commit)

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - CreateTabListItem rewrite, hover handlers, ApplyActiveHighlight, deselection logic, DelayedCollapse

## Decisions Made
None - followed plan as specified

## Deviations from Plan
None - plan executed exactly as written

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Tab buttons now have proper hit targets and hover behavior
- Ready for drag-and-drop improvements (Plan 04)

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-04*
