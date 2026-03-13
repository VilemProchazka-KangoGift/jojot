---
phase: quick-09
plan: 01
subsystem: recovery-panel
tags: [cross-window, orphan-recovery, broadcast]
dependency_graph:
  requires: []
  provides: [cross-window orphan action broadcast]
  affects: [MainWindow.Recovery.cs]
tech_stack:
  added: []
  patterns: [public notification method, GetAllWindows broadcast]
key_files:
  created: []
  modified:
    - JoJot/Views/MainWindow.Recovery.cs
decisions:
  - Added NotifyOrphansChanged() as a single public API for cross-window notification rather than making HideRecoveryPanel() public directly
  - Broadcast placed immediately after UpdateOrphanBadge() in RefreshAfterOrphanAction(), before the remaining-orphan panel refresh, so other windows close before the current window finishes its own refresh
metrics:
  duration: "~5 minutes"
  completed: "2026-03-13T08:53:00Z"
  tasks_completed: 1
  files_changed: 1
---

# Quick Task 9: Recovery Panel Broadcast Summary

**One-liner:** Cross-window orphan broadcast via NotifyOrphansChanged() so all JoJot windows update their badge and close stale recovery panels after any adopt/delete action.

## What Was Done

Added cross-window broadcast to `RefreshAfterOrphanAction()` in `MainWindow.Recovery.cs`.

Previously, when a user adopted or deleted an orphaned session on one window, only that window updated its orphan badge and potentially hid its recovery panel. Other JoJot windows (on other virtual desktops) continued showing the stale blue badge dot and any open recovery panel with outdated content.

### Changes

**`JoJot/Views/MainWindow.Recovery.cs`** — commit `d389c0e`

1. Added `public void NotifyOrphansChanged()` — single public API for cross-window notification. Calls `UpdateOrphanBadge()` + `HideRecoveryPanel()`. `HideRecoveryPanel()` already has a `if (!_recoveryPanelOpen) return;` guard so calling it unconditionally is safe.

2. Modified `RefreshAfterOrphanAction()` to broadcast immediately after the existing `UpdateOrphanBadge()` call:
   ```csharp
   if (Application.Current is App app)
   {
       foreach (var window in app.GetAllWindows())
       {
           if (window == this) continue;
           window.NotifyOrphansChanged();
       }
   }
   ```
   - Skips `this` — the current window manages its own state inline
   - `VirtualDesktopService.OrphanedSessionGuids` has already been updated by `RemoveOrphanGuid()` before `RefreshAfterOrphanAction()` is called, so `UpdateOrphanBadge()` on other windows reads the correct (post-action) state

## Verification

- Build: `dotnet build JoJot/JoJot.slnx` — succeeded, 0 warnings, 0 errors
- Tests: `dotnet test JoJot.Tests/JoJot.Tests.csproj` — 1071 passed, 0 failed
- Note: WPF UI thread requirements prevent unit testing the broadcast directly; manual testing required to verify cross-window panel behavior

## Deviations from Plan

None — plan executed exactly as written (NotifyOrphansChanged approach, broadcast placement, test skip rationale all matched plan guidance).

## Self-Check: PASSED

- File modified: `JoJot/Views/MainWindow.Recovery.cs` — exists and contains NotifyOrphansChanged() + broadcast
- Commit `d389c0e` exists in git log
- Build passes, all tests pass
