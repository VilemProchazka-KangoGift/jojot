---
phase: quick-08
plan: 01
subsystem: App lifecycle
tags: [shutdown, session-ending, WPF, graceful-exit]
dependency_graph:
  requires: []
  provides: [graceful-OS-shutdown]
  affects: [App.xaml.cs]
tech_stack:
  added: []
  patterns: [SessionEnding event handler, synchronous flush before shutdown]
key_files:
  created: []
  modified:
    - JoJot/App.xaml.cs
decisions:
  - Use Shutdown() not Environment.Exit(0) so OnExit cleans up IPC, hotkeys, VirtualDesktop, DB, and mutex
  - Snapshot _windows.Values to a list before iterating because FlushAndClose triggers Closed which mutates the dictionary
  - Do not set e.Cancel=true -- allow Windows to proceed with the session end
metrics:
  duration: "3 minutes"
  completed: "2026-03-13"
  tasks_completed: 1
  files_modified: 1
---

# Quick Task 8: Handle OS Shutdown -- Graceful SessionEnding Summary

**One-liner:** WPF SessionEnding handler that flushes all open windows and calls Shutdown() so JoJot never blocks OS shutdown or user logoff.

## What Was Done

Added `OnSessionEnding` handler in `App.xaml.cs` subscribed during `OnAppStartup` (immediately after `ShutdownMode = ShutdownMode.OnExplicitShutdown`). The handler:

1. Logs the session-ending reason via structured Serilog call
2. Snapshots `_windows.Values` to a local list (necessary because `FlushAndClose` triggers `window.Closed` which removes the entry from `_windows`)
3. Calls `FlushAndClose()` on each window -- stops autosave, flushes editor content to DB, commits pending deletions, saves geometry
4. Calls `Shutdown()` to trigger `OnExit` which cleans up IPC, hotkeys, VirtualDesktop, ThemeService, database, and the single-instance mutex

## Decisions Made

- `Shutdown()` preferred over `Environment.Exit(0)` here because OS session-ending warrants the cleanest shutdown path through `OnExit`. The existing `ExitApplication` uses `Environment.Exit(0)` (user-initiated action, acceptable) but for OS-initiated shutdown `OnExit` cleanup is important.
- Dictionary snapshot required: `FlushAndClose` calls `window.Close()` which fires `window.Closed`, which invokes the lambda registered in `CreateWindowForDesktop` that calls `_windows.Remove(desktopGuid)`. Iterating `_windows.Values` directly while it mutates would throw `InvalidOperationException`.
- `e.Cancel` is NOT set -- Windows must proceed with the shutdown. Setting it would cause JoJot to block the session end, which is the exact problem being fixed.

## Deviations from Plan

None -- plan executed exactly as written.

## Self-Check

- [x] `JoJot/App.xaml.cs` modified with `SessionEnding += OnSessionEnding` subscription
- [x] `OnSessionEnding` method added with correct logging, flush loop, and `Shutdown()` call
- [x] Build: 0 errors, 0 warnings
- [x] Tests: 1071 passed, 0 failed

## Self-Check: PASSED
