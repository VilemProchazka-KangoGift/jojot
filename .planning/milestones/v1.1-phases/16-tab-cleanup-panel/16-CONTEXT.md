# Phase 16: Tab Cleanup Panel - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the existing Delete menu options (Delete older than.../Delete all except pinned/Delete all) in the hamburger menu with a single "Clean up tabs" menu item that opens a dedicated side panel for bulk tab cleanup with age-based filtering, pinned inclusion toggle, and a scrollable preview list with confirmation before deletion.

</domain>

<decisions>
## Implementation Decisions

### Filter controls
- Single-row layout: "Clean up older than [number] [unit dropdown]" on one line
- Unit dropdown options: days, hours, weeks, months
- Preview list updates in real-time as user changes the number or unit — no Apply button
- "Include pinned" checkbox below the filter row (default unchecked)
- Delete button positioned below filter controls, above the preview list
- Delete button label shows count: "Delete 3 tabs"

### Preview list
- All-or-nothing — no individual tab selection (checkboxes). The filter determines what gets deleted
- Each row shows: tab title, content excerpt (~50 chars), and relative age (e.g., "3 days ago")
- Pinned tabs show a pin icon next to the tab name when "Include pinned" is checked
- List sorted by tab panel order (not by age)
- Read-only list — purely for previewing what will be deleted

### Post-delete flow
- After confirmed deletion, panel stays open with updated (refreshed) preview list
- If the currently active tab is deleted, switch to nearest surviving tab; if no tabs survive, create a new empty tab
- Deletion is permanent after confirmation — no soft-delete/undo toast for bulk cleanup
- The existing ConfirmationOverlay (modal backdrop + centered card) is used for the confirmation dialog

### Empty state & defaults
- Default filter: 7 days, "Include pinned" unchecked
- Filter always resets to defaults when panel opens (no persistence between opens)
- Number input: minimum 1, no upper limit
- When no tabs match: show "No tabs match this filter" in muted text where the list would be; Delete button becomes disabled

### Claude's Discretion
- Exact panel width (likely ~300-320px matching existing panels)
- TranslateTransform animation details for slide-in/out
- Confirmation dialog wording
- Spacing, typography, and visual polish within the established patterns
- How to handle the hamburger menu item icon choice

</decisions>

<specifics>
## Specific Ideas

- Delete button below filter controls, above the preview list (not fixed at bottom) — per user's chosen layout
- Recovery panel row pattern is the reference for preview list rows, but with added relative age display
- The "Delete older than..." submenu, "Delete all except pinned", and "Delete all" menu items are all removed — replaced by the single "Clean up tabs" entry

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **PreferencesPanel / RecoveryPanel pattern**: Right-aligned slide-in Border with TranslateTransform, Header + close button (X icon E711), ScrollViewer content. New cleanup panel follows the same pattern.
- **ConfirmationOverlay**: Modal confirmation dialog (backdrop + centered card with Cancel/Delete buttons) — reuse for cleanup confirmation.
- **Recovery panel row style**: Full-width rows with tab name + content excerpt — extend with age display for cleanup preview rows.
- **Segoe Fluent Icons**: Used throughout for menu icons and UI glyphs.

### Established Patterns
- Side panels use `Visibility="Collapsed"` + `Panel.ZIndex` + `Grid.ColumnSpan="3"` + `TranslateTransform` for slide animation
- Boolean flags track panel state (`_recoveryPanelOpen`, `_preferencesOpen`) — add `_cleanupPanelOpen`
- Opening one panel closes others (e.g., `if (_preferencesOpen) HidePreferencesPanel()`)
- Escape key closes active panel (checked in `OnPreviewKeyDown`)
- Hamburger menu items use Border + Grid (icon column 28px + text column) with MouseEnter/Leave/Click handlers

### Integration Points
- **MainWindow.xaml**: Add cleanup panel Border alongside PreferencesPanel/RecoveryPanel; replace three delete menu items with one "Clean up tabs" item
- **MainWindow.xaml.cs**: Add panel show/hide methods, filter change handlers, delete logic (reuse `DatabaseService` delete methods), confirmation flow
- **DatabaseService**: Existing `DeleteEmptyNotesAsync` and bulk delete queries — extend or reuse for age-based filtered deletion
- **NoteTab model**: Has `LastModified` / created timestamp for age calculations

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 16-tab-cleanup-panel*
*Context gathered: 2026-03-07*
