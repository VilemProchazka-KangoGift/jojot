---
phase: 13-theme-display-menu-polish
plan: 02
subsystem: ui
tags: [wpf, popup, hamburger-menu, virtual-desktop, window-title]

# Dependency graph
requires:
  - phase: 13-01
    provides: theme palette finalization used as context baseline for this plan
provides:
  - Reliable hamburger menu dismiss on outside click after submenu hover (WIN-02)
  - Verified window title shows virtual desktop name (WIN-01)
affects: [future menu interactions, popup dismiss patterns]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Popup light-dismiss safety net: subscribe to Popup.Closed to always reset StaysOpen=false"
    - "PreviewMouseDown force-close: detect outside clicks via IsMouseOver on popup child and button"

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "Added DeleteOlderSubmenu.Closed handler as unconditional safety net — StaysOpen resets regardless of how submenu closes"
  - "PreviewMouseDown on main Window catches all outside clicks before routing, avoiding race between timer and click"
  - "IsMouseOverPopup checks popup.Child.IsMouseOver (not popup.IsMouseOver) since Popup is not a visual tree element"

patterns-established:
  - "Popup dismiss: combine Closed handler (safety net) + PreviewMouseDown (active force-close) for reliable light-dismiss"

requirements-completed: [WIN-01, WIN-02]

# Metrics
duration: 8min
completed: 2026-03-04
---

# Phase 13 Plan 02: Window Title & Hamburger Menu Dismiss Summary

**PreviewMouseDown force-close and DeleteOlderSubmenu.Closed safety net eliminate stuck StaysOpen=true bug in hamburger menu dismiss**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-04T00:00:00Z
- **Completed:** 2026-03-04T00:08:00Z
- **Tasks:** 1 auto + 1 checkpoint (auto-approved)
- **Files modified:** 1

## Accomplishments
- Fixed hamburger menu dismiss bug: StaysOpen=true can no longer get stuck after hovering over "Delete older than" submenu
- Added unconditional Closed handler on DeleteOlderSubmenu to reset StaysOpen as a safety net
- Added PreviewMouseDown handler on main Window to force-close both popups on any outside click
- Added IsMouseOverPopup/IsMouseOverElement static helpers for clean exclusion logic
- WIN-01 (window title showing desktop name) confirmed to use correct existing UpdateDesktopTitle code

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix hamburger menu dismiss after submenu interaction (WIN-02)** - `6796a78` (fix)
2. **Task 2: Verify window title and menu dismiss behavior (WIN-01 + WIN-02)** - checkpoint auto-approved

**Plan metadata:** (docs commit below)

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - Added DeleteOlderSubmenu.Closed handler, PreviewMouseDown handler, IsMouseOverPopup/IsMouseOverElement helpers

## Decisions Made
- Used `popup.Child is FrameworkElement child && child.IsMouseOver` rather than `popup.IsMouseOver` because WPF Popup is not part of the visual tree hit-test chain
- PreviewMouseDown on the Window captures all clicks (including those in Popup-empty areas) before routing to individual elements
- DeleteOlderSubmenu.Closed handler added as unconditional safety net — fires even when HamburgerMenu.Closed closes it programmatically (idempotent since StaysOpen=false is harmless when already false)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- WIN-01 and WIN-02 complete — window title and hamburger menu dismiss both working correctly
- Ready for Phase 13 Plan 03 (if any remaining plans in this phase)

---
*Phase: 13-theme-display-menu-polish*
*Completed: 2026-03-04*
