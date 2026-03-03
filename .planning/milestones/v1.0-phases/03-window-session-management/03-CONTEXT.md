# Phase 3: Window & Session Management - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Each virtual desktop gets exactly one JoJot window that persists its geometry, responds correctly to taskbar clicks, and handles window close without terminating the background process. Covers TASK-01 through TASK-05: taskbar left-click (focus/create), middle-click (quick capture), geometry persistence, and window close behavior.

Tab UI, editor, theming, and tray icon are out of scope.

</domain>

<decisions>
## Implementation Decisions

### Default window dimensions
- Compact notepad-sized window (~500x600px) for first-time launch on a desktop with no saved geometry
- Centered on the primary monitor (or current monitor if multi-monitor)
- Freely resizable with a minimum size constraint (min ~300x400 so UI never breaks)
- Each desktop stores its own geometry independently in app_state (per-desktop, not global)

### Geometry persistence fidelity
- Persist maximized/normal window state — add a `window_state` column to app_state (schema migration)
- If closed while maximized, reopen maximized; also remember pre-maximize size for un-maximize
- Save geometry on window close only (TASK-05 handler), not on every move/resize
- Multi-monitor: save absolute screen coordinates — window reopens on the same monitor if still connected
- Off-screen recovery: detect if saved position is off-screen and snap to nearest visible monitor edge; keep size intact, only adjust position

### Window lifecycle on desktop switch
- No auto-create: windows only appear when the user explicitly clicks the taskbar icon or launches JoJot
- On startup, only create a window for the current desktop — other desktop windows are created on-demand via taskbar click
- No system tray icon: when all windows are closed, the process is invisible; user re-launches .exe to trigger IPC and get a window back
- When IPC activate arrives and the current desktop has no window, restore the previous session (reload tabs from database for this desktop — user gets back exactly where they left off)

### Close-and-relaunch feel
- X button destroys the window (per TASK-05): save geometry, flush content, delete empty tabs, then fully destroy — process stays alive
- Changes the current hide behavior (Phase 1 PROC-05) to actual destroy — OnClosing no longer cancels the close
- Instant window reappearance when re-launching: since the process is already running, IPC path should be near-instant; target under 200ms to visible window
- No opening animation — window appears instantly; matches "zero friction" philosophy
- Editor auto-focused on window create: active tab's editor gets focus with cursor at saved position so user can start typing immediately

### Claude's Discretion
- Exact minimum window size values (around 300x400 but fine-tune based on layout)
- Exact default window dimensions (around 500x600 but adjust for DPI)
- Off-screen detection algorithm details
- Schema migration implementation for window_state column
- WindowActivationHelper enhancements for multi-window scenario

</decisions>

<specifics>
## Specific Ideas

- "Zero friction" is the guiding UX principle — JoJot should feel like it was always there
- Window should feel like it's part of the desktop, not a separate app you have to manage
- Re-launching JoJot should restore you to exactly where you left off on that desktop

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `VirtualDesktopService.CurrentDesktopChanged` event: already fires on desktop switch — wire to multi-window visibility
- `VirtualDesktopService.CurrentDesktopGuid`: identifies which desktop is active for window routing
- `WindowActivationHelper.ActivateWindow()`: P/Invoke-based cross-process focus (AttachThreadInput + SetForegroundWindow)
- `MainWindow.ActivateFromIpc()`: IPC-triggered activation with Dispatcher marshaling
- `MainWindow.UpdateDesktopTitle()`: already sets title per desktop with em-dash format
- `MainWindow.FlushAndClose()`: stub exists — needs full implementation for TASK-05
- `IpcService.HandleIpcCommand()`: routes ActivateCommand — needs multi-window routing
- `DatabaseService` session CRUD: CreateSession, UpdateSession, GetAllSessions already exist

### Established Patterns
- Static services (VirtualDesktopService, DatabaseService, LogService) — no DI container
- Async/await with SemaphoreSlim for DB write serialization
- COM events -> public C# events -> UI thread Dispatcher marshaling
- ShutdownMode.OnExplicitShutdown — process stays alive when windows close

### Integration Points
- `App._mainWindow` (single field) needs to become a per-desktop window registry (Dictionary<string, MainWindow>)
- `App.HandleIpcCommand()` needs to route to the correct desktop's window
- `App.OnAppStartup()` Step 9 needs to use the window registry
- `MainWindow.OnClosing()` currently hides — needs to change to destroy with geometry save
- `app_state` table: geometry columns exist but no read/write methods in DatabaseService yet
- `app_state` needs new `window_state` column for maximized/normal persistence (schema migration)

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-window-session-management*
*Context gathered: 2026-03-02*
