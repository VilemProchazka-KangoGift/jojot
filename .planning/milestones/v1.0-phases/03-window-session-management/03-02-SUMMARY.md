---
phase: 03-window-session-management
plan: 02
subsystem: window-management
tags: [wpf, ipc, window-lifecycle, geometry, virtual-desktop]

requires:
  - phase: 03-window-session-management
    provides: "WindowGeometry model, DatabaseService CRUD, WindowPlacementHelper P/Invoke"
  - phase: 02-virtual-desktop-integration
    provides: "VirtualDesktopService for desktop GUID resolution and events"
provides:
  - "Per-desktop window registry (Dictionary<string, MainWindow>)"
  - "Window factory with geometry restore, title setting, and Closed cleanup"
  - "IPC routing: ActivateCommand and NewTabCommand to correct desktop window"
  - "Second instance --new-tab argument detection"
  - "Destroy-on-close with geometry save (replaces hide-on-close)"
  - "Desktop rename events update correct window title"
affects: [04, 05, 06, 08, 10]

tech-stack:
  added: []
  patterns: [per-desktop window registry, window factory, IPC routing by desktop GUID]

key-files:
  created: []
  modified:
    - JoJot/App.xaml.cs
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/Services/StartupService.cs

key-decisions:
  - "Windows destroyed on close (not hidden) — WPF windows cannot be reopened after Close(); fresh instances created via IPC"
  - "HandleIpcCommand is async void — acceptable because it's an event handler called by IPC on the Dispatcher"
  - "Desktop switch events logged but do NOT auto-create windows (user decision)"
  - "Welcome note uses live VirtualDesktopService.CurrentDesktopGuid instead of hardcoded 'default'"

patterns-established:
  - "Per-desktop window registry: Dictionary<string, MainWindow> keyed by GUID"
  - "Window factory pattern: CreateWindowForDesktop handles geometry, title, registry, show"
  - "IPC routing: resolve desktop GUID at handle time (live state), not at send time"

requirements-completed: [TASK-01, TASK-02, TASK-03, TASK-04, TASK-05]

duration: 3min
completed: 2026-03-02
---

# Phase 3 Plan 02: Window Lifecycle Summary

**Per-desktop window registry with create-on-demand factory, destroy-on-close with geometry save, IPC routing by desktop GUID, and --new-tab argument support**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-02T21:25:31Z
- **Completed:** 2026-03-02T21:28:23Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments
- Per-desktop window registry replaces single _mainWindow field
- Window factory creates MainWindow with geometry restore, title, and Closed event cleanup
- IPC ActivateCommand/NewTabCommand route to correct desktop's window or create one
- Second instance --new-tab arg sends NewTabCommand instead of ActivateCommand
- OnClosing saves geometry and allows destroy (no e.Cancel = true)
- Welcome message updated for destroy-on-close behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: MainWindow lifecycle rewrite** - `a579536` (feat)
2. **Task 2: App.xaml.cs window registry and IPC routing** - `55c01d7` (feat)
3. **Task 3: Welcome message and final build** - `a6ca888` (feat)

## Files Created/Modified
- `JoJot/MainWindow.xaml` - MinWidth=320, MinHeight=420, default 500x600
- `JoJot/MainWindow.xaml.cs` - desktopGuid constructor, OnClosing geometry save, RequestNewTab stub
- `JoJot/App.xaml.cs` - _windows registry, CreateWindowForDesktop factory, HandleIpcCommand routing
- `JoJot/Services/StartupService.cs` - Updated welcome message and desktop GUID

## Decisions Made
- [03-02]: Windows destroyed on close (not hidden) — WPF windows cannot be reopened after Close(); fresh instances via IPC
- [03-02]: HandleIpcCommand is async void — acceptable as IPC event handler on Dispatcher
- [03-02]: Desktop switch events logged but no auto-create (user decision honored)
- [03-02]: Welcome note uses VirtualDesktopService.CurrentDesktopGuid instead of hardcoded 'default'

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 3 complete — per-desktop window lifecycle fully operational
- Ready for Phase 4: Tab Management (tabs will live inside per-desktop windows)
- RequestNewTab stub ready for Phase 4 implementation

## Self-Check: PASSED

---
*Phase: 03-window-session-management*
*Completed: 2026-03-02*
