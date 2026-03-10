---
phase: 01-add-in-editor-find-panel-with-ctrl-f
plan: 02
subsystem: ui
tags: [find, replace, wpf, keyboard, panel, mainwindow]

requires:
  - phase: 01-01
    provides: FindReplacePanel UserControl, enhanced find engine, TextBoxHighlightAdorner

provides:
  - Fully wired find/replace side panel in MainWindow
  - Ctrl+F opens find panel, Ctrl+H opens find+replace panel
  - Toolbar Find button (E721 search icon)
  - Selection auto-populates find input
  - Real-time search-as-you-type with match highlighting
  - Match navigation (Enter/Shift+Enter, prev/next buttons)
  - Replace single match with auto-advance to next
  - Replace All with single undo checkpoint and replacement count feedback
  - Tab-switch re-search when find panel is open
  - One-panel-at-a-time enforcement (find closes other panels and vice versa)
  - Old inline EditorFindBar completely removed

affects: [01-03]

tech-stack:
  added: []
  patterns:
    - "WireUpFindPanelEvents() wires both FindReplacePanel events and adorner scroll/theme tracking"
    - "CloseAllSidePanels() in ViewModel updated to include IsFindPanelOpen = false"
    - "_findPanelOpen forwarding property follows existing panel pattern (_preferencesOpen, etc.)"

key-files:
  created: []
  modified:
    - JoJot/ViewModels/MainWindowViewModel.cs
    - JoJot/Views/MainWindow.xaml
    - JoJot/Views/MainWindow.xaml.cs
    - JoJot/Views/MainWindow.Search.cs
    - JoJot/Views/MainWindow.Keyboard.cs
    - JoJot/Views/MainWindow.Toolbar.cs
    - JoJot/Views/MainWindow.Preferences.cs
    - JoJot/Views/MainWindow.Cleanup.cs
    - JoJot/Views/MainWindow.Recovery.cs
    - JoJot/Views/MainWindow.Tabs.cs
    - JoJot/Controls/FindReplacePanel.xaml.cs

key-decisions:
  - "Ctrl+F always opens the side panel (no longer context-dependent on editor focus)"
  - "Replace All uses PushSnapshot before replacement for single-undo semantics"
  - "WireAdornerEvents merged into WireUpFindPanelEvents to reduce constructor call count"
  - "ShowInfoToast used for Replace All feedback (reuses existing toast infrastructure)"

requirements-completed: [FIND-01, FIND-02, FIND-04, FIND-05]

duration: 35min
completed: 2026-03-10
---

# Phase 01 Plan 02: FindReplacePanel Wiring Summary

**Ctrl+F/H find+replace panel fully wired into MainWindow with real-time search, navigation, replace, and tab-switch re-search; old inline EditorFindBar removed**

## Performance

- **Duration:** 35 min
- **Completed:** 2026-03-10
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments

- Added `IsFindPanelOpen` to `MainWindowViewModel` with `SetProperty` backing; `CloseAllSidePanels()` now includes find panel
- Removed `EditorFindBar` Grid from `MainWindow.xaml` (old inline bottom find bar)
- Added `FindReplacePanel` instance to `MainWindow.xaml` root grid (ZIndex 85, right-aligned, Width=320)
- Added toolbar Find button with search icon (Segoe Fluent E721) after Save button
- Rewrote `MainWindow.Search.cs`: removed old EditorFindBar code, added full Find & Replace Panel section
- Ctrl+F opens find panel (always, no longer context-dependent), Ctrl+H opens find+replace panel
- Escape closes find panel if open (before other panel escape handlers)
- Selection auto-populates find input on Ctrl+F
- Real-time search with adorner highlighting via `RunSearch`
- Enter/Shift+Enter cycle through matches; prev/next buttons work via `CycleFindMatch`
- `PerformReplace` replaces current match and re-searches (advancing to next)
- `PerformReplaceAll` pushes undo snapshot, replaces all, shows toast with count
- `RefreshFindIfPanelOpen` called on tab switch for persistent find across tabs
- All other panel show methods (prefs, cleanup, recovery) now close find panel first

## Task Commits

1. **Task 1: ViewModel state, XAML wiring, remove EditorFindBar** - `440c590` (feat)
2. **Task 2: Keyboard shortcuts, panel events, replace operations** - `c573726` (feat)

## Files Created/Modified

- `JoJot/ViewModels/MainWindowViewModel.cs` - IsFindPanelOpen property, CloseAllSidePanels includes find panel
- `JoJot/Views/MainWindow.xaml` - FindReplacePanel instance, toolbar Find button, EditorFindBar removed
- `JoJot/Views/MainWindow.xaml.cs` - _findPanelOpen forwarding property, WireUpFindPanelEvents() call
- `JoJot/Views/MainWindow.Search.cs` - Complete rewrite: new Find & Replace Panel section, adorner helpers
- `JoJot/Views/MainWindow.Keyboard.cs` - Ctrl+F always opens panel, new Ctrl+H, Escape uses _findPanelOpen
- `JoJot/Views/MainWindow.Toolbar.cs` - ToolbarFind_Click handler
- `JoJot/Views/MainWindow.Preferences.cs` - ShowPreferencesPanel closes find panel
- `JoJot/Views/MainWindow.Cleanup.cs` - ShowCleanupPanel closes find panel
- `JoJot/Views/MainWindow.Recovery.cs` - ShowRecoveryPanel closes find panel
- `JoJot/Views/MainWindow.Tabs.cs` - TabList_SelectionChanged calls RefreshFindIfPanelOpen
- `JoJot/Controls/FindReplacePanel.xaml.cs` - Added ShowReplaceCount method

## Decisions Made

- Ctrl+F always opens the side panel — no longer context-dependent on whether the editor is focused. Tab search box remains available by clicking it directly.
- Replace All uses `PushSnapshot` before the replacement for single-undo semantics (one Ctrl+Z undoes the entire Replace All)
- `WireUpFindPanelEvents()` combines adorner scroll/theme wiring with FindReplacePanel event subscriptions (one method called from constructor)
- Replace All feedback shown via existing `ShowInfoToast` rather than a new panel-internal mechanism (reuses infrastructure)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed naming inconsistency: WireAdornerEvents merged into WireUpFindPanelEvents**
- **Found during:** Task 2 (build verification)
- **Issue:** Linter reorganized MainWindow.Search.cs, combining WireAdornerEvents and WireUpFindPanelEvents into a single method; constructor still called old WireAdornerEvents
- **Fix:** Removed WireAdornerEvents() call from constructor (now handled by WireUpFindPanelEvents)
- **Files modified:** JoJot/Views/MainWindow.xaml.cs
- **Committed in:** c573726

**2. [Rule 2 - Missing Critical] Added ShowReplaceCount to FindReplacePanel**
- **Found during:** Task 2 (PerformReplaceAll implementation)
- **Issue:** PerformReplaceAll needed to show replacement count to user; FindReplacePanel had ReplaceCountText element but no public method to display it
- **Fix:** Added ShowReplaceCount(int count) method with 3-second auto-dismiss timer
- **Files modified:** JoJot/Controls/FindReplacePanel.xaml.cs
- **Committed in:** c573726

---

**Total deviations:** 2 auto-fixed (1 bug, 1 missing method)
**Impact on plan:** Both minor; no scope creep.

## Issues Encountered

- The linter periodically reverted changes to files mid-edit, requiring re-applying changes. All changes were ultimately applied correctly.
- Plan 01-01 work was pre-committed before this plan ran; the adorner infrastructure was treated as part of Plan 01-01.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Find+replace panel fully functional and wired
- TextBoxHighlightAdorner infrastructure ready for Plan 01-03 (visual highlighting integration)
- All 1064 tests pass (no regressions)

## Self-Check: PASSED

- `IsFindPanelOpen` in MainWindowViewModel: FOUND
- `FindReplacePanel` in MainWindow.xaml: FOUND
- `ToolbarFind` in MainWindow.xaml: FOUND
- `ShowFindPanel` in MainWindow.Search.cs: FOUND
- `Key.H` in MainWindow.Keyboard.cs: FOUND
- Build: succeeded (0 errors)
- Tests: 1064 passed (0 failed)
- No EditorFindBar references in source .cs or .xaml files

---
*Phase: 01-add-in-editor-find-panel-with-ctrl-f*
*Completed: 2026-03-10*
