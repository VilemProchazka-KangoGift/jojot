---
phase: 03-window-session-management
status: passed
verified: 2026-03-02
verifier: automated
---

# Phase 3: Window & Session Management — Verification

## Phase Goal
Each virtual desktop gets exactly one JoJot window that persists its geometry, responds correctly to taskbar clicks, and handles window close without terminating the background process.

## Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| TASK-01 | VERIFIED | App.xaml.cs HandleIpcCommand: ActivateCommand looks up _windows registry, focuses existing or calls CreateWindowForDesktop |
| TASK-02 | VERIFIED | App.xaml.cs HandleIpcCommand: NewTabCommand looks up window, calls RequestNewTab; --new-tab arg sends NewTabCommand from second instance |
| TASK-03 | VERIFIED | HandleIpcCommand NewTabCommand case: if no window exists, CreateWindowForDesktop is called first, then RequestNewTab on the new window |
| TASK-04 | VERIFIED | DatabaseService.GetWindowGeometryAsync/SaveWindowGeometryAsync with parameterized queries; WindowPlacementHelper.CaptureGeometry/ApplyGeometry with P/Invoke; ClampToNearestScreen for off-screen recovery |
| TASK-05 | VERIFIED | MainWindow.OnClosing: CaptureGeometry + fire-and-forget SaveWindowGeometryAsync, no e.Cancel (window destroyed); ShutdownMode.OnExplicitShutdown keeps process alive |

## Success Criteria Verification

### 1. Left-clicking the taskbar icon focuses the existing window for the current desktop, or creates a new one if none exists
**VERIFIED**: `HandleIpcCommand` resolves `VirtualDesktopService.CurrentDesktopGuid`, checks `_windows.TryGetValue`, calls `WindowActivationHelper.ActivateWindow` if found, or `CreateWindowForDesktop` if not.

### 2. Middle-clicking the taskbar icon creates a new empty tab and focuses it immediately; if no window existed, it spawns one first
**VERIFIED**: `NewTabCommand` case handles both scenarios — existing window gets `RequestNewTab()`, new window is created then `RequestNewTab()` called. Second instance `--new-tab` argument detection sends `NewTabCommand` via IPC.

### 3. Closing a window saves its geometry and flushes content; the process stays alive and responds to taskbar clicks again
**VERIFIED**: `MainWindow.OnClosing` captures geometry via `WindowPlacementHelper.CaptureGeometry`, fires-and-forgets `DatabaseService.SaveWindowGeometryAsync`, does NOT set `e.Cancel = true`. `window.Closed` event removes from `_windows` registry. `ShutdownMode.OnExplicitShutdown` keeps process alive. IPC server continues running.

### 4. On reopen, the window restores to the saved position and size for that desktop
**VERIFIED**: `CreateWindowForDesktop` calls `DatabaseService.GetWindowGeometryAsync` and `WindowPlacementHelper.ApplyGeometry` twice — once before Show (WPF properties), once after Show (SetWindowPlacement P/Invoke). `ClampToNearestScreen` handles off-screen recovery.

## Must-Have Truths Verification

| Truth | Status |
|-------|--------|
| App._windows is Dictionary<string, MainWindow> keyed by desktop GUID | VERIFIED |
| On startup, exactly one window is created for the current desktop | VERIFIED |
| IPC ActivateCommand resolves current desktop GUID, looks up registry | VERIFIED |
| IPC NewTabCommand resolves current desktop GUID, ensures window, calls RequestNewTab | VERIFIED |
| Second instance with --new-tab sends NewTabCommand | VERIFIED |
| MainWindow.OnClosing saves geometry, allows close (no e.Cancel) | VERIFIED |
| MainWindow.Closed removes from _windows registry | VERIFIED |
| Process stays alive after all windows closed (OnExplicitShutdown) | VERIFIED |
| Geometry restored via WindowPlacementHelper.ApplyGeometry on create | VERIFIED |
| MainWindow has MinWidth=320 and MinHeight=420 in XAML | VERIFIED |
| MainWindow constructor accepts desktopGuid parameter | VERIFIED |
| Desktop rename events update correct window title | VERIFIED |
| Desktop switch events logged but do NOT auto-create | VERIFIED |
| CreateWindowForDesktop sets title before showing | VERIFIED |
| Welcome tab mentions 'keeps JoJot running' | VERIFIED |

## Build Verification

```
dotnet build JoJot/JoJot.slnx
Build succeeded. 0 Warning(s). 0 Error(s).
```

## Artifacts Verification

| File | Exists | Role |
|------|--------|------|
| JoJot/Models/WindowGeometry.cs | YES | Immutable geometry record |
| JoJot/Services/WindowPlacementHelper.cs | YES | P/Invoke capture/apply/clamp |
| JoJot/Services/DatabaseService.cs | YES | Geometry CRUD + migration |
| JoJot/JoJot.csproj | YES | UseWindowsForms=true |
| JoJot/App.xaml.cs | YES | Window registry + factory + IPC routing |
| JoJot/MainWindow.xaml | YES | Min/default dimensions |
| JoJot/MainWindow.xaml.cs | YES | desktopGuid lifecycle |
| JoJot/Services/StartupService.cs | YES | Updated welcome message |

## Score

**5/5 must-haves verified**

## Result

**PASSED** — All success criteria met. All 5 TASK requirements implemented and verified against codebase. Build succeeds with zero errors.
