---
phase: 01-add-in-editor-find-panel-with-ctrl-f
plan: 01
subsystem: ui
tags: [wpf, find-replace, usercontrol, tdd, theme]

# Dependency graph
requires: []
provides:
  - Enhanced FindAllMatches with caseSensitive and wholeWord optional parameters
  - ReplaceAll returning (NewContent, Count) tuple
  - ReplaceSingle for targeted single-match replacement
  - IsWholeWordMatch word-boundary helper
  - FindReplacePanel UserControl with slide animation, events, and option toggles
  - c-find-match-bg and c-find-match-active-bg theme resources in both light and dark themes
affects:
  - 01-02 (MainWindow wiring will subscribe to FindReplacePanel events and call these engine methods)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - FindReplacePanel follows PreferencesPanel slide-in/out pattern (TranslateTransform, CubicEase)
    - Event-driven panel: raises typed EventArgs (FindChangedEventArgs) to parent window
    - Find engine methods use optional bool params for backward-compatible extension

key-files:
  created:
    - JoJot/Controls/FindReplacePanel.xaml
    - JoJot/Controls/FindReplacePanel.xaml.cs
  modified:
    - JoJot/ViewModels/MainWindowViewModel.cs
    - JoJot/Themes/LightTheme.xaml
    - JoJot/Themes/DarkTheme.xaml
    - JoJot.Tests/ViewModels/FindEngineTests.cs

key-decisions:
  - "Extended FindAllMatches with default params (caseSensitive=false, wholeWord=false) for full backward compatibility with existing 2-param callers"
  - "Word boundary check uses IsLetterOrDigit — underscore is not a word character, matching common editor behavior"
  - "ReplaceAll builds result with forward StringBuilder pass using match positions from FindAllMatches"
  - "FindReplacePanel uses System.Windows.Media.Brush explicitly to resolve ambiguity with WinForms (project uses both UseWPF+UseWindowsForms)"

patterns-established:
  - "Panel slide animation: 320px offset, 250ms EaseOut show / 200ms EaseIn hide"
  - "Toggle buttons: accent background when active, transparent when inactive, white text on active"
  - "FindChangedEventArgs carries Query, CaseSensitive, WholeWord for single-event delivery to parent"

requirements-completed: [FIND-01, FIND-02, FIND-03]

# Metrics
duration: 4min
completed: 2026-03-10
---

# Phase 01 Plan 01: Find/Replace Foundation Summary

**Case-sensitive and whole-word find engine (tested), ReplaceAll/ReplaceSingle methods, FindReplacePanel UserControl with slide animation and option toggles, and theme highlight colors in both light and dark themes**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-10T16:29:29Z
- **Completed:** 2026-03-10T16:34:00Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- Extended FindAllMatches with caseSensitive and wholeWord options, fully backward-compatible via default params
- Added ReplaceAll (returns new content + count) and ReplaceSingle (targeted replacement) to the find engine
- Added 36 new TDD tests covering case-sensitive, whole-word, ReplaceAll, and ReplaceSingle scenarios (1064 total, all passing)
- Created FindReplacePanel UserControl following PreferencesPanel slide-in/out pattern with Find/Replace rows, option toggles, navigation buttons, and events to parent
- Added c-find-match-bg and c-find-match-active-bg to both LightTheme.xaml and DarkTheme.xaml

## Task Commits

Each task was committed atomically:

1. **Task 1: Enhance find engine with case/whole-word options and replace methods** - `74f3499` (feat)
2. **Task 2: Add theme highlight colors and create FindReplacePanel UserControl** - `96c31a5` (feat)

_Note: Task 1 used TDD (RED: compile errors, GREEN: all 48 FindEngine tests pass)_

## Files Created/Modified

- `JoJot/ViewModels/MainWindowViewModel.cs` - Extended FindAllMatches, added IsWholeWordMatch helper, ReplaceAll, ReplaceSingle
- `JoJot.Tests/ViewModels/FindEngineTests.cs` - Added 36 new tests for new find engine methods
- `JoJot/Controls/FindReplacePanel.xaml` - Find/replace panel UI with find input, option toggles, nav buttons, replace rows
- `JoJot/Controls/FindReplacePanel.xaml.cs` - Panel code-behind with Show/Hide animations, all events, toggle state management
- `JoJot/Themes/LightTheme.xaml` - Added c-find-match-bg (#FFFF8C) and c-find-match-active-bg (#FF9632)
- `JoJot/Themes/DarkTheme.xaml` - Added c-find-match-bg (#5A5A00) and c-find-match-active-bg (#C87820)

## Decisions Made

- Extended FindAllMatches with default params for backward compatibility — existing 2-param callers continue to work without changes
- Word boundary uses `char.IsLetterOrDigit` — underscore is not treated as a word character, consistent with common editor behavior
- ReplaceAll uses a forward StringBuilder pass through positions from FindAllMatches rather than replacing from end to start, which is simpler and handles all cases correctly
- Used `System.Windows.Media.Brush` fully-qualified in FindReplacePanel.xaml.cs to resolve ambiguity in the WPF+WinForms project

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ambiguous Brush reference in FindReplacePanel.xaml.cs**
- **Found during:** Task 2 (build verification)
- **Issue:** `Brush` was ambiguous between `System.Drawing.Brush` and `System.Windows.Media.Brush` because the project enables both UseWPF and UseWindowsForms
- **Fix:** Changed `(Brush)FindResource(...)` calls to `(System.Windows.Media.Brush)FindResource(...)` in `UpdateToggleVisual`
- **Files modified:** JoJot/Controls/FindReplacePanel.xaml.cs
- **Verification:** Build succeeded with 0 errors after fix
- **Committed in:** 96c31a5 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug)
**Impact on plan:** Trivial fix required by the project's WPF+WinForms dual-target setup. No scope creep.

## Issues Encountered

None beyond the Brush ambiguity auto-fix above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Find engine fully tested and ready to be called from MainWindow
- FindReplacePanel is self-contained and ready to be added to MainWindow.xaml layout
- Theme highlight colors are defined; next plan will implement the TextBoxHighlightAdorner usage
- Panel exposes all necessary events: CloseRequested, FindTextChanged, FindNextRequested, FindPreviousRequested, ReplaceRequested, ReplaceAllRequested

---
*Phase: 01-add-in-editor-find-panel-with-ctrl-f*
*Completed: 2026-03-10*
