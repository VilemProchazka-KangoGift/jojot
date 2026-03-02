# JoJot — Menus & Context Menus

---

## Window menu (`[≡]`)

| Item | Behaviour |
|---|---|
| Recover sessions… | Opens orphaned session panel (see `02-virtual-desktops.md §orphaned`). Badge shown on menu button when orphaned sessions exist. |
| Delete all older than… | Dialog: input N days. Deletes all **non-pinned** tabs where `updated_at < now - N days`. Pinned tabs excluded. Confirmation required. |
| Delete all except pinned | Deletes every non-pinned tab on this desktop. Confirmation required. |
| Delete all | Deletes every **non-pinned** tab on this desktop. Pinned tabs always preserved. Confirmation required. |
| ─── | |
| Preferences… | Opens Preferences dialog (see `07-preferences.md`) |
| Exit | Flush all content + terminate background process |

> **Note:** There is no way to bulk-delete pinned tabs from the menu. Pinned tabs must be unpinned individually first.

Menu-level bulk deletes still show a confirmation dialog (unlike single-tab deletes which use the toast). After confirmation, a single toast appears: `"N notes deleted"` with one Undo.

---

## Tab context menu (right-click on tab)

| Item | Behaviour |
|---|---|
| Rename | Inline rename: label becomes editable field. Enter commits, Escape cancels, blank/whitespace clears custom name (reverts to content/fallback). |
| Pin / Unpin | Toggle pin state |
| Clone to new tab | Duplicate content into new tab immediately below this one |
| Save as TXT… | Export this tab's content via OS save dialog |
| Delete | Delete this tab immediately — deletion toast shown |
| Delete all below | Delete all **non-pinned** tabs below this one; pinned tabs below are silently skipped |
