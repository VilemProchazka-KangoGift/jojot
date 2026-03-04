---
phase: 13-theme-display-menu-polish
plan: 01
subsystem: ui
tags: [wpf, theming, dark-mode, font-size, tab-labels]

# Dependency graph
requires:
  - phase: 12-tab-panel-improvements
    provides: Tab panel infrastructure, sidebar layout, _currentFontSize field
provides:
  - Dark mode tab text legibility via c-text-primary binding on labelBlock
  - Percentage-based font size display (FontSizeToPercent helper)
  - Tab label font scaling via RebuildTabList() in SetFontSizeAsync
affects: [future-theme-work, preferences-panel, tab-display]

# Tech tracking
tech-stack:
  added: []
  patterns: [SetResourceReference for theme-aware foreground, FontSizeToPercent static helper for consistent percentage display]

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "Used existing c-text-primary token (#D4D4D4 dark / #1A1A1A light) rather than creating a new c-tab-text token — sufficient contrast without new token overhead"
  - "13pt = 100% baseline for percentage calculation — clean values at common sizes (8pt=62%, 16pt=123%, 26pt=200%)"
  - "Tab labels scale via RebuildTabList() in SetFontSizeAsync — rebuilds all items with current _currentFontSize"

patterns-established:
  - "FontSizeToPercent(int size): static helper for font size display — use everywhere font size is shown to user"
  - "Tab label foreground: labelBlock.SetResourceReference(ForegroundProperty, c-text-primary) before placeholder check"

requirements-completed: [THEME-01, THEME-02]

# Metrics
duration: 12min
completed: 2026-03-04
---

# Phase 13 Plan 01: Theme Display & Menu Polish Summary

**Dark mode tab text legibility via c-text-primary binding, percentage-based font size display with FontSizeToPercent helper, and tab label scaling via RebuildTabList in SetFontSizeAsync**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-04T00:00:00Z
- **Completed:** 2026-03-04T00:12:00Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- Tab labels in dark mode now show #D4D4D4 text against dark sidebar (#252526) and selected-tab (#1A3A4A) — ~10:1 and ~7:1 contrast ratios
- Font size display converted from "Xpt" to percentage (e.g. "100%" at default 13pt) across all 4 display locations
- Tab labels scale dynamically when font size changes via Ctrl+=/Ctrl+-/Ctrl+Scroll — RebuildTabList() called in SetFontSizeAsync
- Keyboard shortcut help updated: "Reset font size (100%)" instead of "13pt"

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix dark mode tab text contrast (THEME-01)** - `dcf00af` (feat)
2. **Task 2: Convert font size display to % and scale tab labels (THEME-02)** - `7ac4ad8` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `JoJot/MainWindow.xaml.cs` - labelBlock foreground binding, FontSizeToPercent helper, tab font scaling, % display

## Decisions Made

- Used `c-text-primary` (existing token) instead of creating a new `c-tab-text` token — the existing token provides excellent contrast without adding theme complexity
- Baseline 13pt = 100% for percentage calculation — produces clean readable values at all supported font sizes (8-32pt)
- `RebuildTabList()` called at end of `SetFontSizeAsync` to propagate font size to existing tab items — leverages existing rebuild infrastructure

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Dark mode tab legibility fully resolved (THEME-01)
- Font size display is now percentage-based throughout (THEME-02)
- Ready for Phase 13 Plan 02 (window/menu polish: WIN-01, WIN-02)

---
*Phase: 13-theme-display-menu-polish*
*Completed: 2026-03-04*
