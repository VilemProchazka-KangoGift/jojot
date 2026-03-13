---
phase: quick-14
plan: 01
subsystem: ui
tags: [wpf, datatemplates, dispatcher, visual-tree, tab-highlight]

requires: []
provides:
  - Deferred ApplyActiveHighlight that handles DataTemplate not yet rendered
affects: [tab-selection, pin-toggle, window-open]

tech-stack:
  added: []
  patterns: ["Defer visual-tree operations to DispatcherPriority.Loaded when FindNamedDescendant returns null"]

key-files:
  created: []
  modified:
    - JoJot/Views/MainWindow.Tabs.cs

key-decisions:
  - "Added isDeferred bool guard to prevent infinite deferral in pathological cases where template still absent after Loaded pass"
  - "Single retry at DispatcherPriority.Loaded is sufficient — WPF guarantees templates are applied by that point"

patterns-established:
  - "Visual-tree deferral pattern: if FindNamedDescendant returns null, schedule retry at DispatcherPriority.Loaded with a one-shot guard"

requirements-completed: [FIX-HIGHLIGHT]

duration: 5min
completed: 2026-03-14
---

# Quick Task 14: Fix Tab Highlight Missing on Window Open Summary

**Deferred ApplyActiveHighlight via DispatcherPriority.Loaded so selected-tab background shows on window open and after pin/unpin**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-14
- **Completed:** 2026-03-14
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Selected tab now shows `c-selected-bg` highlight when window first opens (LoadTabsAsync path)
- Selected tab retains highlight after pin/unpin toggle (TogglePinAsync -> RebuildTabList path)
- One-shot deferral guard (`isDeferred` parameter) prevents runaway recursion in edge cases

## Task Commits

1. **Task 1: Defer ApplyActiveHighlight when DataTemplate visual tree is not ready** - `3a49f9f` (fix)

## Files Created/Modified
- `JoJot/Views/MainWindow.Tabs.cs` - Added `isDeferred` parameter and deferred retry at `DispatcherPriority.Loaded`

## Decisions Made
- Used a `bool isDeferred = false` guard parameter rather than a raw retry counter — clearer intent and zero overhead for the normal path (template already rendered on tab click)
- `DispatcherPriority.Loaded` chosen to match the existing pattern in the same file (scroll restore at line 377, ScrollIntoView at line 459)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. Build passed with 0 warnings, 0 errors. 1 pre-existing test failure (`DisplayLabel_Content31Chars_Truncated`) confirmed present before the change and unrelated.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Fix is self-contained. No follow-up needed.

---
*Phase: quick-14*
*Completed: 2026-03-14*
