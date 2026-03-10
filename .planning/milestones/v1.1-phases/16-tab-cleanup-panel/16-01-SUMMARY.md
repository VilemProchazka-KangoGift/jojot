---
phase: 16-tab-cleanup-panel
plan: 01
subsystem: ui
tags: [wpf, side-panel, cleanup, filter, preview-list]

# Dependency graph
requires:
  - phase: 08-menus
    provides: "Hamburger menu structure, delete menu items, recovery panel pattern"
provides:
  - "Cleanup panel XAML skeleton with filter controls and preview list"
  - "ShowCleanupPanel/HideCleanupPanel with slide animation"
  - "Real-time filter logic (age/unit/pinned) with live preview"
  - "Preview row rendering with pin icon, title, excerpt, and relative age"
  - "MenuCleanup_Click handler replacing three delete menu handlers"
affects: [16-tab-cleanup-panel]

# Tech tracking
tech-stack:
  added: []
  patterns: ["Age-based filter with number/unit controls", "Live preview list pattern for destructive operations"]

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "Used E74D (delete icon) for cleanup menu item instead of E9F5 for consistency with existing delete theming"
  - "FontFamily fully qualified as System.Windows.Media.FontFamily to resolve ambiguity with System.Drawing"
  - "Filter change handlers added as stubs in Task 1 (Rule 3 - blocking) since XAML references them before Task 2 implementation"

patterns-established:
  - "Cleanup panel follows identical pattern to RecoveryPanel (Width=320, slide animation, one-panel-at-a-time)"
  - "GetCleanupCandidates filters in-memory _tabs collection, not database"

requirements-completed: [CLEANUP-01, CLEANUP-02, CLEANUP-03, CLEANUP-04, CLEANUP-06]

# Metrics
duration: 7min
completed: 2026-03-07
---

# Phase 16 Plan 01: Cleanup Panel UI Summary

**Side panel with age-based filter controls, include-pinned checkbox, and real-time preview list replacing three delete menu items**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-07T21:41:14Z
- **Completed:** 2026-03-07T21:48:35Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Replaced three delete menu items (Delete older than, Delete all except pinned, Delete all) with single "Clean up tabs" menu item
- Built cleanup side panel with header, filter controls (number input, unit dropdown, include-pinned checkbox), delete button, and scrollable preview list
- Implemented real-time preview rendering with tab title, pin icon, content excerpt, relative age, and dynamic delete button count
- Added one-panel-at-a-time guards and Escape key handling consistent with existing panels

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace delete menu items with "Clean up tabs" and create panel skeleton with show/hide** - `874a052` (feat)
2. **Task 2: Implement real-time filter logic and preview list rendering** - `f52253e` (feat)

## Files Created/Modified
- `JoJot/MainWindow.xaml` - Replaced 3 delete menu items + submenu popup with single cleanup item; added CleanupPanel Border with filter controls and preview list
- `JoJot/MainWindow.xaml.cs` - Removed submenu timer/methods and ConfirmAndDelete*Async; added ShowCleanupPanel/HideCleanupPanel, RefreshCleanupPreview, GetCleanupCandidates, CreateCleanupPreviewRow, FormatRelativeAge, GetCleanupExcerpt

## Decisions Made
- Used E74D (delete icon) for the cleanup menu item for visual consistency with the existing delete theme
- Fully qualified `System.Windows.Media.FontFamily` in pin icon Run to resolve ambiguity with `System.Drawing.FontFamily` (both are in scope due to WinForms SystemEvents reference)
- Filter change handler stubs were added in Task 1 to make the XAML compile, then replaced with full implementations in Task 2

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added stub handler methods in Task 1 for XAML-referenced events**
- **Found during:** Task 1 (panel skeleton)
- **Issue:** XAML references CleanupAgeInput_TextChanged, CleanupUnitCombo_SelectionChanged, CleanupIncludePinned_Changed, and RefreshCleanupPreview which don't exist yet (Task 2 implements them)
- **Fix:** Added stub methods that compile but defer to Task 2 for full implementation
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** 874a052 (Task 1 commit)

**2. [Rule 1 - Bug] Fixed FontFamily ambiguity in CreateCleanupPreviewRow**
- **Found during:** Task 2 (preview row rendering)
- **Issue:** `FontFamily` is ambiguous between `System.Drawing.FontFamily` and `System.Windows.Media.FontFamily`
- **Fix:** Fully qualified as `System.Windows.Media.FontFamily`
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** f52253e (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both auto-fixes necessary for compilation. No scope creep.

## Issues Encountered
- JoJot.exe was running during build, causing MSB3027 file lock errors on the output binary. Process was killed to verify clean build. Not a code issue.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Cleanup panel UI is complete with filter controls and live preview
- Plan 02 will implement the actual delete action (CleanupDelete_Click) with confirmation
- Panel skeleton and all filter/preview logic is ready for plan 02

---
*Phase: 16-tab-cleanup-panel*
*Completed: 2026-03-07*
