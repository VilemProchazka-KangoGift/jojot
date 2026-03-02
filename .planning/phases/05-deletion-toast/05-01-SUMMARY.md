---
phase: 05-deletion-toast
plan: 01
subsystem: ui
tags: [wpf, animation, storyboard, soft-delete, undo, toast, cancellation-token]

# Dependency graph
requires:
  - phase: 04-tab-management
    provides: ObservableCollection _tabs, SelectTabByNote, RebuildTabList, CreateNewTabAsync, MatchesSearch, DatabaseService.DeleteNoteAsync
provides:
  - PendingDeletion record for soft-delete lifecycle
  - DeleteTabAsync: single-tab soft-delete with toast + 4s hard-delete
  - DeleteMultipleAsync: bulk soft-delete skipping pinned tabs
  - CommitPendingDeletionAsync: hard-delete flush (called by new deletion, timer, shutdown)
  - UndoDeleteAsync: re-inserts tabs at original positions, cancels timer
  - ApplyFocusCascadeAsync: 3-tier focus after active tab deletion
  - ShowToast/HideToast: 150ms slide-up/slide-down storyboard animation
  - ToastBorder XAML overlay element in editor column
affects: [06-context-menu-delete, 07-keyboard-delete, 08-close-button-delete, future-delete-triggers]

# Tech tracking
tech-stack:
  added: [System.Windows.Media.Animation (Storyboard, DoubleAnimation, CubicEase)]
  patterns:
    - Soft-delete with CancellationTokenSource dismiss timer
    - Storyboard.SetTarget with object reference (avoids name-scope issues)
    - PendingDeletion record captures full restore state (tabs, indexes, CTS)

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "Storyboard.SetTarget uses object reference not element name — avoids WPF name-scope exceptions when animating programmatically"
  - "CommitPendingDeletionAsync nulls _pendingDeletion before Cancel+Dispose to prevent double-dispose races"
  - "UndoDeleteAsync inserts tabs in ascending originalIndex order with Math.Min clamping to handle shifted indexes correctly"
  - "ShowToast with TOST-04: content-swap only (no re-animation) when toast already Visible — new deletion commits previous via CommitPendingDeletionAsync"
  - "ApplyFocusCascadeAsync clears search text if search is hiding all remaining tabs before recursing"
  - "FlushAndClose calls CommitPendingDeletionAsync fire-and-forget — process stays alive long enough for DB write to complete"

patterns-established:
  - "Soft-delete pattern: remove from UI immediately, hold in PendingDeletion, hard-delete after 4s timer or next deletion"
  - "Toast show/hide: Storyboard with CubicEase, Visibility managed before/after animation"
  - "Focus cascade: first-below → last → clear-search-recurse → new-empty-tab"

requirements-completed: [TDEL-02, TDEL-05, TDEL-06, TOST-01, TOST-02, TOST-03, TOST-04, TOST-05, TOST-06]

# Metrics
duration: 4min
completed: 2026-03-02
---

# Phase 5 Plan 01: Deletion Engine & Toast Overlay Summary

**Soft-delete engine with 4s undo toast using WPF Storyboard animation, CancellationToken dismiss timer, and 3-tier focus cascade**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-02T22:23:38Z
- **Completed:** 2026-03-02T22:27:57Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Toast Border overlay in editor column with 150ms slide-up/slide-down animation via CubicEase Storyboard
- Complete soft-delete lifecycle: remove from UI, hold in PendingDeletion record, hard-delete after 4s timer
- UndoDeleteAsync restores tabs at original positions by inserting in ascending index order with clamping
- Bulk delete (DeleteMultipleAsync) silently skips pinned tabs; shows "N notes deleted" toast
- Focus cascade (ApplyFocusCascadeAsync): first tab at/below deleted index, then last visible, then clear search and recurse, then create new empty tab
- FlushAndClose commits pending deletions before window close to prevent data loss

## Task Commits

Tasks 1 and 2 were committed together due to a build dependency (UndoToast_Click in Task 1 references UndoDeleteAsync from Task 2):

1. **Task 1: Toast overlay XAML and animation methods** - `374dd5f` (feat)
   - Includes Task 2 deletion engine methods (required for successful compile)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `JoJot/MainWindow.xaml` - Added ToastBorder element in Grid.Column=2 with TranslateTransform, ToastMessageBlock TextBlock, Undo TextBlock wired to UndoToast_Click
- `JoJot/MainWindow.xaml.cs` - Added PendingDeletion record, _pendingDeletion field, CommitPendingDeletionAsync, DeleteTabAsync, DeleteMultipleAsync, StartDismissTimerAsync, UndoDeleteAsync, ApplyFocusCascadeAsync, ShowToast, HideToast, UpdateToastContent, UpdateToastContentBulk, UndoToast_Click; updated FlushAndClose to commit pending deletions

## Decisions Made

- **Storyboard.SetTarget vs. Storyboard.SetTargetName:** Used object reference API to avoid WPF name-scope exceptions when creating Storyboards programmatically in code-behind
- **CommitPendingDeletionAsync null-before-dispose:** `_pendingDeletion = null` is set before `Cancel()` and `Dispose()` to guard against double-dispose if the method is called concurrently
- **Ascending-order insert with clamping in UndoDeleteAsync:** Inserting restored tabs in ascending originalIndex order with `Math.Min(originalIndex, _tabs.Count)` handles index shifts when multiple tabs are restored
- **TOST-04 content-swap:** ShowToast detects `ToastBorder.Visibility == Visible` and returns early (content already updated) — no re-animation, previous pending deletion already committed by CommitPendingDeletionAsync called in DeleteTabAsync

## Deviations from Plan

None - plan executed exactly as written. Tasks 1 and 2 were implemented in the same edit session and committed together due to the forward reference from `UndoToast_Click` (Task 1) to `UndoDeleteAsync` (Task 2), which is a build-time requirement.

## Issues Encountered

- Initial build attempt after Task 1-only implementation failed because `UndoDeleteAsync` (Task 2) was referenced in `UndoToast_Click` (Task 1). Resolved by implementing Task 2 methods before the first commit, then committing all changes together.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Deletion engine is complete and ready for all five delete trigger phases (context menu, keyboard shortcut, close button, etc.)
- DeleteTabAsync and DeleteMultipleAsync are the sole entry points; trigger phases just call these methods
- FlushAndClose already handles pending deletion commits on shutdown
- No blockers for Phase 5 Plans 2+

---
*Phase: 05-deletion-toast*
*Completed: 2026-03-02*
