# JoJot — Virtual Desktops & Process Lifecycle

This is the most complex part of JoJot. Read fully before implementing anything in this area.

---

## Core concepts

**One window per virtual desktop.** Each Windows virtual desktop gets its own independent JoJot window with its own tabs and saved state.

**One background process.** A single JoJot process runs at all times (after first launch), managed as a system tray / taskbar app. Individual windows are created and destroyed per desktop as needed.

**Desktop identity.** Each desktop is identified by a GUID from `IVirtualDesktopManager`. This GUID is the primary key for all per-desktop state. Because GUIDs are reassigned on reboot, JoJot also stores the desktop name and index as fallback identifiers.

---

## Taskbar icon click behaviours

### Scenario A — Left click, window already exists for this desktop

> User is on Desktop "Work". A JoJot window for "Work" is already open (possibly minimised or behind other windows).

**Result:** Bring existing window to foreground and focus it. No new window, no new tab.

---

### Scenario B — Left click, no window exists for this desktop

> User is on Desktop "Personal". No JoJot window has been opened on this desktop yet.

1. Background process detects current desktop GUID.
2. Looks up saved state using three-tier match (see below).
3. **If saved session found:** open window restoring saved tabs, geometry, and active tab.
4. **If no saved session:** open window with a single empty tab.

---

### Scenario C — Middle click (any desktop)

Global "quick capture" shortcut. Always creates a new empty tab on the current desktop.

**C1 — Window for current desktop already exists:**
1. Bring window to foreground if not visible.
2. Create new empty tab.
3. Focus editor. User can type immediately.

**C2 — No window for current desktop yet:**
1. Spawn new window for this desktop (same as Scenario B steps 1–2).
2. Do **not** restore the last active tab — immediately create a new empty tab and focus it.
3. Previously saved tabs are loaded into the list and visible, but the new empty tab is active.

**Important:** Middle-click never switches the user to a different desktop. The new tab always opens on whichever desktop is currently active at the moment of the click.

---

## Window title

| Situation | Title format |
|---|---|
| Named desktop | `JoJot — Work` |
| Unnamed desktop | `JoJot — Desktop 2` |
| Name unresolvable | `JoJot` |

Title updates live if the user renames the desktop mid-session via `IVirtualDesktopNotification` event subscription.

---

## Session matching on startup

GUIDs are reassigned on every reboot. JoJot uses a three-tier fallback:

| Priority | Match method | Field | Reliability |
|---|---|---|---|
| 1 | Exact GUID match | `desktop_guid` | Perfect — within same boot session only |
| 2 | Desktop name match | `desktop_name` | Good — works across reboots if desktops are named |
| 3 | Desktop index match | `desktop_index` | Weak — last resort only |
| — | No match | — | Session marked orphaned |

First hit wins. The stored GUID is updated to the current live GUID so future lookups within this session use exact matching.

**Index matching caveat:** Only auto-adopt if there is exactly one unmatched session and one unmatched desktop at that index — never if ambiguous.

**Name ambiguity:** If two orphaned sessions have the same desktop name, both are surfaced in the recovery panel. Do not silently merge or overwrite.

---

## Virtual Desktop API failure modes

| Failure | Fallback |
|---|---|
| GUID cannot be resolved at launch | Fall back to `"default"` GUID — app works as single-instance notepad, no desktop scoping |
| Desktop name cannot be resolved | Store `NULL` for `desktop_name`; skip tier-2 matching |
| Desktop index cannot be resolved | Store `NULL` for `desktop_index`; skip tier-3 matching |
| `IVirtualDesktopNotification` events stop firing | Poll `IVirtualDesktopManager::GetWindowDesktopId` on every `WM_ACTIVATEAPP`; if GUID changed, treat as drag event and show lock overlay |
| `MoveWindowToDesktop` fails during Cancel | Keep overlay active; replace Cancel with **Retry** + message: *"Automatic move failed. Please move this window back to '[desktop name]' manually via Task View, then click Retry."* |

---

## Orphaned session recovery

When no session match is found, the session is not lost — it becomes **orphaned** and stays in the DB until the user acts.

A **"Recover sessions…"** item in the `[≡]` menu opens a recovery panel listing all orphaned sessions. Each entry shows last known desktop name (or `Desktop N`), number of tabs, and date last updated.

**Actions per orphaned session:**
- **Adopt into current desktop** — merges orphaned tabs below existing tabs
- **Open as new window** — spawns an unscoped window for the session
- **Delete** — permanently removes the session and all its tabs

On startup, if orphaned sessions exist, a small non-blocking badge appears on the `[≡]` menu button (no dialog).

---

## Dragging a window to another desktop

Windows allows dragging app windows between virtual desktops via Task View. JoJot detects this via `IVirtualDesktopNotification::OnWindowMovedToDesktop` and immediately locks the window.

**Detection and lock sequence:**
```
1. User drags JoJot window from "Work" to "Personal" in Task View
2. OS moves window — it is now physically on "Personal"
3. OnWindowMovedToDesktop fires → JoJot detects GUID changed
4. Write row to pending_moves (origin GUID, target GUID, window handle)
5. Apply lock overlay immediately
6. All editing and tab interaction disabled
7. User resolves via overlay buttons
8. Lock released, state committed or rolled back, pending_moves row deleted
```

If JoJot crashes between steps 4 and 8, the `pending_moves` row is detected on next launch and the window is automatically restored to its original desktop.

**Re-trigger:** A persistent ⚠ badge in the title bar is shown whenever the window's scoped GUID doesn't match the current desktop GUID. Clicking it re-triggers the overlay.

---

### Lock overlay appearance

Semi-transparent dark overlay (`rgba(0,0,0,0.65)`) covers the entire window content. Content is visible but dimmed and non-interactive. Centred on the overlay:

```
        ⚠  Window moved to "Personal"
   This window belongs to "Work".
   What would you like to do?

      [ Reparent ]     [ Cancel ]
```

The title bar remains visible above the overlay. The overlay is always dark regardless of theme. All keyboard shortcuts suppressed while active.

---

### Overlay options

Buttons shown depend on whether the target desktop already has a session:

| Target desktop state | Buttons shown |
|---|---|
| No existing JoJot session | **Reparent**, **Cancel** |
| Existing JoJot session | **Merge**, **Cancel** |

Reparent and Merge are never shown together.

**Reparent** *(only when target desktop has no session)*
- Window re-scopes to new desktop. All `notes` rows updated to new GUID.
- `app_state` for original desktop cleared; new record created for target.
- If original desktop had notes not loaded here, they remain as orphaned session.

**Merge** *(only when target desktop already has a session)*
- Dragged window's tabs appended to existing window (pinned tabs → bottom of pinned zone; unpinned → below all unpinned).
- `sort_order` recalculated. Original desktop's `app_state` deleted. Locked window closes.

**Cancel**
- Window moved back to original desktop via `IVirtualDesktopManager::MoveWindowToDesktop`.
- No state changes. `pending_moves` row deleted.
- If move fails: replace Cancel with Retry + manual instruction.

---

### Drag edge cases

| Situation | Behaviour |
|---|---|
| Dragged to desktop with minimised JoJot window | Counts as existing session → Merge + Cancel shown |
| Dragged again before resolving first move | Second drag ignored — overlay stays for unresolved move |
| Crash while overlay active | `pending_moves` row detected on startup → window restored automatically |
| Notifications missed, polling detects mismatch on focus | ⚠ badge shown; clicking re-triggers overlay |

---

## IPC & process communication

**Named mutex:** `Global\JoJot_SingleInstance` — acquired by background process on startup, held for lifetime.

**Named pipe:** `\\.\pipe\JoJot_IPC` — background process listens. New instances connect and send a message before exiting.

**Message format (JSON):**
```json
{ "action": "left-click",   "desktop_guid": "..." }
{ "action": "middle-click", "desktop_guid": "..." }
```

**Failure modes:**

| Situation | Behaviour |
|---|---|
| Mutex held, pipe connects | Send message, exit cleanly |
| Mutex held, pipe times out (> 500 ms) | Assume hung; force-kill via PID, start fresh |
| Mutex held, pipe fails immediately | Same — force-kill and restart |
| Mutex not held | Start normally as background process |

The launching instance resolves the current desktop GUID before sending — the background process may not know which desktop is active at the moment of the click.

---

## Process lifecycle

```
First launch
  └─ Start background process
  └─ Resolve current desktop GUID
  └─ Open / create jojot.db
  └─ Spawn window for current desktop (Scenario B)

Subsequent launches (process already running)
  └─ New instance detects existing process via named mutex
  └─ Sends IPC: "left-click on desktop {GUID}"
  └─ Existing process handles (Scenario A or B)
  └─ New instance exits immediately

Middle-click on taskbar icon
  └─ Taskbar sends IPC: "middle-click on desktop {GUID}"
  └─ Background process handles (Scenario C1 or C2)

User closes window (X button)
  └─ Flush unsaved content
  └─ Delete all empty tabs for this desktop
  └─ Save window geometry
  └─ Destroy window — background process stays alive

User exits via [≡] → Exit
  └─ Flush all unsaved content across all open windows
  └─ Delete all empty tabs across all desktops
  └─ Terminate background process
```
