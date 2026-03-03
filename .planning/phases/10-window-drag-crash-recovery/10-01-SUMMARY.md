---
phase: 10-window-drag-crash-recovery
plan: 01
subsystem: virtual-desktop-interop
tags: [drag-detection, crash-recovery, com-interop]
requires: [VirtualDesktopService, VirtualDesktopInterop, DatabaseService]
provides: [WindowMovedToDesktop event, MoveWindowToDesktop API, pending_moves CRUD, PendingMove model]
affects: [VirtualDesktopNotificationListener, App.xaml.cs]
tech-stack:
  added: []
  patterns: [COM-callback-event-chain, fire-and-forget-dispatch]
key-files:
  created:
    - JoJot/Models/PendingMove.cs
  modified:
    - JoJot/Services/DatabaseService.cs
    - JoJot/Interop/VirtualDesktopInterop.cs
    - JoJot/Interop/VirtualDesktopNotificationListener.cs
    - JoJot/Services/VirtualDesktopService.cs
    - JoJot/MainWindow.xaml.cs
key-decisions:
  - "[10-01]: _desktopGuid changed from readonly to mutable — needed by Plan 02 reparent flow which reassigns window to new desktop"
  - "[10-01]: DetectMovedWindow iterates all windows checking GetWindowDesktopId against expected GUID — more reliable than trying to map the IntPtr view parameter from ViewVirtualDesktopChanged"
  - "[10-01]: Dispatcher.BeginInvoke at Normal priority for detection — lets COM state settle before querying GetWindowDesktopId"
requirements-completed: [DRAG-01, DRAG-06, DRAG-09]
duration: ~3 min
completed: 2026-03-03
---

# Phase 10 Plan 01: Infrastructure — Drag Detection, Pending Moves, COM Wrapper

PendingMove model, DatabaseService CRUD for crash recovery, MoveWindowToDesktop COM wrapper, and the complete drag detection event chain from ViewVirtualDesktopChanged COM callback through VirtualDesktopService.WindowMovedToDesktop public event.

**Duration:** ~3 min | **Tasks:** 2/2 | **Files:** 6

## What Was Built

1. **PendingMove model** (`JoJot/Models/PendingMove.cs`): Record with Id, WindowId, FromDesktop, ToDesktop, DetectedAt — maps to existing pending_moves table schema.

2. **DatabaseService CRUD** for pending_moves: InsertPendingMoveAsync (writes on drag detect), DeletePendingMoveAsync (clears after resolution), GetPendingMovesAsync (reads for crash recovery), DeleteAllPendingMovesAsync (clears after recovery).

3. **MoveWindowToDesktop COM wrapper** in VirtualDesktopInterop: Wraps IVirtualDesktopManager.MoveWindowToDesktop for the cancel/go-back flow.

4. **Drag detection event chain**: ViewVirtualDesktopChanged callback -> WindowViewChanged event -> VirtualDesktopService.OnWindowViewChanged -> DetectMovedWindow (iterates _windows, checks GUID mismatch) -> WindowMovedToDesktop public event with (hwnd, fromGuid, toGuid, toName).

5. **TryMoveWindowToDesktop** on VirtualDesktopService: Safe wrapper returning bool success/failure for cancel flow.

## Deviations from Plan

- DesktopGuid property already existed at MainWindow line 146 — did not need to add it (plan assumed it was missing)
- _desktopGuid field was `readonly` — changed to mutable for Plan 02 reparent flow

## Commits

- Task 1: `96163ab` — PendingMove model + DatabaseService CRUD
- Task 2: `376d452` — MoveWindowToDesktop wrapper + detection event chain

## Issues Encountered

None

## Next

Ready for Plan 02: Lock overlay UI, resolution flows, crash recovery wiring.
