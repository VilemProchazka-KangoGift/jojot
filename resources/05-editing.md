# JoJot — Editing, Autosave, Undo/Redo & File Drop

---

## Editor

- Monospace font (Consolas, default 13 pt, configurable via Preferences or `Ctrl+=` / `Ctrl+-`)
- Plain text only — no bold, italic, hyperlinks, markdown rendering
- Word-wrap always on; horizontal scrollbar hidden
- No status bar

---

## Autosave

- No save button. Debounced write to SQLite: **500 ms after last keystroke**.
- `updated_at` updated on every write.
- On app close: flush immediately, no data loss.
- On tab restore: reload last known content, cursor position, and scroll offset.

**Write frequency cap:** A write resets the debounce timer, but a new write cannot be scheduled sooner than 500 ms after the previous write completed. This caps writes during sustained typing.

---

## Undo / Redo

JoJot does **not** use WPF's native `TextBox` undo. The native stack is cleared on every tab switch and cannot be transferred between tabs.

Instead, each tab owns a **per-tab in-memory `UndoStack`** object, independent of the editor control.

### Two-tier stack model

**Tier 1 — Fine-grained recent snapshots**
- Up to **50 full content snapshots**
- One pushed on every debounced autosave (500 ms), if content differs from the top
- Used during normal `Ctrl+Z` / `Ctrl+Y` operation

**Tier 2 — Coarse checkpoints**
- Up to **20 checkpoints**
- One saved every **5 minutes of active editing** (5 minutes with at least one keystroke)
- Represent longer-interval recovery — "where was this note an hour ago"
- Indistinguishable from tier-1 steps during use

The undo/redo pointer moves across both tiers seamlessly.

---

### Memory pressure and collapse

**Global budget:** 50 MB across all `UndoStack`s across all windows.

**Trigger:** When total exceeds 80% of budget (40 MB), collapse runs.

**Collapse process:**
1. Sort open tabs by last-accessed time, oldest first.
2. Collapse oldest tabs' tier-1 fine-grained stack into their nearest tier-2 checkpoint.
3. Continue until memory drops below 60% of budget (30 MB).
4. If still above 60% after all tier-1 stacks collapsed, begin evicting oldest tier-2 checkpoints from least-recently-used tabs (25% at a time).
5. The **currently active tab is never collapsed or evicted** while focused.

Collapse is silent — no UI notification.

---

### Tab switching

- **On leaving a tab:** save content and cursor position to tab's in-memory model; note timestamp as `last_accessed`.
- **On arriving at a tab:** load content and cursor position into editor; bind editor to that tab's `UndoStack`; update `last_accessed` to now.
- Undo/redo toolbar buttons and `Ctrl+Z` / `Ctrl+Y` operate on whichever `UndoStack` is currently bound.

### Stack lifetime

- `UndoStack` objects live in memory for the window's lifetime only.
- Closing a window discards all stacks for that desktop — **no DB persistence**.
- Closing and reopening starts with empty stacks on all tabs.

---

## New tab behaviour

1. Immediately creates a new `notes` row with empty content and `created_at = now`.
2. Focuses the editor so the user can start typing instantly.
3. Tab label: `"New note"` until the user types (auto-label updates in real-time as content is entered).

---

## File drop

Dragging a file onto any part of the JoJot window opens it as a new tab.

### Accepted files

Acceptance is determined by **content inspection, not file extension**:
1. Read the first 8 KB of the file.
2. Check for valid UTF-8 or UTF-16 byte sequence with no null bytes or non-printable control characters (except tab, newline, carriage return).
3. If passes: accept regardless of extension (`.txt`, `.md`, `.log`, `.yaml`, `.csv`, `.json`, etc.)
4. If fails: reject with error message.

**Size limit:** 500 KB. Checked before content inspection.

### Drop behaviour (valid file)

1. Read full file content as UTF-8 (or UTF-16 if BOM detected).
2. Create new tab with file content as note body.
3. Set tab name to **filename including extension** (e.g. `notes.txt`).
4. New tab created and focused immediately.
5. Note saved via normal autosave path (500 ms debounce starts immediately).
6. Original file on disk is not modified.

**Drop visual feedback:**
- While dragging over the window: subtle highlight border around window edge.
- If file is already known invalid (e.g. `.exe`): no highlight, cursor shows no-drop indicator.

### Error messages

Shown as a small inline alert near the top of the editor, auto-dismissing after 4 seconds.

| Reason | Message |
|---|---|
| File over 500 KB | `File too large — JoJot accepts files up to 500 KB` |
| Binary content | `Not a text file — JoJot only accepts plain text files` |
| File read error | `Could not read file — check permissions and try again` |

### Multiple files dropped simultaneously

Each valid file opens as its own tab in drop order. Each invalid file shows its own error. Errors do not block valid files.
