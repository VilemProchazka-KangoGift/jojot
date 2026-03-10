---
phase: 15-review-round-2-ui-bug-fixes
plan: 06
subsystem: ui
tags: [wpf, drag-and-drop, tab-interaction, fluent-icons, adorner]

# Dependency graph
requires:
  - phase: 15-review-round-2-ui-bug-fixes
    provides: "Tab pin/close buttons, drag-and-drop infrastructure from plans 03-05"
provides:
  - "Correct pinned tab hover glyph (E77A unpin icon)"
  - "Fluent ChromeClose icon (E711) for tab close button"
  - "Hover color feedback on unpinned pin buttons"
  - "Visible drag ghost using RenderTargetBitmap snapshot"
  - "Separator-aware drop indicator rendering"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "RenderTargetBitmap for drag adorner snapshot (frozen bitmap, DPI-aware)"
    - "Forward/backward scan for non-Border ListBoxItems in drop indicators"

key-files:
  created: []
  modified:
    - "JoJot/MainWindow.xaml.cs"

key-decisions:
  - "Unpin glyph E77A with Segoe Fluent Icons font for pinned hover (not multiplication sign)"
  - "ChromeClose glyph E711 at 10pt for close button (matches pin icon visual weight)"
  - "RenderTargetBitmap snapshot replaces live VisualBrush in DragAdorner"
  - "Forward-then-backward scan to find nearest Border item when drop target is separator"

patterns-established:
  - "RenderTargetBitmap: DPI-aware frozen snapshot for adorner visuals avoids live binding issues"

requirements-completed: [R2-TAB-01, R2-TAB-02, R2-DND-01]

# Metrics
duration: 4min
completed: 2026-03-05
---

# Phase 15 Plan 06: Tab Hover Glyphs, Close Icon, and Drag Ghost Fix Summary

**Pinned hover shows unpin glyph (E77A), close uses Fluent ChromeClose (E711), drag ghost captured as frozen bitmap before opacity=0**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-05T09:12:39Z
- **Completed:** 2026-03-05T09:16:34Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- Pinned tab hover now shows crossed-out pin glyph (E77A) instead of multiplication sign character
- Close icon uses Fluent ChromeClose (E711) at 10pt with proper icon font, matching pin icon visual weight
- Unpinned pin button changes to accent color on hover for visual feedback
- Drag ghost is a visible semi-transparent bitmap snapshot (not an invisible live VisualBrush reference)
- Drop indicators render correctly even when _dragInsertIndex points to a separator ListBoxItem

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix tab hover glyphs and colors** - `d409ef0` (fix)
2. **Task 2: Fix drag ghost visibility and separator-aware drop indicators** - `a785631` (fix, bundled with 15-07 concurrent execution)

**Note:** Task 2 changes were applied to working tree and committed alongside 15-07 changes in commit `a785631` due to concurrent plan execution on the same file.

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - Fixed pinned hover glyph, close icon font/size, unpinned hover handlers, DragAdorner bitmap snapshot, separator-aware drop indicators

## Decisions Made
- Used E77A (Segoe Fluent Icons unpin glyph) instead of multiplication sign for pinned hover -- stays in icon font, no font swap needed
- Used E711 (ChromeClose) at FontSize 10 for close button -- 10pt Fluent icon matches 12pt pin icon visual weight in 22x22 hit target
- Replaced VisualBrush with RenderTargetBitmap in DragAdorner -- bitmap is frozen snapshot, immune to source element opacity changes
- Forward-then-backward scan when drop target is separator -- ensures indicator always appears on nearest real tab item

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- JoJot.exe was locked by running instance during build verification -- used alternate output path (-p:OutputPath=bin/VerifyBuild) to bypass file lock
- Task 2 changes were committed by concurrent 15-07 plan execution (same file staged together) -- all changes verified present in HEAD

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All three gap-closure requirements (R2-TAB-01, R2-TAB-02, R2-DND-01) resolved
- Tab hover behavior matches design expectations
- Drag-and-drop visual and positional accuracy corrected

---
*Phase: 15-review-round-2-ui-bug-fixes*
*Completed: 2026-03-05*
