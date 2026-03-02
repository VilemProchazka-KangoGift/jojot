# JoJot — Documentation Overview

**Version:** 1.0  
**Stack:** WPF · .NET 10 · Native AOT · SQLite  
**Platform:** Windows 10/11

## What is JoJot?

Lightweight Windows desktop app for plain-text notes. One window per virtual desktop, always accessible from the taskbar. Fastest possible capture — no formatting, no cloud sync.

---

## Document Index

Load only the file(s) relevant to the feature you're working on.

| File | Covers | Load when working on… |
|---|---|---|
| `01-data-model.md` | SQLite schema, all tables and columns | DB access, migrations, persistence |
| `02-virtual-desktops.md` | Desktop detection, session matching, window dragging, IPC | Anything touching virtual desktops or process lifecycle |
| `03-layout-and-ui.md` | Window layout, tab list, toolbar, deletion toast, theming | UI components, XAML structure |
| `04-menus.md` | Window menu (≡), tab context menu, all menu items | Menu behaviour |
| `05-editing.md` | Autosave, undo/redo stack, copy/paste, file drop, new tab | Editor behaviour, save logic |
| `06-keyboard-shortcuts.md` | Full shortcut table, font size controls | Keyboard handling |
| `07-preferences.md` | Preferences dialog, theme, font size, hotkey, debounce | Settings UI |
| `08-startup.md` | Startup sequence, process lifecycle | App bootstrap, first-run |

---

## Key constraints (read before implementing anything)

- **Native AOT** — no runtime reflection. Use `Microsoft.Data.Sqlite` with AOT-safe annotations. Test XAML-heavy paths early.
- **One SQLite connection** per process, shared across all windows. All writes serialised. Never open multiple connections.
- **Startup target: < 200 ms** to first interactive window. No migrations on the cold-start path.
- **No confirmation dialogs for delete** — all deletions are immediate, with a 4-second undo toast at the bottom of the window.
- **Undo/redo is per-tab, in-memory only** — not persisted across sessions. WPF's native TextBox undo is not used.
- **Pinned tabs are never deleted by bulk operations** — bulk delete always skips them silently.

---

## Non-Goals (out of scope)

- Rich text / markdown rendering
- Cloud sync
- Full-text search across all desktops
- Images or attachments
- Encryption
- Mobile or cross-platform
