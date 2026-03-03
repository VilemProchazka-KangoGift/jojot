---
phase: 08-menus-context-actions-orphaned-sessions
plan: 02
subsystem: ui
tags: [wpf, csharp, confirmation-dialog, bulk-delete, modal-overlay, soft-delete, toast]

# Dependency graph
requires:
  - phase: 08-01
    provides: ConfirmationOverlay XAML (ConfirmTitle, ConfirmMessage, ConfirmCancelButton, ConfirmDeleteButton), ShowRecoveryPanel stub, _confirmAction field placeholder, hamburger menu click handlers calling ConfirmAndDelete* methods
  - phase: 05-deletion-toast
    provides: DeleteMultipleAsync (soft-delete with pinned-skip + undo toast), ShowToast, PendingDeletion infrastructure
provides:
  - ShowConfirmation() helper — sets title/message/callback, shows overlay, focuses Cancel
  - HideConfirmation() helper — collapses overlay, clears callback
  - ConfirmAndDeleteOlderThanAsync(int days) — counts non-pinned tabs by updated_at cutoff, shows count in overlay
  - ConfirmAndDeleteExceptPinnedAsync() — counts non-pinned tabs, shows confirmation
  - ConfirmAndDeleteAllAsync() — counts non-pinned tabs, mentions pinned count if any
  - Escape/Enter keyboard handling for confirmation overlay in Window_PreviewKeyDown
  - App.OpenWindowForOrphanAsync(string guid) stub for Plan 03 recovery panel
affects:
  - 08-03 (orphan recovery — OpenWindowForOrphanAsync stub is wired)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Callback pattern for modal confirmation — ShowConfirmation stores Action? _confirmAction, ConfirmDelete_Click invokes it
    - Keyboard gate pattern — Window_PreviewKeyDown intercepts ALL keys while overlay is visible, handles Escape/Enter, blocks others
    - Synchronous Task return from async-named methods — methods don't await but return Task.CompletedTask for interface compatibility

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs

key-decisions:
  - "ConfirmAndDelete* methods return Task.CompletedTask (not async Task) — ShowConfirmation is synchronous (shows overlay), actual deletion happens in _confirmAction callback via DeleteMultipleAsync"
  - "Enter key handling calls HideConfirmation() first then _confirmAction?.Invoke() — overlay must collapse before deletion starts to avoid visual artifact"
  - "All keyboard shortcuts blocked (not just Escape/Enter) while overlay is visible — prevents accidental Ctrl+W or other destructive shortcuts while overlay is open"
  - "App.OpenWindowForOrphanAsync added as stub (Plan 03 prereq) — linter added recovery panel code referencing this method; blocking build required Rule 3 fix"

patterns-established:
  - "Confirmation callback pattern: ShowConfirmation(title, message, Action onConfirm) — caller captures candidates at call time, not at confirm time"
  - "Modal keyboard interception: check overlay visibility at top of Window_PreviewKeyDown, handle Escape/Enter, block all others, return early"

requirements-completed: [MENU-03, MENU-04, MENU-05]

# Metrics
duration: 8min
completed: 2026-03-03
---

# Phase 8 Plan 02: Bulk Delete Confirmation Summary

**Custom modal overlay with per-action note count and keyboard navigation (Escape/Enter) wired to DeleteMultipleAsync soft-delete infrastructure**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-03T09:47:08Z
- **Completed:** 2026-03-03T09:55:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Implemented three bulk delete confirmation methods replacing stubs from Plan 01
- "Delete older than N days" counts non-pinned tabs filtered by updated_at < cutoff, shows count + day range
- "Delete all except pinned" counts non-pinned tabs, preserves pinned note
- "Delete all" counts non-pinned tabs, mentions pinned preservation if any pinned exist
- ShowConfirmation/HideConfirmation helpers manage overlay state and _confirmAction callback
- Escape key dismisses without deleting; Enter confirms; all other keys blocked during overlay
- Backdrop click cancels (ConfirmOverlayBackdrop_Click delegates to HideConfirmation)

## Task Commits

1. **Task 1: Confirmation overlay keyboard handling and helpers** - `4cb56b8` (feat)
2. **Task 2: Bulk delete confirmation implementations** - `3be4583` (feat)

**Plan metadata:** (docs commit — see state updates)

## Files Created/Modified
- `JoJot/MainWindow.xaml.cs` - ShowConfirmation/HideConfirmation helpers, keyboard gate in Window_PreviewKeyDown, three ConfirmAndDelete* implementations, OpenWindowForOrphanAsync stub wiring via recovery panel code
- `JoJot/App.xaml.cs` - Added OpenWindowForOrphanAsync public method stub (opens/focuses orphan window)

## Decisions Made
- ConfirmAndDelete* methods are synchronous Task returns (not truly async) — ShowConfirmation is synchronous, deletion happens in the stored callback when user confirms
- Enter key in overlay calls HideConfirmation() before invoking _confirmAction — overlay collapses before deletion starts, avoiding visual artifact of overlay+toast simultaneously
- All keyboard shortcuts are blocked while overlay is visible (not just Escape/Enter) — prevents Ctrl+W or other destructive shortcuts accidentally triggering during overlay interaction

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added App.OpenWindowForOrphanAsync stub**
- **Found during:** Task 2 build verification
- **Issue:** Linter had already added Plan 03 recovery panel code to MainWindow.xaml.cs; that code called `app.OpenWindowForOrphanAsync(guid)` which didn't exist in App.xaml.cs, causing CS1061 build error
- **Fix:** Added `public async Task OpenWindowForOrphanAsync(string orphanGuid)` to App.xaml.cs — checks for existing window, activates or creates new window bound to orphan GUID
- **Files modified:** JoJot/App.xaml.cs
- **Verification:** dotnet build succeeds with 0 errors, 0 warnings
- **Committed in:** 3be4583 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking build error)
**Impact on plan:** Fix was required for compilation. Method was legitimate Plan 03 infrastructure added proactively by linter.

## Issues Encountered
- Linter added full Plan 03 recovery panel implementation (ShowRecoveryPanel, HideRecoveryPanel, CreateRecoveryCard, UpdateOrphanBadge) to MainWindow.xaml.cs during Plan 02 execution — this is Plan 03 scope but required by build; accepted as pre-wiring

## Next Phase Readiness
- Plan 03 (orphan recovery) has full recovery panel code already present in MainWindow.xaml.cs — wiring of App.OpenWindowForOrphanAsync, UpdateOrphanBadge on startup, and badge initialization remain
- DatabaseService.GetOrphanedSessionInfoAsync, MigrateTabsAsync, DeleteSessionAndNotesAsync all present (from Plan 01 linter additions)
- All three bulk delete confirmation flows tested at build level; runtime testing requires running the app

---
*Phase: 08-menus-context-actions-orphaned-sessions*
*Completed: 2026-03-03*
