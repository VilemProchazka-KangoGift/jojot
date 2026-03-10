# Phase 12: Tab Panel UX - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Make the tab panel visually clear, pin-aware, and user-resizable. The tab panel's selected-tab highlight changes from a left-edge border to a distinct background color, pinned tabs show a visible icon, the title text adapts when the delete icon is visible, a draggable divider allows resizing the panel width, and drag-to-reorder continues to work. No new tab features or capabilities are added.

</domain>

<decisions>
## Implementation Decisions

### Selected tab highlight
- Replace the current 2px left accent border with a distinct background color
- Remove the left-edge border entirely — background color alone signals selection
- Non-selected tabs retain hover background (c-hover-bg) on mouse enter
- Selected tab does not change on hover — stays solid

### Pin icon styling
- All visual details (icon type, position, grouping, delete behavior) are Claude's discretion
- Must be a "visible pin icon that distinguishes [pinned tabs] from unpinned tabs" per success criteria

### Panel resize
- Add a draggable divider (GridSplitter) between tab panel and editor
- All details (persist width, min/max, divider appearance) are Claude's discretion
- Must allow user to "drag the divider to resize the panel width to their preference" per success criteria

### Title + delete icon space
- When delete icon is visible on hover, title shortens to fill remaining space
- When no delete icon is shown, title spans the full tab width
- All adaptive behavior details (animation, dynamic fill, date row, pinned space reclaim) are Claude's discretion

### Claude's Discretion
- Highlight prominence and exact background color (subtle tint vs solid — match the minimalist aesthetic)
- Whether highlight has rounded corners or fills edge-to-edge
- Pin icon: Segoe Fluent Icons vs emoji, placement (left/right of title), grouping separator
- Whether pinned tabs hide the delete icon on hover
- Panel resize: min/max width limits, divider visual style, whether width persists across sessions
- Title width: smooth animation vs instant clip on delete icon show/hide
- Whether title uses dynamic Star sizing vs fixed MaxWidth
- Whether date row adapts to panel width or stays compact
- Whether pinned tabs reclaim delete-icon space for title

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. User deferred all visual detail decisions to Claude's judgment, trusting the existing minimalist aesthetic as the guide.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CreateTabListItem()` in MainWindow.xaml.cs (line ~241): Builds tab items programmatically — the core method to modify for highlight, pin icon, and title layout changes
- `ApplyActiveHighlight()` (line ~514): Currently applies 2px left border — needs replacement with background color
- `TabList_SelectionChanged()` (line ~437): Clears old selection styling — needs update for background-based highlight
- `ToolbarButtonStyle` in MainWindow.xaml: Already uses rounded-corner hover pattern with `c-hover-bg` — reusable pattern
- Theme resources: `c-accent`, `c-hover-bg`, `c-sidebar-bg`, `c-text-muted`, `c-border` — existing theme keys
- `AnimateOpacity()`: Already used for delete icon fade — reusable for smooth transitions

### Established Patterns
- Code-behind UI construction (not MVVM): Tab items built in C#, not XAML templates
- Theme-aware via `SetResourceReference` and `DynamicResource` — never hardcoded colors
- Mouse enter/leave handlers on `outerBorder` for hover effects
- Drag-to-reorder fully wired with PreviewMouse* events on ListBoxItem

### Integration Points
- `MainWindow.xaml` Grid.ColumnDefinitions (line 59-63): Fixed 180px | 1px border | * — needs GridSplitter replacing the static border
- `NoteTab.Pinned` property: Boolean flag already available for pin-aware rendering
- `preferences` SQLite table: Available for persisting panel width if chosen
- `WindowPlacementHelper`: Existing per-window geometry persistence — could include panel width

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 12-tab-panel-ux*
*Context gathered: 2026-03-04*
