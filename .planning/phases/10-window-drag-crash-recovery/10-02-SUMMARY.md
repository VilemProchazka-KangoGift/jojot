---
phase: 10-window-drag-crash-recovery
plan: 02
subsystem: window-drag-ui
tags: [lock-overlay, resolution-flows, crash-recovery, misplaced-detection]
requires: [Plan 01 infrastructure, VirtualDesktopService.WindowMovedToDesktop, PendingMove CRUD]
provides: [DragOverlay UI, reparent flow, merge flow, cancel flow, crash recovery, misplaced badge]
affects: [MainWindow.xaml, MainWindow.xaml.cs, App.xaml.cs, DatabaseService.cs]
tech-stack:
  added: []
  patterns: [fade-animation-overlay, event-driven-resolution, startup-crash-recovery]
key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs
    - JoJot/Services/DatabaseService.cs
key-decisions:
  - "[10-02]: MigrateTabsPreservePinsAsync preserves pin state (unlike MigrateTabsAsync which unpins) — merge flow keeps pinned tabs pinned per CONTEXT.md"
  - "[10-02]: MigrateNotesDesktopGuidAsync is a simple desktop_guid UPDATE — no sort_order or pin changes needed for reparent"
  - "[10-02]: Crash recovery migrates tabs back to origin desktop using MigrateTabsPreservePinsAsync, then shows toast"
  - "[10-02]: DragOverlay at Panel.ZIndex=200 — above FileDropOverlay(100) and ConfirmationOverlay"
  - "[10-02]: Keyboard shortcuts blocked while _isDragOverlayActive — follows ConfirmationOverlay pattern"
requirements-completed: [DRAG-02, DRAG-03, DRAG-04, DRAG-05, DRAG-06, DRAG-07, DRAG-08, DRAG-09, DRAG-10]
duration: ~5 min
completed: 2026-03-03
---

# Phase 10 Plan 02: Lock Overlay UI, Resolution Flows, Crash Recovery

Lock overlay XAML, three resolution flows (reparent/merge/cancel), cancel-failure escalation with retry, crash recovery on startup, misplaced-window detection with auto-show, and all supporting DatabaseService methods.

**Duration:** ~5 min | **Tasks:** 2/2 | **Files:** 4

## What Was Built

1. **DragOverlay XAML** (`JoJot/MainWindow.xaml`): Semi-transparent dark overlay (#A6000000, ~0.65 alpha) with centered card containing title, message, and 3 action buttons (Keep here, Merge notes, Go back). Cancel failure text hidden by default. Panel.ZIndex="200".

2. **Drag resolution handlers** (`JoJot/MainWindow.xaml.cs`):
   - `OnWindowMovedToDesktop`: Filters by HWND match, triggers `ShowDragOverlayAsync`
   - `ShowDragOverlayAsync`: DRAG-08 guard, writes pending_move, configures buttons (2 or 3 based on target occupancy), 150ms fade-in
   - `DragKeepHere_Click`: DRAG-04 reparent — migrates notes, updates _desktopGuid, reparents window registry, updates session, deletes pending_move
   - `DragMerge_Click`: DRAG-05 merge — migrates tabs preserving pins, reloads target window tabs, shows merge toast, closes source window
   - `DragCancel_Click`: DRAG-06/07 cancel — COM MoveWindowToDesktop with verification, DRAG-07 failure escalation (Retry + instruction text)
   - `HideDragOverlayAsync`: 150ms fade-out, resets all drag state
   - `OnWindowActivated_CheckMisplaced`: DRAG-10 — checks GUID mismatch on focus, adds "(misplaced)" badge, auto-shows overlay

3. **App helper methods** (`JoJot/App.xaml.cs`):
   - `HasWindowForDesktop`: Checks window registry for merge flow
   - `ReparentWindow`: Updates registry key from old to new GUID
   - `ReloadWindowTabs`: Triggers tab reload on target window after merge
   - `ShowMergeToast`: Displays merge completion toast on target window
   - `ResolvePendingMovesAsync`: DRAG-09 crash recovery — reads pending_moves, migrates tabs back to origin, shows recovery toast

4. **DatabaseService methods** (`JoJot/Services/DatabaseService.cs`):
   - `MigrateNotesDesktopGuidAsync`: Simple desktop_guid UPDATE preserving sort_order and pin state (reparent)
   - `MigrateTabsPreservePinsAsync`: Like MigrateTabsAsync but preserves pin state (merge)
   - `UpdateSessionDesktopGuidAsync`: Updates app_state session row desktop_guid (reparent)

## Deviations from Plan

- Combined Task 1 and Task 2 into a single commit since they are tightly coupled and both needed for compilation
- Did not add the optional App-level WindowMovedToDesktop logging subscription (plan marked it as optional; MainWindow handles detection directly)

## Commits

- `6e39b77` — Lock overlay UI, resolution flows, crash recovery, DatabaseService methods

## Issues Encountered

- Build failed initially because DatabaseService was missing 3 methods (MigrateNotesDesktopGuidAsync, MigrateTabsPreservePinsAsync, UpdateSessionDesktopGuidAsync) — added them to resolve
- Context window reset during execution required resuming from summary state

## Next

Phase 10 execution complete. All DRAG requirements (DRAG-01 through DRAG-10) implemented across Plans 01 and 02.
