# JoJot

## What This Is

Lightweight Windows desktop app for plain-text notes. Each Windows virtual desktop gets its own independent JoJot window with its own tabs and saved state. One background process manages all windows, accessible from the taskbar. Built for fastest possible capture — no formatting, no cloud sync, no clutter.

## Core Value

Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.

## Requirements

### Validated

- WPF app skeleton created (App.xaml, MainWindow.xaml, .NET 10 project)

### Active

- [ ] SQLite data model with WAL mode, single connection, 4 tables (notes, app_state, pending_moves, preferences)
- [ ] Virtual desktop detection via IVirtualDesktopManager, one window per desktop
- [ ] Three-tier session matching (GUID, name, index) across reboots
- [ ] Single-instance background process with named mutex and named pipe IPC
- [ ] Taskbar click handling: left-click (focus/create window), middle-click (quick capture)
- [ ] Window title showing desktop name, live-updated
- [ ] Tab list panel (180px fixed, scrollable, drag-to-reorder within zones)
- [ ] Tab labels with 3-tier fallback (custom name, content preview, "New note")
- [ ] Tab rename (double-click, F2, context menu), inline editing
- [ ] Tab search box filtering by label and content (Ctrl+F)
- [ ] Plain-text editor (monospace, word-wrap, no formatting)
- [ ] Autosave with 500ms debounce to SQLite, write frequency cap
- [ ] Two-tier undo/redo stack (50 fine-grained + 20 coarse checkpoints, 50MB global budget, memory pressure collapse)
- [ ] Toolbar: undo, redo, pin, clone, copy, paste, save-as-TXT, delete
- [ ] Copy behaviour: selection or full note if nothing selected
- [ ] Save as TXT with OS dialog, UTF-8 BOM
- [ ] Pin/unpin tabs, pinned always sorted to top, protected from bulk delete
- [ ] Clone tab
- [ ] Tab deletion (multiple triggers), immediate with 4-second undo toast
- [ ] Deletion toast with undo, auto-dismiss, bulk support
- [ ] File drop: content-inspected acceptance, 500KB limit, multiple files, error messages
- [ ] Window menu: recover sessions, bulk delete operations, preferences, exit
- [ ] Tab context menu: rename, pin, clone, save, delete, delete all below
- [ ] Orphaned session recovery panel with adopt/open/delete actions
- [ ] Window drag detection via IVirtualDesktopNotification, lock overlay
- [ ] Drag overlay: reparent (no existing session) or merge (existing session) or cancel
- [ ] pending_moves crash recovery on startup
- [ ] Theming: light, dark, system (follows Windows), instant ResourceDictionary swap
- [ ] Theme tokens for all UI elements (10 color tokens)
- [ ] Preferences dialog: theme toggle, font size, autosave debounce, global hotkey
- [ ] Global hotkey (Win+Shift+N default) via RegisterHotKey
- [ ] Font size controls: Ctrl+=, Ctrl+-, Ctrl+0, Ctrl+Scroll
- [ ] All keyboard shortcuts (tab management, editor, font size)
- [ ] Startup sequence: < 200ms to first interactive window
- [ ] Native AOT publishing (PublishAot=true), no runtime reflection
- [ ] Background migrations after window shown, never on cold-start path
- [ ] Post-delete focus rules (next below, then last, then new empty tab)
- [ ] Window close: flush content, delete empty tabs, save geometry, keep process alive
- [ ] Exit: flush all windows, delete all empty tabs, terminate process

### Out of Scope

- Rich text / markdown rendering — plain text only by design
- Cloud sync — local-first, no network dependency
- Full-text search across all desktops — search is per-desktop only
- Images or attachments — text only
- Encryption — not a security tool
- Mobile or cross-platform — Windows desktop only
- Tab persistence of undo history — stacks are in-memory only

## Context

The project has detailed spec documents in `resources/` covering all 8 feature areas. These are the definitive spec — build exactly what's documented. The WPF skeleton exists (App.xaml, MainWindow.xaml) but no functionality is implemented yet.

Key technical considerations:
- Native AOT means no runtime reflection. Use Microsoft.Data.Sqlite with AOT-safe annotations.
- WPF + AOT matured in .NET 9/10 but XAML-heavy paths should be tested early.
- Virtual desktop API (IVirtualDesktopManager) is COM-based, undocumented, and version-sensitive.
- Single SQLite connection per process, WAL mode, all writes serialized.

Spec documents (in `resources/`):
1. `01-data-model.md` — SQLite schema, all tables
2. `02-virtual-desktops.md` — Desktop detection, session matching, IPC, drag handling
3. `03-layout-and-ui.md` — Window layout, tabs, toolbar, toast, theming
4. `04-menus.md` — Window menu, tab context menu
5. `05-editing.md` — Autosave, undo/redo, file drop
6. `06-keyboard-shortcuts.md` — Full shortcut table
7. `07-preferences.md` — Preferences dialog
8. `08-startup.md` — Startup sequence, AOT notes

## Constraints

- **Tech stack**: WPF, .NET 10, C#, Native AOT, SQLite (Microsoft.Data.Sqlite) — non-negotiable
- **Platform**: Windows 10/11 only
- **Performance**: < 200ms to first interactive window on cold start
- **Data**: Single SQLite connection per process, WAL mode, writes serialized
- **AOT**: No runtime reflection; all libraries must be AOT-compatible
- **UX**: No confirmation dialogs for single-tab deletion (toast undo only)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Native AOT over JIT | < 200ms startup target, self-contained binary | -- Pending |
| Custom undo/redo over WPF native | WPF TextBox undo clears on tab switch, can't transfer between tabs | -- Pending |
| Single process + IPC over multi-process | One SQLite connection, consistent state, lower resource use | -- Pending |
| Content inspection over extension for file drop | Accept any text file regardless of extension (.log, .yaml, etc.) | -- Pending |
| Three-tier session matching | GUIDs reassigned on reboot; name + index fallback ensures continuity | -- Pending |
| No confirmation dialogs for delete | Speed over safety; 4-second undo toast provides recovery path | -- Pending |

---
*Last updated: 2026-03-02 after initialization*
