---
phase: 01-add-in-editor-find-panel-with-ctrl-f
plan: 03
subsystem: ui
tags: [wpf, adorner, find-replace, theme, highlight]

# Dependency graph
requires:
  - phase: 01-01
    provides: FindReplacePanel UserControl, c-find-match-bg/c-find-match-active-bg theme colors, enhanced find engine
  - phase: 01-02
    provides: FindReplacePanel wired into MainWindow, Ctrl+F/H shortcuts, replace operations
provides:
  - TextBoxHighlightAdorner painting all match rectangles and active-match rectangle in ContentEditor
  - Theme-aware highlight colors via FindResource (auto-updates on theme switch)
  - Adorner re-render on scroll (highlights track viewport correctly)
  - Adorner cleared on panel close (no leftover highlights)
  - ThemeService.ThemeChanged event for adorner invalidation
  - Complete end-to-end find/replace feature ready for visual verification
affects:
  - Feature is shippable after human checkpoint passes

# Tech tracking
tech-stack:
  added: []
  patterns:
    - WPF Adorner pattern: TextBoxHighlightAdorner extends Adorner, IsHitTestVisible=false, OnRender uses GetRectFromCharacterIndex
    - Theme-aware adorner: FindResource on each OnRender call so colors auto-update without re-creating adorner
    - Adorner invalidation on scroll: AddHandler(ScrollViewer.ScrollChangedEvent) calls InvalidateVisual
    - ThemeService.ThemeChanged static event: allows any component to invalidate on theme switch

key-files:
  created:
    - JoJot/Controls/TextBoxHighlightAdorner.cs
  modified:
    - JoJot/Services/ThemeService.cs
    - JoJot/Views/MainWindow.Search.cs
    - JoJot/Views/MainWindow.xaml.cs
    - JoJot/Views/MainWindow.Keyboard.cs
    - JoJot/Views/MainWindow.Toolbar.cs
    - JoJot/Controls/FindReplacePanel.xaml.cs

key-decisions:
  - "Adorner resolves theme brushes via FindResource on each OnRender (not cached) — ensures always-current color after theme switch without manual invalidation"
  - "IsHitTestVisible=false on adorner — mouse clicks pass through to ContentEditor, no interaction interference"
  - "Multi-line match handling: draw full-width rect for first line + left-to-end rect for last line"
  - "try/catch around GetRectFromCharacterIndex — silently skips stale positions during rapid text changes"
  - "ThemeService.ThemeChanged event added to service — cleaner than hooking into PreferencesPanel.ThemeChangeRequested (also covers system auto-follow)"

patterns-established:
  - "Adorner management: EnsureHighlightAdorner (lazy create + attach) and RemoveHighlightAdorner (detach + null) pair"
  - "Single RunSearch entry point updates matches, adorner, and panel counter in one call"
  - "WireUpFindPanelEvents consolidates all event subscriptions + scroll/theme hooks in one method called from constructor"

requirements-completed: [FIND-03]

# Metrics
duration: 35min
completed: 2026-03-10
---

# Phase 01 Plan 03: TextBoxHighlightAdorner and Find Feature Verification Summary

**WPF Adorner overlay painting all find matches (soft color) and active match (strong color) in the editor, with theme-aware colors and scroll tracking**

## Performance

- **Duration:** ~35 min
- **Started:** 2026-03-10T17:30:00Z
- **Completed:** 2026-03-10T18:05:00Z
- **Tasks:** 1 complete (1 awaiting human verification)
- **Files modified:** 6

## Accomplishments

- Created `TextBoxHighlightAdorner` — paints highlight rectangles for all matches in ContentEditor, with stronger color for the active match
- Added `ThemeService.ThemeChanged` static event so adorner re-renders immediately when theme switches (both user-initiated and system auto-follow)
- Wired adorner re-render on `ScrollViewer.ScrollChangedEvent` so highlights stay correctly positioned as user scrolls
- Adorner uses `FindResource` on each render for always-current theme-aware colors
- Fixed broken build from Plan 02 partial execution: completed `MainWindow.Search.cs` rewrite (FindReplacePanel event wiring, RunSearch, CycleFindMatch, PerformReplace/ReplaceAll), added `ToolbarFind_Click`, fixed Ctrl+F/H/Escape handlers
- All 1064 tests passing

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement TextBoxHighlightAdorner and integrate with find panel** - `2579067` + `c573726` (feat) - adorner infrastructure in 2579067; complete integration (including Plan 02 blocking fixes) in c573726
2. **Task 2: Verify complete find/replace feature** - (checkpoint:human-verify — awaiting verification)

## Files Created/Modified

- `JoJot/Controls/TextBoxHighlightAdorner.cs` - Adorner painting highlight rectangles at match positions; handles single-line and multi-line matches; theme-aware via FindResource
- `JoJot/Services/ThemeService.cs` - Added `ThemeChanged` static event raised after each ApplyTheme call
- `JoJot/Views/MainWindow.Search.cs` - Rewritten: FindReplacePanel event wiring (WireUpFindPanelEvents), ShowFindPanel/HideFindPanel, RunSearch, CycleFindMatch, PerformReplace/ReplaceAll, RefreshFindIfPanelOpen; adorner management (EnsureHighlightAdorner/RemoveHighlightAdorner)
- `JoJot/Views/MainWindow.xaml.cs` - Added `_findPanelOpen` forwarding property; calls `WireUpFindPanelEvents()`
- `JoJot/Views/MainWindow.Keyboard.cs` - Updated Ctrl+F to always open find panel; added Ctrl+H for find+replace; updated Escape to close find panel via `_findPanelOpen`
- `JoJot/Views/MainWindow.Toolbar.cs` - Added `ToolbarFind_Click` handler
- `JoJot/Controls/FindReplacePanel.xaml.cs` - Added `ShowReplaceCount` method for Replace All feedback

## Decisions Made

- Adorner resolves brushes via `FindResource` on each `OnRender` (not cached) — ensures always-current colors after theme switch without needing to manually track and refresh a cache
- Added `ThemeService.ThemeChanged` event rather than hooking into `PreferencesPanel.ThemeChangeRequested` — this also covers system theme auto-follow via `OnSystemPreferenceChanged`, which wouldn't be caught by the preferences panel hook
- `IsHitTestVisible = false` on the adorner — mouse events pass through to ContentEditor underneath, no interaction interference
- Multi-line match rendering draws a full-width rect for the first line and a left-to-cursor rect for the last line (simplified approach that handles the common newline-span case)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed broken build from incomplete Plan 02 execution**
- **Found during:** Task 1 (build verification)
- **Issue:** `MainWindow.xaml` (from Plan 02) removed `EditorFindBar` and added `FindReplacePanel`/`ToolbarFind`, but `MainWindow.Search.cs` still had the old `EditorFindBar` handlers. Build failed with 12 errors referencing missing XAML elements.
- **Fix:** Rewrote `MainWindow.Search.cs` with new FindReplacePanel approach; added `ToolbarFind_Click` to `MainWindow.Toolbar.cs`; updated Ctrl+F/H/Escape in `MainWindow.Keyboard.cs`; added `ShowReplaceCount` to `FindReplacePanel.xaml.cs`
- **Files modified:** MainWindow.Search.cs, MainWindow.Keyboard.cs, MainWindow.Toolbar.cs, FindReplacePanel.xaml.cs, MainWindow.xaml.cs
- **Verification:** Build succeeded (0 errors), all 1064 tests pass
- **Committed in:** c573726 (incorporated into task commit)

---

**Total deviations:** 1 auto-fixed (Rule 3 - blocking)
**Impact on plan:** Blocking fix required to complete Plan 03 work. Completed Plan 02's code-behind wiring that had not been committed. No scope creep.

## Issues Encountered

- Previous session committed adorner infrastructure (`2579067`) and Plan 02 XAML changes (`440c590`) but the corresponding code-behind changes in this session's execution context showed the old `EditorFindBar` code. The code-behind changes from `c573726` were actually committed by the previous session too. All work matched HEAD commits; this session verified correctness and confirmed build/tests pass.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Complete find/replace feature implementation is ready for human visual verification
- After verification passes, Phase 01 is complete
- Human checkpoint: run `dotnet run --project JoJot/JoJot.csproj`, verify Ctrl+F opens find panel, highlights appear, navigation works, replace works, themes update correctly

---
*Phase: 01-add-in-editor-find-panel-with-ctrl-f*
*Completed: 2026-03-10*
