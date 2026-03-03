# Phase 4: Tab Management - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can create, rename, search, reorder, pin, clone, and navigate tabs, with each tab displaying a smart label derived from its content. This is the first real UI phase — it introduces the tab panel, tab entries, and a simple TextBox content area (full editor is Phase 6). Requirements: TABS-01 through TABS-13.

</domain>

<decisions>
## Implementation Decisions

### Panel placement & chrome
- Tab panel on the LEFT side of the window, 180px fixed width
- 1px subtle vertical border line separates panel from content area (works before theming in Phase 7)
- Distinct fixed header bar at top of panel containing search box and + button, with subtle bottom border separating it from the scrollable tab list
- + (new tab) button sits to the right of the search box in the header; search box takes remaining width

### Tab item layout
- Two-row layout per tab entry (~40-48px tall):
  - Row 1: pin icon (if pinned) + label (truncated with ellipsis)
  - Row 2: created date (left-aligned) + updated time (right-aligned)
- "New note" placeholder label in muted italic per spec (TABS-02)
- Relative smart date formatting: "Just now", "5 min ago", "Today 2:30 PM", "Yesterday", "Mar 1", "Jan 15, 2025"
- Pinned tabs: pin icon before label + subtle zone separator line/label between pinned and unpinned zones (makes drag-to-reorder boundary explicit)

### Content area placeholder
- Simple WPF TextBox that loads/saves content from the notes table (Phase 6 replaces with full editor)
- Content saves on tab switch and window close — no background autosave timer (Phase 6 adds debounced autosave)
- Monospace font from day one: Consolas 13pt, word-wrap on, no horizontal scrollbar (matches EDIT-01 spec)
- Auto-create first empty tab when window opens with no tabs — no empty state screen needed

### Drag & interaction feel
- Drag-to-reorder: thin horizontal accent-colored drop indicator line between tabs, dragged tab at 0.6 opacity
- Tab hover: subtle background color highlight on non-active tabs
- Search box: always visible in header with placeholder text; Ctrl+F focuses it; real-time filtering as user types; Escape clears and returns focus to editor
- Search filtering: non-matching tabs hidden entirely (not dimmed) — clean focused list of matches only

### Claude's Discretion
- Exact pixel dimensions and spacing within tab entries
- Accent color values (pre-theming hardcoded values, Phase 7 replaces with tokens)
- Animation easing curves and exact durations for drag feedback
- Internal TextBox implementation details (scroll behavior, selection handling)
- Keyboard focus management specifics beyond what's in requirements

</decisions>

<specifics>
## Specific Ideas

No specific references — open to standard approaches. The two-row tab layout with smart dates was chosen for readability within the 180px constraint. The simple TextBox content area makes Phase 4 immediately usable rather than view-only.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DatabaseService` (Services/DatabaseService.cs): Full CRUD infrastructure with write serialization. Notes table already has all columns needed: id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, editor_scroll_offset, cursor_position
- `LogService` (Services/LogService.cs): Logging throughout the app
- `WindowPlacementHelper` (Services/WindowPlacementHelper.cs): Window geometry save/restore — already integrated into MainWindow.OnClosing
- `VirtualDesktopService` (Services/VirtualDesktopService.cs): Desktop GUID resolution — used to scope tabs to the correct desktop

### Established Patterns
- Static services with async methods and SemaphoreSlim write serialization (DatabaseService pattern)
- XAML code-behind model (partial classes, no MVVM framework)
- PascalCase naming, single `JoJot` namespace, files in Services/, Models/, Interop/ subdirectories
- Fire-and-forget async for non-critical DB writes (see MainWindow.OnClosing geometry save)

### Integration Points
- `MainWindow.xaml`: Currently empty Grid — becomes the tab panel + content area layout
- `MainWindow.RequestNewTab()`: Stub method waiting for Phase 4 implementation
- `MainWindow.FlushAndClose()`: Needs to flush tab content before close
- `App.CreateWindowForDesktop()`: Needs to load tabs for the desktop after window creation
- `IpcService` NewTabCommand: Routes through App to MainWindow.RequestNewTab()
- Notes table query: `SELECT * FROM notes WHERE desktop_guid = @guid ORDER BY pinned DESC, sort_order ASC`

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 04-tab-management*
*Context gathered: 2026-03-02*
