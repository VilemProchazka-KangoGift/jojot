# Phase 8: Menus, Context Actions & Orphaned Sessions - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can access window-level operations via a hamburger menu, tab-level operations via right-click context menu, and recover orphaned desktop sessions from a recovery panel. This phase adds the command surfaces and orphan recovery UI — no new data operations or keyboard shortcuts (those are Phase 9).

</domain>

<decisions>
## Implementation Decisions

### Recovery Panel Presentation
- Sidebar flyout that slides out from the left sidebar when triggered from the hamburger menu
- Each orphaned session displayed as a card with desktop name, tab count, last updated date, and always-visible Adopt/Open/Delete buttons
- Adopted (merged) tabs append at the bottom of the current desktop's tab list (below pinned zone)
- Flyout stays open until explicitly dismissed via X button — supports processing multiple sessions in sequence
- Badge dot disappears immediately when last orphan is processed

### Menu Placement & Visual Style
- Hamburger button placed left of the search box in the sidebar header
- Custom styled popup menu matching the app's theme (DynamicResource colors, Segoe Fluent Icons, hover highlights) — same visual treatment as the toolbar
- Tab right-click context menu uses the same custom style as the hamburger menu for consistency
- Menu items show keyboard shortcuts right-aligned in muted text (e.g., "Delete  Ctrl+W")

### Bulk Delete Flow
- "Delete older than N days" uses a submenu with presets: 7 days, 14 days, 30 days, 90 days — no custom input
- All bulk deletes (delete older than, delete except pinned, delete all) use a custom modal confirmation dialog showing the count of affected notes
- "Delete all" uses the same confirmation dialog as other bulk deletes — no extra friction
- After confirmation, deletion toast appears with "N notes deleted" and single undo (reuses existing toast system from Phase 5)

### Badge & Notification
- Small accent-colored dot (6-8px) at top-right corner of the hamburger icon when orphaned sessions exist
- Badge checks at startup only (during session matching) — not live-updated mid-session
- Badge disappears immediately when all orphaned sessions are resolved
- "Recover sessions" menu item text uses accent color when orphans exist (double signal: badge + menu item)

### Claude's Discretion
- Exact flyout animation (slide direction, duration, easing)
- Menu item spacing, padding, and separator styling
- Confirmation dialog layout and button placement
- Recovery card visual design details (borders, shadows, spacing)
- Context menu positioning relative to right-click location

</decisions>

<specifics>
## Specific Ideas

No specific external references — decisions are self-contained above.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **ToolbarButtonStyle** (MainWindow.xaml): Rounded-rectangle button with hover background and disabled opacity — reuse for hamburger menu button
- **Segoe Fluent Icons font**: Already used throughout toolbar — use for menu item icons and hamburger icon
- **Toast system** (MainWindow.xaml.cs): DeleteTabAsync, DeleteMultipleAsync, ShowToast, UndoDeleteAsync — fully supports bulk delete with undo
- **DynamicResource theme tokens**: 10+ tokens (c-win-bg, c-sidebar-bg, c-border, c-text-primary, c-text-muted, c-hover-bg, c-accent, c-toolbar-icon, etc.) for consistent theming

### Established Patterns
- Static service classes (DatabaseService, ThemeService, VirtualDesktopService) — no DI, direct static calls
- Code-behind pattern (no MVVM) — all UI logic in MainWindow.xaml.cs (1727 lines)
- Async/await for all database operations
- Tab items rendered programmatically in RebuildTabList using ListBoxItems

### Integration Points
- **Sidebar header** (MainWindow.xaml): Currently has search box + new-tab button — hamburger button goes left of search
- **VirtualDesktopService.MatchSessionsAsync**: Already counts orphaned sessions and logs them — needs to expose orphan data
- **DatabaseService**: Has GetAllSessionsAsync, GetNotesForDesktopAsync — needs methods for orphan queries and tab migration
- **MainWindow.xaml.cs**: Tab context menu handlers, menu click handlers, flyout show/hide logic all wire in here

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-menus-context-actions-orphaned-sessions*
*Context gathered: 2026-03-03*
