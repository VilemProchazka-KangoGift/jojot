# JoJot — Keyboard Shortcuts

---

## Global (works from any app)

| Shortcut | Action |
|---|---|
| Win+Shift+N | Focus JoJot window for current desktop; minimise if already focused |

Registered via `RegisterHotKey` Win32 API. Configurable in Preferences.

---

## Tab management

| Shortcut | Action |
|---|---|
| Ctrl+T | New tab |
| Ctrl+W | Delete active tab — deletion toast shown, no confirmation dialog |
| Ctrl+K | Clone active tab to new tab |
| Ctrl+P | Pin / unpin active tab |
| Ctrl+Tab | Next tab |
| Ctrl+Shift+Tab | Previous tab |
| F2 | Rename active tab (inline — same as double-click on label) |

---

## Editor

| Shortcut | Action |
|---|---|
| Ctrl+Z | Undo |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+C | Copy selection; copies entire note if nothing selected |
| Ctrl+V | Paste at cursor |
| Ctrl+X | Cut selection |
| Ctrl+A | Select all |
| Ctrl+S | Save active tab as TXT (opens OS save dialog) |
| Ctrl+F | Focus tab search box |

---

## Font size

| Shortcut | Action |
|---|---|
| Ctrl+= | Increase editor font size by 1 pt |
| Ctrl+- | Decrease editor font size by 1 pt |
| Ctrl+0 | Reset to default (13 pt) |
| Ctrl+Scroll Up | Increase font size by 1 pt |
| Ctrl+Scroll Down | Decrease font size by 1 pt |

**Ctrl+Scroll scope:** Only affects font size when cursor is over the editor area. Scrolling over the tab list with Ctrl held still scrolls the list normally.

---

## Delete behaviour

There are **no confirmation dialogs** for tab deletion. All triggers delete immediately and show the deletion toast (see `03-layout-and-ui.md §deletion-toast`).

| Trigger | Confirmation? | Toast? |
|---|---|---|
| Ctrl+W | No | Yes |
| Toolbar 🗑 button | No | Yes |
| Tab hover delete icon | No | Yes |
| Middle-click on tab | No | Yes |
| Context menu → Delete | No | Yes |
| Menu → Delete all / Delete all below | No (menu confirm only) | Yes — `"N notes deleted"` |

---

## Resolved decision

**Ctrl+Q rejected for delete-tab** — too close to quit-app muscle memory. `Ctrl+W` retained as the single delete shortcut.
