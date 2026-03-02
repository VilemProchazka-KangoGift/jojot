# JoJot — Data Model

## SQLite configuration

- Journal mode: `WAL` (Write-Ahead Logging) — prevents UI blocking during writes, allows concurrent reads.
- Synchronous level: `NORMAL` — safe for this use case; full durability is not required.
- Single connection per process, shared across all windows. All writes serialised through this connection. **Never open multiple connections.**
- DB path: `AppData\Local\JoJot\jojot.db`

---

## `notes` table

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER PK | Auto-increment |
| `desktop_guid` | TEXT | Windows virtual desktop GUID |
| `name` | TEXT | Custom name; NULL = auto-label (derived from content) |
| `content` | TEXT | Full plain text |
| `pinned` | INTEGER | 0 / 1 |
| `created_at` | DATETIME | UTC |
| `updated_at` | DATETIME | UTC, updated on every autosave flush |
| `sort_order` | INTEGER | Position within this desktop's tab list |
| `editor_scroll_offset` | INTEGER | Vertical scroll position, restored on tab focus |
| `cursor_position` | INTEGER | Caret index, restored on tab focus |

**Tab label derivation (in priority order):**
1. `name` if set (custom rename)
2. First ~30 characters of `content` (trimmed, collapsed whitespace)
3. `"New note"` — shown muted/italic, for empty tabs with no name

---

## `app_state` table

| Column | Type | Notes |
|---|---|---|
| `desktop_guid` | TEXT PK | Updated on every successful session match |
| `desktop_name` | TEXT | Last known desktop name; tier-2 match fallback |
| `desktop_index` | INTEGER | Last known Task View position; tier-3 match fallback |
| `active_tab_id` | INTEGER | Last focused tab |
| `window_x` | INTEGER | Window position and size for this desktop |
| `window_y` | INTEGER | |
| `window_w` | INTEGER | |
| `window_h` | INTEGER | |
| `scroll_offset` | INTEGER | Tab list scroll position |

---

## `pending_moves` table

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER PK | |
| `window_handle` | INTEGER | HWND of the locked window |
| `origin_desktop_guid` | TEXT | Desktop the window belongs to |
| `target_desktop_guid` | TEXT | Desktop the window was dragged to |
| `created_at` | DATETIME | UTC — used to detect stale flags on startup |

A row exists only while a drag is unresolved. On startup, any rows here indicate a previous crash mid-drag; the window is restored to `origin_desktop_guid` and the row is deleted.

---

## `preferences` table (global, not per-desktop)

| Column | Type | Notes |
|---|---|---|
| `key` | TEXT PK | |
| `value` | TEXT | |

**Stored keys:**

| Key | Values | Default |
|---|---|---|
| `theme` | `light` / `dark` / `system` | `system` |
| `font_size` | integer (points) | `13` |
| `autosave_debounce_ms` | integer (ms) | `500` |
| `global_hotkey` | string | `Win+Shift+N` |

---

## Migrations

- **Never run on the cold-start path.** The window must show before any schema work.
- **First launch** (DB does not exist): create schema synchronously before opening window — fast, one-time only.
- **Upgrade** (schema version mismatch): run in a background thread after the window is shown. App is fully usable during migration. If migration fails, log and continue — do not block or crash.
