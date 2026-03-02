# Roadmap: JoJot

## Overview

JoJot is built in 9 phases that flow bottom-up from infrastructure to integration. The data layer, process lifecycle, and startup sequence come first because every other component depends on them. Virtual desktop integration is the product's core differentiator and highest technical risk, so it arrives second and is fully isolated before anything builds on top of it. Window management, tabs, and the editing engine follow in dependency order. Theming, menus, and context actions form the visual and command surfaces. File I/O, hotkeys, and preferences complete the power-user layer. Window drag between desktops — the most complex virtual desktop interaction — closes out the roadmap once all foundations are proven stable.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Foundation** - SQLite data model, single-instance process, named pipe IPC, and startup sequence skeleton
- [ ] **Phase 2: Virtual Desktop Integration** - COM interop isolation, desktop GUID detection, three-tier session matching, live title updates
- [ ] **Phase 3: Window & Session Management** - One window per desktop lifecycle, taskbar click handling, geometry persistence, window close behavior
- [ ] **Phase 4: Tab Management** - Tab panel, labels, reorder, rename, search, pin, clone, new/delete with focus rules
- [ ] **Phase 5: Deletion & Toast** - Tab deletion triggers, deletion toast with 4-second undo, bulk delete support
- [ ] **Phase 6: Editor & Undo** - Plain-text editor, autosave with debounce, custom two-tier undo/redo stack, save as TXT, copy behavior
- [ ] **Phase 7: Theming & Toolbar** - Light/Dark/System themes via ResourceDictionary, all 10 color tokens, toolbar with all actions
- [ ] **Phase 8: Menus, Context Actions & Orphaned Sessions** - Window menu, tab context menu, bulk delete operations, orphaned session recovery panel
- [ ] **Phase 9: File Drop, Preferences, Hotkeys & Keyboard** - File drop with content inspection, preferences dialog, global hotkey, all keyboard shortcuts
- [ ] **Phase 10: Window Drag & Crash Recovery** - Inter-desktop drag detection, lock overlay, reparent/merge/cancel flow, pending_moves crash recovery

## Phase Details

### Phase 1: Foundation
**Goal**: The app can launch as a single instance, enforce the named mutex, accept IPC connections, read/write SQLite with WAL mode, and execute the startup sequence — all before any UI is presented.
**Depends on**: Nothing (first phase)
**Requirements**: DATA-01, DATA-02, DATA-03, DATA-04, DATA-05, DATA-06, DATA-07, PROC-01, PROC-02, PROC-03, PROC-04, PROC-05, PROC-06, STRT-01, STRT-02, STRT-03, STRT-04
**Success Criteria** (what must be TRUE):
  1. Launching JoJot twice: the second instance sends an IPC message and exits cleanly; the first instance receives it
  2. The SQLite database is created at AppData\Local\JoJot\jojot.db with WAL mode on first launch; all four tables exist and are queryable
  3. Schema creation happens synchronously on first launch; background migrations run after the window is shown and never block startup
  4. The published ReadyToRun binary launches and is interactive; startup time is measured and logged as the baseline
  5. Killing the process and restarting leaves the database intact with no corruption
**Plans**: 3 plans

Plans:
- [x] 01-01-PLAN.md — Data layer: SQLite with WAL mode, schema for all four tables, integrity check, corruption recovery, logging service, IPC message types
- [x] 01-02-PLAN.md — Process lifecycle: named mutex single-instance guard, named pipe IPC server/client, P/Invoke window activation, window close behavior
- [x] 01-03-PLAN.md — Startup wiring: full startup sequence orchestration in App.xaml.cs, welcome tab, background migrations, ReadyToRun config, end-to-end verification

### Phase 2: Virtual Desktop Integration
**Goal**: The app can detect the current virtual desktop via COM, maintain a stable desktop identity across reboots via three-tier session matching, update window titles live, and fall back gracefully when the COM API is unavailable.
**Depends on**: Phase 1
**Requirements**: VDSK-01, VDSK-02, VDSK-03, VDSK-04, VDSK-05, VDSK-06, VDSK-07, VDSK-08, VDSK-09
**Success Criteria** (what must be TRUE):
  1. The window title shows "JoJot — {desktop name}" and updates live when the desktop is renamed in Windows
  2. After a reboot, JoJot matches the correct session to the correct desktop via GUID, then name, then index — in that order
  3. When the virtual desktop COM API is unavailable, JoJot launches successfully in single-notepad fallback mode
  4. All COM interop code is behind the VirtualDesktopService boundary; no COM types appear in business logic
  5. The service is verified against Windows 11 23H2 and 24H2 with the correct GUID dispatch per OS build
**Plans**: 3 plans

Plans:
- [ ] 02-01-PLAN.md — COM interop foundation: GUID dispatch dictionary, COM interface definitions, VirtualDesktopService with fallback mode
- [ ] 02-02-PLAN.md — Session matching: three-tier algorithm (GUID -> name -> index), database integration, startup wiring
- [ ] 02-03-PLAN.md — Window title: live title updates via COM notifications, desktop rename subscription, title format with em-dash

### Phase 3: Window & Session Management
**Goal**: Each virtual desktop gets exactly one JoJot window that persists its geometry, responds correctly to taskbar clicks, and handles window close without terminating the background process.
**Depends on**: Phase 2
**Requirements**: TASK-01, TASK-02, TASK-03, TASK-04, TASK-05
**Success Criteria** (what must be TRUE):
  1. Left-clicking the taskbar icon focuses the existing window for the current desktop, or creates a new one if none exists
  2. Middle-clicking the taskbar icon creates a new empty tab and focuses it immediately; if no window existed, it spawns one first
  3. Closing a window saves its geometry and flushes content; the process stays alive and responds to taskbar clicks again
  4. On reopen, the window restores to the saved position and size for that desktop
**Plans**: TBD

Plans:
- [ ] 03-01: TBD
- [ ] 03-02: TBD

### Phase 4: Tab Management
**Goal**: Users can create, rename, search, reorder, pin, clone, and navigate tabs, with each tab displaying a smart label derived from its content.
**Depends on**: Phase 3
**Requirements**: TABS-01, TABS-02, TABS-03, TABS-04, TABS-05, TABS-06, TABS-07, TABS-08, TABS-09, TABS-10, TABS-11, TABS-12, TABS-13
**Success Criteria** (what must be TRUE):
  1. The tab panel shows a 180px fixed-width scrollable list; each tab displays pin icon (if pinned), label, created date, and updated time
  2. Tab labels fall back automatically: custom name, then first ~30 chars of content, then "New note" (muted/italic)
  3. Double-clicking or pressing F2 on a tab opens inline rename; Enter commits; Escape cancels; empty submission reverts to content fallback
  4. Ctrl+F opens the search box and filters tabs by label and content; Escape clears search and returns focus to the editor
  5. Pinned tabs are always sorted to the top; drag-to-reorder works within the pinned and unpinned zones independently
**Plans**: TBD

Plans:
- [ ] 04-01: TBD
- [ ] 04-02: TBD
- [ ] 04-03: TBD

### Phase 5: Deletion & Toast
**Goal**: Users can delete tabs through any of five triggers with no confirmation dialog; a 4-second undo toast with slide-up animation provides recovery; bulk deletion is supported.
**Depends on**: Phase 4
**Requirements**: TDEL-01, TDEL-02, TDEL-03, TDEL-04, TDEL-05, TDEL-06, TOST-01, TOST-02, TOST-03, TOST-04, TOST-05, TOST-06
**Success Criteria** (what must be TRUE):
  1. Pressing Ctrl+W, clicking the toolbar delete button, clicking the tab hover icon, middle-clicking a tab, or using the context menu all delete the tab immediately with no confirmation dialog
  2. A toast slides up from the bottom within 150ms and auto-dismisses after 4 seconds; clicking Undo restores the tab to its original position with its content and custom name intact
  3. A new deletion while the toast is visible replaces it; the previously pending deletion becomes permanent
  4. Bulk delete shows "N notes deleted" with a single undo for all; pinned tabs are never deleted by bulk operations
  5. After deletion, focus moves to the first tab below, then the last tab, then a new empty tab — in that order
**Plans**: TBD

Plans:
- [ ] 05-01: TBD
- [ ] 05-02: TBD

### Phase 6: Editor & Undo
**Goal**: Users can write plain text that autosaves reliably, undo/redo across tab switches using a custom two-tier stack, copy full notes silently, and export to UTF-8 TXT files.
**Depends on**: Phase 4
**Requirements**: EDIT-01, EDIT-02, EDIT-03, EDIT-04, EDIT-05, EDIT-06, EDIT-07, UNDO-01, UNDO-02, UNDO-03, UNDO-04, UNDO-05, UNDO-06, UNDO-07, UNDO-08
**Success Criteria** (what must be TRUE):
  1. Text typed in the editor is saved to SQLite within the debounce interval (default 500ms); closing the app mid-edit loses no data
  2. Switching tabs and pressing Ctrl+Z undoes the correct tab's history — not mixed across tabs; WPF native TextBox undo never fires
  3. Undo history spans up to 50 fine-grained snapshots and 20 coarse 5-minute checkpoints seamlessly via Ctrl+Z and Ctrl+Y
  4. When global undo memory exceeds 50MB, the oldest inactive tabs are collapsed first; the active tab's history is never collapsed
  5. Pressing Ctrl+C with nothing selected copies the entire note content to the clipboard silently; Ctrl+S opens an OS save dialog and saves UTF-8 with BOM
**Plans**: TBD

Plans:
- [ ] 06-01: TBD
- [ ] 06-02: TBD
- [ ] 06-03: TBD

### Phase 7: Theming & Toolbar
**Goal**: The app renders with Light, Dark, and System themes using a complete set of 10 color tokens, switches instantly without restart, and provides a fully functional toolbar above the editor.
**Depends on**: Phase 6
**Requirements**: THME-01, THME-02, THME-03, THME-04, TOOL-01, TOOL-02, TOOL-03
**Success Criteria** (what must be TRUE):
  1. Switching between Light, Dark, and System themes in Preferences applies instantly — no restart, no flash, all UI elements update
  2. Setting Windows to dark mode with JoJot on System theme causes JoJot to switch themes automatically
  3. All 10 color tokens are applied consistently across every UI element with no hardcoded colors remaining
  4. The toolbar shows Undo, Redo, Pin, Clone, Copy, Paste, Save as TXT, and Delete buttons; Delete is right-aligned in red at 70% opacity; all buttons have tooltips with shortcut info after 600ms
**Plans**: TBD

Plans:
- [ ] 07-01: TBD
- [ ] 07-02: TBD

### Phase 8: Menus, Context Actions & Orphaned Sessions
**Goal**: Users can access window-level operations via the hamburger menu, tab-level operations via right-click context menu, and recover orphaned desktop sessions from the recovery panel.
**Depends on**: Phase 7
**Requirements**: MENU-01, MENU-02, MENU-03, MENU-04, MENU-05, CTXM-01, CTXM-02, ORPH-01, ORPH-02, ORPH-03, ORPH-04
**Success Criteria** (what must be TRUE):
  1. The hamburger menu shows all items (Recover sessions, Delete older than, Delete except pinned, Delete all, Preferences, Exit); Exit flushes all windows and terminates the process
  2. Right-clicking any tab shows: Rename, Pin/Unpin, Clone, Save as TXT, Delete, Delete all below; "Delete all below" skips pinned tabs silently
  3. When orphaned sessions exist, a badge appears on the menu button; the recovery panel lists each session with name, tab count, and last updated date
  4. Adopting an orphaned session merges its tabs into the current desktop; opening it creates a new window; deleting removes it permanently
  5. Bulk delete operations (delete older than N days, delete all except pinned, delete all) require confirmation and show the deletion toast with undo
**Plans**: TBD

Plans:
- [ ] 08-01: TBD
- [ ] 08-02: TBD
- [ ] 08-03: TBD

### Phase 9: File Drop, Preferences, Hotkeys & Keyboard
**Goal**: Users can drag text files into JoJot to open them as tabs, configure all preferences live, activate JoJot from anywhere with a global hotkey, and operate the app entirely from the keyboard.
**Depends on**: Phase 8
**Requirements**: DROP-01, DROP-02, DROP-03, DROP-04, DROP-05, DROP-06, DROP-07, PREF-01, PREF-02, PREF-03, PREF-04, PREF-05, KEYS-01, KEYS-02, KEYS-03, KEYS-04
**Success Criteria** (what must be TRUE):
  1. Dragging a text file (any extension) onto the window opens it as a new tab with the filename as the label; binary files and files over 500KB show an inline error that auto-dismisses after 4 seconds
  2. Dropping multiple files simultaneously creates one tab per valid file; invalid files show individual errors without blocking valid ones; the window border highlights while dragging over it
  3. Pressing Win+Shift+N from any application focuses or creates the JoJot window for the current desktop
  4. The Preferences dialog applies all changes live (theme, font size, debounce interval, hotkey) with no restart required
  5. All documented keyboard shortcuts work: Ctrl+T, Ctrl+W, Ctrl+K, Ctrl+P, Ctrl+Tab, Ctrl+Shift+Tab, F2, Ctrl+Z, Ctrl+Y, Ctrl+S, Ctrl+F, Ctrl+=, Ctrl+-, Ctrl+0, and Ctrl+Scroll over editor
**Plans**: TBD

Plans:
- [ ] 09-01: TBD
- [ ] 09-02: TBD
- [ ] 09-03: TBD

### Phase 10: Window Drag & Crash Recovery
**Goal**: When the user drags a JoJot window to another virtual desktop, the lock overlay appears, the user resolves the conflict (reparent, merge, or cancel), and the pending_moves table ensures correct recovery if the process crashes mid-drag.
**Depends on**: Phase 2, Phase 9
**Requirements**: DRAG-01, DRAG-02, DRAG-03, DRAG-04, DRAG-05, DRAG-06, DRAG-07, DRAG-08, DRAG-09, DRAG-10
**Success Criteria** (what must be TRUE):
  1. Dragging a JoJot window to a desktop with no existing session shows a lock overlay with a Reparent button; clicking it re-scopes the window and all its notes to the new desktop
  2. Dragging to a desktop that already has a JoJot session shows a Merge button; clicking it appends the dragged window's tabs to the existing window and closes the dragged window
  3. Clicking Cancel moves the window back to the original desktop; if that fails, Cancel is replaced by Retry plus a manual instruction message
  4. A second drag while the overlay is active is silently ignored
  5. If JoJot crashes during a drag, the next startup reads pending_moves and restores the window to its origin desktop before showing it
**Plans**: TBD

Plans:
- [ ] 10-01: TBD
- [ ] 10-02: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation | 3/3 | Complete | 2026-03-02 |
| 2. Virtual Desktop Integration | 0/TBD | Not started | - |
| 3. Window & Session Management | 0/TBD | Not started | - |
| 4. Tab Management | 0/TBD | Not started | - |
| 5. Deletion & Toast | 0/TBD | Not started | - |
| 6. Editor & Undo | 0/TBD | Not started | - |
| 7. Theming & Toolbar | 0/TBD | Not started | - |
| 8. Menus, Context Actions & Orphaned Sessions | 0/TBD | Not started | - |
| 9. File Drop, Preferences, Hotkeys & Keyboard | 0/TBD | Not started | - |
| 10. Window Drag & Crash Recovery | 0/TBD | Not started | - |
