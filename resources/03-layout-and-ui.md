# JoJot — Layout & UI

---

## Window layout

```
┌──────────────────────────────────────────────────────┐
│  [≡ Menu]                        [JoJot — Work]      │  ← title bar
├──────────────────┬───────────────────────────────────┤
│ [🔍 Search…] [＋] │  [⟲][⟳]  |  [📌][⎘]  |  [📋][📄]  |  [💾]   →→  [🗑]  │
│──────────────────│───────────────────────────────────┤
│  Tab             │                                   │
│  List            │   plain text editor area          │
│  180px           │   monospace font, word-wrap on    │
│  (scrollable)    │                                   │
├──────────────────┴───────────────────────────────────┤
│  [Deletion toast — hidden by default]                 │  ← 36px, slides up
└──────────────────────────────────────────────────────┘
```

---

## Tab search & new button (left panel header)

```
┌─ 180px ───────────────────────────┐
│  [🔍 Search tabs…         ]  [＋]  │  ← 40px tall header row
└───────────────────────────────────┘
```

- Search box takes all available width minus the new tab button.
- `[＋]` is icon-only, `26 × 26 px`, right of the search input, same row.
- `[＋]` tooltip: `"New tab (Ctrl+T)"`.
- Clicking `[＋]` is identical to `Ctrl+T`.
- `Ctrl+F` focuses the search box from anywhere.
- Escape clears search and returns focus to editor.

**Search scope:** The search box filters tabs **within the current desktop only**. It matches against the tab label (custom name or content preview) and the full note content. Matches are highlighted in the tab list.

---

## Tab list (left panel)

Fixed width `180 px`, vertically scrollable.

Each tab entry shows:
- 📌 pin icon (if pinned) — always sorted to top of list
- Tab label — see fallback rules below
- Created date (left) and last updated time (right) on the meta line
- Active tab highlighted with a `2 px` left accent border

**Drag-to-reorder:** within zones only (pinned zone / unpinned zone separately). Pin toggle is the only way to change zone.

---

### Tab label fallback priority

1. `name` column — custom name set via rename, always takes precedence
2. First ~30 characters of `content` (trimmed, collapsed whitespace)
3. `"New note"` — shown muted/italic — for empty tabs with no custom name

---

### Tab rename

- **Double-click** on tab label → inline rename
- **F2** → inline rename on active tab
- **Right-click → Rename** → same
- Label becomes an editable text field in place
- **Enter** or click outside → commit
- **Escape** → cancel, restore previous name
- **Empty / whitespace** submitted → clears `name` column; tab reverts to content/fallback

---

### Tab hover behaviour

On hover, a 🗑 delete icon appears in the **upper-right corner** of the tab entry:
- Icon size: `12 px`, positioned `4 px` from top, `6 px` from right
- Preview text line is right-truncated with `…` to avoid colliding with icon (add `20 px` right padding when icon is visible)
- Icon colour: `c-text-muted` at rest → `#e74c3c` on icon hover
- Clicking the icon deletes immediately — **no confirmation dialog**
- Fades in `100 ms` on tab hover, fades out on mouse leave

---

### Tab middle-click

Middle-clicking any tab deletes it immediately — **no confirmation dialog**.

---

### Tab deletion — all triggers

All delete triggers delete immediately. A deletion toast appears at the bottom of the window (see below).

**Post-delete focus rule:**
1. Focus moves to the **first tab below** the deleted tab.
2. If none below (was last), focus moves to the **last tab** in the list.
3. If no tabs remain, create a single new empty tab and focus it.

---

## Deletion toast

Appears at the **bottom of the window** on every deletion:

```
┌──────────────────────────────────────────────────────┐
│  "Meeting Notes" deleted        [ Undo ]          [ ✕ ]  │  ← 36px tall
└──────────────────────────────────────────────────────┘
```

**Behaviour:**
- Slides up from bottom — `translateY(100% → 0)`, `150 ms ease-out`
- Auto-dismisses after **4 seconds**, slides back down
- **Undo** restores the tab: same content, same position, same custom name — dismisses toast
- New deletion while toast visible → replaces the toast; previous deletion is now **permanent**
- Toast Undo is independent of editor `Ctrl+Z` — it only restores the deleted tab
- **Bulk delete:** toast shows `"N notes deleted"` with one Undo for all

**Layout:**
- Full width of the window, fixed to bottom edge, pushes no content up
- Background: `c-toolbar-bg`, top border `1px c-border`
- Tab name in quotes, italic, truncated at 30 chars
- Undo: `c-accent` text, underline on hover, font-weight 600
- ✕: `c-text-muted` → `c-text` on hover, dismisses without undo

---

## Toolbar (above editor)

```
[⟲][⟳]  |  [📌][⎘]  |  [📋][📄]  |  [💾]          →→→  [🗑]
 Edit        Tab       Clipboard    Export       spacer    Delete
```

The Delete button is right-aligned via a flex spacer.

| Group | Button | Action | Shortcut | Tooltip |
|---|---|---|---|---|
| Edit | ⟲ Undo | Undo last edit | Ctrl+Z | `Undo (Ctrl+Z)` |
| Edit | ⟳ Redo | Redo | Ctrl+Y | `Redo (Ctrl+Y)` |
| Tab | 📌 Pin/Unpin | Toggle pin on active tab | Ctrl+P | `Pin / Unpin (Ctrl+P)` |
| Tab | ⎘ Clone | Clone active tab | Ctrl+K | `Clone tab (Ctrl+K)` |
| Clipboard | 📋 Copy | Copy selection; full note if nothing selected | Ctrl+C | `Copy (Ctrl+C) — copies full note if nothing selected` |
| Clipboard | 📄 Paste | Paste at cursor | Ctrl+V | `Paste (Ctrl+V)` |
| Export | 💾 Save as TXT | OS save dialog, exports `.txt` | Ctrl+S | `Save as TXT (Ctrl+S)` |
| *(spacer)* | | Flex spacer | | |
| Delete | 🗑 Delete | Delete active tab — toast shown | Ctrl+W | `Delete tab (Ctrl+W)` |

**Tooltip delay:** 600 ms hover.  
**Delete button:** default opacity 0.7, colour `#e74c3c`, hover opacity 1.0.

---

### Copy behaviour
- Text selected → copy selection only (standard)
- No selection → copy **entire note content** to clipboard silently (no visual confirmation)

### Paste behaviour
Standard paste at cursor. If pasted into empty tab, preview updates immediately.

### Save as TXT behaviour
- Default filename: custom tab name → first line of content (sanitised) → `jojot-YYYY-MM-DD-HHmm.txt`
- Encoding: UTF-8 with BOM
- Content: raw text only — no metadata

---

## Theming

Three themes: **Light**, **Dark**, **System** (follows Windows app mode). Global preference, not per-desktop. Theme switching is instant — WPF `ResourceDictionary` swap.

| Token | Light | Dark |
|---|---|---|
| `c-win-bg` | `#FFFFFF` | `#1E1E1E` |
| `c-tab-bg` | `#F5F5F5` | `#252526` |
| `c-tab-active` | `#DDEEFF` | `#37373D` |
| `c-tab-hover` | `#E8E8E8` | `#2D2D2D` |
| `c-accent` | `#0078D4` | `#4FC3F7` |
| `c-editor-bg` | `#FFFFFF` | `#1E1E1E` |
| `c-text` | `#1E1E1E` | `#D4D4D4` |
| `c-toolbar-bg` | `#F0F0F0` | `#2D2D2D` |
| `c-toolbar-icon` | `#333333` | `#CCCCCC` |
| `c-toolbar-icon-hover` | `#0078D4` | `#4FC3F7` |

**System** mode re-evaluates on `SystemEvents.UserPreferenceChanged`.
