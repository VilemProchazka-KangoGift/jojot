# Phase 5: Deletion & Toast - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Tab deletion with five triggers (Ctrl+W, toolbar button, tab hover icon, middle-click, context menu), immediate execution with no confirmation dialog, and a 4-second undo toast with slide-up animation. Bulk deletion infrastructure with pinned-tab protection. Toolbar button (Phase 7) and context menu triggers (Phase 8) are wired later — Phase 5 builds the deletion engine, toast system, and the triggers it can implement now (Ctrl+W, hover icon, middle-click).

</domain>

<decisions>
## Implementation Decisions

### Toast visual design
- Toast spans editor area only (Grid column 2), not full window width
- Dark charcoal background (#333333) with white text
- Left-aligned text: `"tab name" deleted` (tab name in quotes/italic, truncated 30 chars per TOST-06)
- Right-aligned "Undo" link in accent blue (#2196F3) with underline
- 36px tall, full width of editor column, anchored to bottom
- Slide-up animation: translateY 100%→0, 150ms ease-out (WPF Storyboard)
- Auto-dismiss after 4 seconds with slide-down exit animation
- No countdown indicator or progress bar — clean disappearance
- New deletion while toast visible: instant content swap (no re-animation), timer resets, previous deletion becomes permanent

### Delete hover icon
- × (close/X) rendered as text, 12px font size
- Positioned upper-right of the tab item (per TDEL-03)
- Appears on hover with 100ms opacity fade-in (0→1)
- Shows on ALL tabs on hover, including the active (selected) tab
- Default color: muted gray (#888888)
- On hover over the × itself: red (#e74c3c), matching future toolbar delete button (TOOL-02)

### Focus rules after delete
- Deleting a NON-ACTIVE tab: active tab and editor content stay unchanged
- Deleting the ACTIVE tab: focus cascade per TDEL-05 — first tab below → last tab in list → auto-create new empty tab
- No empty state screen — always auto-create an empty tab when last tab is deleted (consistent with existing LoadTabsAsync pattern)
- Search stays active during delete: cascade applies within filtered results; if no filtered tabs remain, clear search then cascade on full list
- Undo restoration: restored tab becomes the active tab automatically and is selected in the tab list

### Bulk delete infrastructure
- Phase 5 builds the engine (DeleteMultipleAsync, multi-tab toast, multi-tab undo) but no bulk UI triggers
- Bulk triggers arrive in Phase 7 (toolbar) and Phase 8 (context menu: delete all below, delete older than, delete all except pinned)
- Bulk toast shows "N notes deleted" with single Undo button
- All-or-nothing undo: restores ALL tabs to their original positions, no selective restore

### Deletion model
- Soft delete: remove tab from _tabs collection and UI immediately, hold NoteTab object(s) in memory
- After 4 seconds (toast expires): commit hard delete via DatabaseService.DeleteNoteAsync
- On undo: re-insert NoteTab into _tabs at original sort_order position, rebuild tab list, select restored tab
- On new deletion replacing toast: immediately hard-delete the previous pending deletion, then soft-delete the new one

### Claude's Discretion
- Exact WPF Storyboard implementation for slide-up/slide-down animations
- Internal data structure for pending deletions (single field vs queue)
- Whether the toast is a UserControl or inline XAML in MainWindow
- Edge case handling for rapid successive deletes

</decisions>

<specifics>
## Specific Ideas

- Toast color (#333) with white text matches VS Code's undo toast / Slack snackbar pattern — high contrast, feels like a system notification
- × icon color transition (gray → red) pre-aligns with Phase 7 toolbar delete button color (#e74c3c) for visual consistency
- Soft delete model avoids DB round-trips during the undo window — only one DB call happens (either delete on expire or no-op on undo)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `NoteTab` model: Has all properties needed for undo restore (Id, Name, Content, Pinned, SortOrder, CursorPosition, EditorScrollOffset)
- `DatabaseService.DeleteNoteAsync(long noteId)`: Already exists for hard delete
- `DatabaseService.InsertNoteAsync(...)`: Available for undo re-insert if needed (but soft-delete model avoids this)
- `AccentBrush` (#2196F3): Defined in MainWindow — reuse for Undo link color
- `MutedTextBrush` (#888): Defined in MainWindow — reuse for × icon default color

### Established Patterns
- Tab items built programmatically via `CreateTabListItem()` — extend with hover × icon
- `RebuildTabList()` / `SelectTabByNote()` used for refreshing after mutations — reuse for delete + undo
- Fire-and-forget async pattern (`_ = DatabaseService.Method()`) used throughout — follow for hard delete on expire
- `_tabs` ObservableCollection with manual sort_order management — undo needs to restore sort_order correctly

### Integration Points
- `MainWindow.Window_PreviewKeyDown`: Add Ctrl+W handler for active tab deletion
- `CreateTabListItem()`: Add × icon with hover events + middle-click handler on ListBoxItem
- XAML Grid (MainWindow.xaml): Add toast overlay element in Grid column 2

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-deletion-toast*
*Context gathered: 2026-03-02*
