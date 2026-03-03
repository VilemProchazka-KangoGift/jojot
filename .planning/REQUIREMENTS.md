# Requirements: JoJot

**Defined:** 2026-03-02
**Core Value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Data & Persistence

- [x] **DATA-01**: SQLite database at AppData\Local\JoJot\jojot.db with WAL mode and NORMAL synchronous
- [x] **DATA-02**: Single SQLite connection per process, all writes serialized
- [x] **DATA-03**: Notes table with id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, editor_scroll_offset, cursor_position
- [x] **DATA-04**: App_state table storing per-desktop window geometry, active tab, scroll offset, desktop name/index
- [x] **DATA-05**: Pending_moves table tracking unresolved window drags for crash recovery
- [x] **DATA-06**: Preferences table (key/value) storing theme, font_size, autosave_debounce_ms, global_hotkey
- [x] **DATA-07**: Schema created synchronously on first launch; migrations run in background thread after window shown

### Process Lifecycle & IPC

- [x] **PROC-01**: Single-instance background process via named mutex (Global\JoJot_SingleInstance)
- [x] **PROC-02**: Named pipe IPC (\\.\pipe\JoJot_IPC) for second-instance communication
- [x] **PROC-03**: Second instance resolves current desktop GUID, sends JSON action via pipe, then exits
- [x] **PROC-04**: Pipe timeout (> 500ms) or failure triggers force-kill of hung process and fresh start
- [x] **PROC-05**: Background process stays alive when all windows are closed
- [x] **PROC-06**: Exit via menu flushes all content across all windows, deletes empty tabs, terminates process

### Virtual Desktop Integration

- [x] **VDSK-01**: Detect current virtual desktop via IVirtualDesktopManager COM API
- [x] **VDSK-02**: One independent JoJot window per virtual desktop with its own tabs and state
- [x] **VDSK-03**: Three-tier session matching on startup: GUID (exact), desktop name, desktop index
- [x] **VDSK-04**: Update stored GUID to current live GUID after successful match
- [x] **VDSK-05**: Index matching only when exactly one unmatched session and one unmatched desktop at that index
- [x] **VDSK-06**: Window title shows "JoJot — {desktop name}" or "JoJot — Desktop N" or "JoJot"
- [x] **VDSK-07**: Window title updates live via IVirtualDesktopNotification when desktop is renamed
- [x] **VDSK-08**: Fallback to "default" GUID if virtual desktop API fails (single-instance notepad mode)
- [x] **VDSK-09**: Virtual desktop service abstraction layer isolating all COM interop from business logic

### Taskbar & Window Management

- [x] **TASK-01**: Left-click on taskbar icon: focus existing window or create new window for current desktop
- [x] **TASK-02**: Middle-click on taskbar icon: quick capture — new empty tab on current desktop, focused immediately
- [x] **TASK-03**: Middle-click with no existing window: spawn window, load saved tabs, but create and focus new empty tab
- [x] **TASK-04**: Restore saved window geometry (position, size) per desktop
- [x] **TASK-05**: Window close: flush content, delete empty tabs, save geometry, destroy window (process stays alive)

### Tab Management

- [x] **TABS-01**: Tab list panel, 180px fixed width, vertically scrollable
- [x] **TABS-02**: Tab label with 3-tier fallback: custom name → first ~30 chars of content → "New note" (muted/italic)
- [x] **TABS-03**: Tab entry shows pin icon (if pinned), label, created date (left), updated time (right)
- [x] **TABS-04**: Active tab highlighted with 2px left accent border
- [x] **TABS-05**: Drag-to-reorder within zones (pinned zone / unpinned zone separately)
- [x] **TABS-06**: Tab rename via double-click, F2, or context menu; inline editable field; Enter commits, Escape cancels
- [x] **TABS-07**: Empty/whitespace rename submission clears custom name, reverts to content fallback
- [x] **TABS-08**: New tab (Ctrl+T / + button): creates notes row, focuses editor immediately, label "New note"
- [x] **TABS-09**: Clone tab (Ctrl+K / toolbar / context menu): duplicate content into new tab below
- [x] **TABS-10**: Pin/unpin toggle (Ctrl+P / toolbar / context menu): pinned tabs always sorted to top
- [x] **TABS-11**: Tab search box (Ctrl+F): filters tabs by label and full content within current desktop
- [x] **TABS-12**: Search box takes all available width minus + button; Escape clears and returns focus to editor
- [x] **TABS-13**: Ctrl+Tab / Ctrl+Shift+Tab: next/previous tab navigation

### Tab Deletion

- [x] **TDEL-01**: Multiple delete triggers: Ctrl+W, toolbar button, tab hover icon, middle-click on tab, context menu
- [x] **TDEL-02**: All single-tab deletions are immediate with no confirmation dialog
- [x] **TDEL-03**: Tab hover shows delete icon (12px, upper-right, fades in 100ms) with color change on hover
- [x] **TDEL-04**: Middle-click on any tab deletes it immediately
- [x] **TDEL-05**: Post-delete focus: first tab below → last tab in list → create new empty tab
- [x] **TDEL-06**: Pinned tabs are never deleted by bulk operations (silently skipped)

### Deletion Toast

- [x] **TOST-01**: Toast appears at bottom of window on every deletion, 36px tall, full width
- [x] **TOST-02**: Slides up from bottom (translateY 100%→0, 150ms ease-out), auto-dismisses after 4 seconds
- [x] **TOST-03**: Undo button restores tab (same content, position, custom name), dismisses toast
- [x] **TOST-04**: New deletion while toast visible replaces toast; previous deletion becomes permanent
- [x] **TOST-05**: Bulk delete toast shows "N notes deleted" with single undo for all
- [x] **TOST-06**: Toast styling: tab name in quotes/italic (truncated 30 chars), undo in accent color with underline

### Editor

- [x] **EDIT-01**: Plain-text editor with monospace font (Consolas, default 13pt), word-wrap on, no horizontal scrollbar
- [x] **EDIT-02**: Autosave with configurable debounce (default 500ms) to SQLite; updated_at set on every write
- [x] **EDIT-03**: Write frequency cap: new write cannot be scheduled sooner than debounce interval after previous write completed
- [x] **EDIT-04**: On app close: flush immediately, no data loss
- [x] **EDIT-05**: On tab restore: reload content, cursor position, and scroll offset from database
- [x] **EDIT-06**: Copy behavior: selection copied normally; no selection copies entire note content silently
- [x] **EDIT-07**: Save as TXT (Ctrl+S): OS save dialog, UTF-8 with BOM, default filename from tab name or content

### Undo/Redo

- [x] **UNDO-01**: Custom per-tab in-memory UndoStack (WPF native TextBox undo disabled via IsUndoEnabled=False)
- [x] **UNDO-02**: Tier-1: up to 50 full content snapshots, pushed on every debounced autosave if content differs
- [x] **UNDO-03**: Tier-2: up to 20 coarse checkpoints, saved every 5 minutes of active editing
- [x] **UNDO-04**: Undo/redo pointer moves across both tiers seamlessly via Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z
- [x] **UNDO-05**: Global 50MB budget across all UndoStacks; collapse at 80%, target 60%
- [x] **UNDO-06**: Collapse: oldest tabs first, tier-1 into tier-2, then evict oldest tier-2; active tab never collapsed
- [x] **UNDO-07**: Tab switch saves content/cursor to in-memory model; arriving tab binds its UndoStack
- [x] **UNDO-08**: UndoStacks are in-memory only — not persisted, discarded on window close

### File Drop

- [x] **DROP-01**: Dragging a file onto JoJot window opens it as a new tab
- [x] **DROP-02**: Acceptance by content inspection (valid UTF-8/UTF-16, no null bytes/non-printable chars), not extension
- [x] **DROP-03**: Size limit 500KB checked before content inspection
- [x] **DROP-04**: Tab name set to filename including extension; content loaded; original file unmodified
- [x] **DROP-05**: Drop visual feedback: highlight border while dragging over window
- [x] **DROP-06**: Error messages (inline alert, auto-dismiss 4s): too large, binary content, read error
- [x] **DROP-07**: Multiple files dropped simultaneously: each valid file gets its own tab; errors don't block valid files

### Window Menu

- [x] **MENU-01**: Window menu (hamburger icon) with: Recover sessions, Delete all older than, Delete all except pinned, Delete all, separator, Preferences, Exit
- [x] **MENU-02**: Recover sessions opens orphaned session panel (badge on menu button when orphaned sessions exist)
- [x] **MENU-03**: "Delete all older than N days" dialog; deletes non-pinned tabs by updated_at; confirmation required
- [x] **MENU-04**: "Delete all except pinned" and "Delete all" with confirmation; pinned tabs always preserved
- [x] **MENU-05**: Bulk deletes show single toast with "N notes deleted" and one undo

### Tab Context Menu

- [x] **CTXM-01**: Right-click on tab shows: Rename, Pin/Unpin, Clone to new tab, Save as TXT, Delete, Delete all below
- [x] **CTXM-02**: "Delete all below" deletes non-pinned tabs below this one; pinned tabs silently skipped

### Orphaned Sessions

- [x] **ORPH-01**: Sessions with no desktop match become orphaned (stay in DB until user acts)
- [x] **ORPH-02**: Recovery panel lists orphaned sessions with desktop name, tab count, last updated date
- [x] **ORPH-03**: Actions per session: Adopt into current desktop (merge tabs), Open as new window, Delete
- [x] **ORPH-04**: Non-blocking badge on menu button when orphaned sessions exist (no dialog on startup)

### Window Drag (Desktop Transfer)

- [x] **DRAG-01**: Detect window drag to another desktop via IVirtualDesktopNotification::OnWindowMovedToDesktop
- [x] **DRAG-02**: Write pending_moves row immediately on detection; apply lock overlay
- [x] **DRAG-03**: Lock overlay: semi-transparent dark (rgba 0,0,0,0.65), content visible but non-interactive
- [x] **DRAG-04**: Reparent button (no existing session on target): re-scope window and all notes to new desktop
- [x] **DRAG-05**: Merge button (existing session on target): append tabs to existing window, close dragged window
- [x] **DRAG-06**: Cancel button: move window back to original desktop via MoveWindowToDesktop
- [x] **DRAG-07**: Cancel failure: replace Cancel with Retry + manual instruction message
- [x] **DRAG-08**: Second drag while overlay active is ignored
- [x] **DRAG-09**: Crash recovery: pending_moves rows on startup restore window to origin desktop
- [x] **DRAG-10**: Persistent warning badge in title bar when window GUID doesn't match current desktop GUID

### Theming

- [x] **THME-01**: Three themes: Light, Dark, System (follows Windows app mode)
- [x] **THME-02**: Instant theme switching via WPF ResourceDictionary swap
- [x] **THME-03**: System theme re-evaluates on SystemEvents.UserPreferenceChanged
- [x] **THME-04**: 10 color tokens defined for both light and dark themes (c-win-bg through c-toolbar-icon-hover)

### Preferences

- [x] **PREF-01**: Preferences dialog opened via menu; all changes apply live, no restart
- [x] **PREF-02**: Theme toggle: Light | System | Dark
- [x] **PREF-03**: Font size control: +/- buttons, 8-32pt range, 1pt step, reset link to 13pt
- [x] **PREF-04**: Autosave debounce interval: numeric input, 200-2000ms range, default 500
- [x] **PREF-05**: Global hotkey picker: key combination, default Win+Shift+N

### Keyboard & Hotkeys

- [x] **KEYS-01**: Global hotkey (Win+Shift+N default) via RegisterHotKey: focus/minimize JoJot window
- [x] **KEYS-02**: Font size: Ctrl+= increase, Ctrl+- decrease, Ctrl+0 reset to 13pt
- [x] **KEYS-03**: Ctrl+Scroll over editor area changes font size; over tab list scrolls normally
- [x] **KEYS-04**: All keyboard shortcuts per spec: Ctrl+T, Ctrl+W, Ctrl+K, Ctrl+P, Ctrl+Tab, Ctrl+Shift+Tab, F2, Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+A, Ctrl+S, Ctrl+F

### Toolbar

- [x] **TOOL-01**: Toolbar above editor: Undo, Redo | Pin, Clone | Copy, Paste | Save as TXT | spacer | Delete
- [x] **TOOL-02**: Delete button right-aligned via flex spacer; default opacity 0.7, color #e74c3c, hover opacity 1.0
- [x] **TOOL-03**: Tooltip delay 600ms; tooltips include shortcut key info

### Startup & Publishing

- [x] **STRT-01**: Startup sequence: mutex → pending_moves check → open DB → session match → load tabs → restore geometry → apply theme → focus tab → show window
- [x] **STRT-02**: PublishReadyToRun=true (not Native AOT) for fast startup; best-effort sub-200ms
- [x] **STRT-03**: Background migrations after window shown; never block cold-start path
- [x] **STRT-04**: First launch: create schema synchronously (fast, one-time), then show window

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Enhanced Search

- **SRCH-01**: Full-text search across all desktops (currently per-desktop only)

### Import/Export

- **IMEX-01**: Batch export all notes to folder of .txt files
- **IMEX-02**: Import from folder of .txt files

### Advanced Desktop Features

- **ADVD-01**: Desktop-specific preferences (font size, theme per desktop)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Rich text / markdown rendering | Plain text only by design — core positioning |
| Cloud sync | Local-first, no network dependency |
| Images or attachments | Text only |
| Encryption | Not a security tool |
| Mobile or cross-platform | Windows desktop only (WPF) |
| Native AOT (PublishAot=true) | WPF incompatible (dotnet/wpf#3811); using ReadyToRun instead |
| Cold-start < 200ms guarantee | Unrealistic for WPF (Defender scanning); warm-start best-effort |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| DATA-01 | Phase 1 | Complete |
| DATA-02 | Phase 1 | Complete |
| DATA-03 | Phase 1 | Complete |
| DATA-04 | Phase 1 | Complete |
| DATA-05 | Phase 1 | Complete |
| DATA-06 | Phase 1 | Complete |
| DATA-07 | Phase 1 | Complete |
| PROC-01 | Phase 1 | Complete |
| PROC-02 | Phase 1 | Complete |
| PROC-03 | Phase 1 | Complete |
| PROC-04 | Phase 1 | Complete |
| PROC-05 | Phase 1 | Complete |
| PROC-06 | Phase 1 | Complete |
| STRT-01 | Phase 1 | Complete |
| STRT-02 | Phase 1 | Complete |
| STRT-03 | Phase 1 | Complete |
| STRT-04 | Phase 1 | Complete |
| VDSK-01 | Phase 2 | Complete |
| VDSK-02 | Phase 2 | Complete |
| VDSK-03 | Phase 2 | Complete |
| VDSK-04 | Phase 2 | Complete |
| VDSK-05 | Phase 2 | Complete |
| VDSK-06 | Phase 2 | Complete |
| VDSK-07 | Phase 2 | Complete |
| VDSK-08 | Phase 2 | Complete |
| VDSK-09 | Phase 2 | Complete |
| TASK-01 | Phase 3 | Complete |
| TASK-02 | Phase 3 | Complete |
| TASK-03 | Phase 3 | Complete |
| TASK-04 | Phase 3 | Complete |
| TASK-05 | Phase 3 | Complete |
| TABS-01 | Phase 4 | Complete |
| TABS-02 | Phase 4 | Complete |
| TABS-03 | Phase 4 | Complete |
| TABS-04 | Phase 4 | Complete |
| TABS-05 | Phase 4 | Complete |
| TABS-06 | Phase 4 | Complete |
| TABS-07 | Phase 4 | Complete |
| TABS-08 | Phase 4 | Complete |
| TABS-09 | Phase 4 | Complete |
| TABS-10 | Phase 4 | Complete |
| TABS-11 | Phase 4 | Complete |
| TABS-12 | Phase 4 | Complete |
| TABS-13 | Phase 4 | Complete |
| TDEL-01 | Phase 5 | Complete |
| TDEL-02 | Phase 5 | Complete |
| TDEL-03 | Phase 5 | Complete |
| TDEL-04 | Phase 5 | Complete |
| TDEL-05 | Phase 5 | Complete |
| TDEL-06 | Phase 5 | Complete |
| TOST-01 | Phase 5 | Complete |
| TOST-02 | Phase 5 | Complete |
| TOST-03 | Phase 5 | Complete |
| TOST-04 | Phase 5 | Complete |
| TOST-05 | Phase 5 | Complete |
| TOST-06 | Phase 5 | Complete |
| EDIT-01 | Phase 6 | Complete |
| EDIT-02 | Phase 6 | Complete |
| EDIT-03 | Phase 8.1 | Complete |
| EDIT-04 | Phase 6 | Complete |
| EDIT-05 | Phase 6 | Complete |
| EDIT-06 | Phase 6 | Complete |
| EDIT-07 | Phase 6 | Complete |
| UNDO-01 | Phase 6 | Complete |
| UNDO-02 | Phase 6 | Complete |
| UNDO-03 | Phase 6 | Complete |
| UNDO-04 | Phase 6 | Complete |
| UNDO-05 | Phase 6 | Complete |
| UNDO-06 | Phase 6 | Complete |
| UNDO-07 | Phase 6 | Complete |
| UNDO-08 | Phase 6 | Complete |
| THME-01 | Phase 7 | Complete |
| THME-02 | Phase 7 | Complete |
| THME-03 | Phase 7 | Complete |
| THME-04 | Phase 7 | Complete |
| TOOL-01 | Phase 7 | Complete |
| TOOL-02 | Phase 7 | Complete |
| TOOL-03 | Phase 7 | Complete |
| MENU-01 | Phase 8 Plan 01 | Complete |
| MENU-02 | Phase 8 Plan 03 | Complete |
| MENU-03 | Phase 8 Plan 02 | Complete |
| MENU-04 | Phase 8 Plan 02 | Complete |
| MENU-05 | Phase 8 Plan 02 | Complete |
| CTXM-01 | Phase 8 Plan 01 | Complete |
| CTXM-02 | Phase 8 Plan 01 | Complete |
| ORPH-01 | Phase 8 Plan 03 | Complete |
| ORPH-02 | Phase 8 Plan 03 | Complete |
| ORPH-03 | Phase 8.1 | Complete |
| ORPH-04 | Phase 8 Plan 03 | Complete |
| DROP-01 | Phase 9 | Complete |
| DROP-02 | Phase 9 | Complete |
| DROP-03 | Phase 9 | Complete |
| DROP-04 | Phase 9 | Complete |
| DROP-05 | Phase 9 | Complete |
| DROP-06 | Phase 9 | Complete |
| DROP-07 | Phase 9 | Complete |
| PREF-01 | Phase 9 | Complete |
| PREF-02 | Phase 9 | Complete |
| PREF-03 | Phase 9 | Complete |
| PREF-04 | Phase 9 | Complete |
| PREF-05 | Phase 9 | Complete |
| KEYS-01 | Phase 9 | Complete |
| KEYS-02 | Phase 9 | Complete |
| KEYS-03 | Phase 9 | Complete |
| KEYS-04 | Phase 9 | Complete |
| DRAG-01 | Phase 10 | Complete |
| DRAG-02 | Phase 10 | Complete |
| DRAG-03 | Phase 10 | Complete |
| DRAG-04 | Phase 10 | Complete |
| DRAG-05 | Phase 10 | Complete |
| DRAG-06 | Phase 10 | Complete |
| DRAG-07 | Phase 10 | Complete |
| DRAG-08 | Phase 10 | Complete |
| DRAG-09 | Phase 10 | Complete |
| DRAG-10 | Phase 10 | Complete |

**Coverage:**
- v1 requirements: 120 total
- Mapped to phases: 120
- Satisfied: 120 of 120 (Phases 1-10)
- Unmapped: 0

---
*Requirements defined: 2026-03-02*
*Last updated: 2026-03-03 after Phase 10.2 gap closure — all 120 v1 requirements verified and marked complete*
