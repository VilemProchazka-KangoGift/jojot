# Phase 15: Review Round 2 — UI/UX Bug Fixes & Polish - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Address all issues from the second manual review of v1.1. Fix the critical note persistence bug, polish tab interactions (pin/close buttons), improve drag-and-drop visuals, refine preferences, redesign session recovery as a sidebar, clean up startup flow, and polish move-to-desktop behavior. No new features — strictly fixing and polishing existing functionality.

</domain>

<decisions>
## Implementation Decisions

### Tab button layout (R2-TAB-01, R2-TAB-02, R2-TAB-03)
- Unpinned tabs: pin icon on the left, close X on the right
- Both buttons visible on hover AND when the tab is selected; hidden otherwise
- For pinned tabs: on hover, the pin icon is replaced by an X icon (click to unpin)
- Button icon sizes and hit target padding: Claude's discretion — should look balanced with tab height and title text

### Drag-and-drop visuals (R2-DND-01, R2-DND-02)
- Ghost: semi-transparent copy (~50% opacity) of the full tab item (title + date) floating near the cursor via WPF adorner layer
- Blank space: the original slot is preserved as an empty space (same height as the tab); other tabs do NOT shift to fill the gap
- Drop indicator: a 2px accent-colored horizontal line drawn BETWEEN items at the insertion point (not on an item's border)
- Indicator hidden at the original slot AND its immediately adjacent positions (positions that wouldn't change the order)

### Session recovery sidebar (R2-RECOVER-01)
- Redesign from centered modal overlay to a sidebar panel (like preferences)
- One panel at a time: opening recovery closes preferences, and vice versa
- Actions per session card: Adopt (merge into current) and Delete only — Open button removed
- Each card shows: bold desktop name header, tab title previews (first 3–5 tab names), and last updated date
- Sidebar width: Claude's discretion — should fit content without feeling cramped

### Font size scaling (R2-FONT-01, R2-FONT-02, R2-FONT-03, R2-FONT-04)
- Tab titles and tab dates stay at FIXED sizes — they do NOT scale with the font size control
- Only the editor content area scales when font size is changed
- This eliminates the random font inconsistency between tabs (R2-FONT-04) since all tabs use constant sizes
- Reset button: keep in same position below +/- controls, change label from "Reset to 13pt" to "100%"

### Claude's Discretion
- Tab button icon sizes and hit target dimensions (balanced with tab height)
- Recovery sidebar width (fits content well)
- Ghost implementation approach (adorner layer or alternative WPF technique)
- Exact fixed font sizes for tab titles and dates

</decisions>

<specifics>
## Specific Ideas

- "On hover, cross the pin icon" → on hover the pin icon replaces with an X, click unpins
- "The dragged tab should be set to invisible (blank empty space)" → preserved slot, not collapsed gap
- "Recover Sessions should be a sidebar like preferences" → same animation pattern, one-panel-at-a-time model
- "It should explicitly state the last desktop name" → bold desktop name as card header with tab title previews
- "The tab titles are too big. Retain the original relative size" → tabs stay fixed, only editor scales
- "Reset to 13pt should just say 100%" → same position, text change only

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `PreferencesPanel` (MainWindow.xaml lines 612–757): Existing right-side sliding panel with TranslateTransform animation — recovery sidebar should follow this exact pattern
- `CreateTabListItem()` (MainWindow.xaml.cs lines 263–450): Tab item factory — needs restructuring for pin+close button layout on unpinned tabs
- `FileDropOverlay` (MainWindow.xaml lines 326–339): Full-window overlay pattern with DragEnter/DragOver/Drop handlers — extend AllowDrop to the entire window Grid
- `AutosaveService.FlushAsync()` + `_contentProvider` pattern: The persistence bug root — `_activeTab.Content` isn't updated by autosave, only the DB. Tab switching loads stale in-memory `tab.Content`
- `UpdateOrphanBadge()` (MainWindow.xaml.cs): Already hides/shows orphan badge — extend to hide the entire "Recover Sessions" menu item

### Established Patterns
- Static services with async initialization (`DatabaseService`, `ThemeService`, etc.)
- Code-behind UI construction (tabs built in C#, not XAML DataTemplates)
- Theme brush keys (`c-text-primary`, `c-text-muted`, `c-accent`, `c-selected-bg`, `c-hover-bg`, `c-sidebar-bg`) via `FindResource`
- Panel animation: TranslateTransform X slide (300→0 in, 0→300 out) with cubic ease

### Integration Points
- `TabList_SelectionChanged` (line 490): Must save `_activeTab.Content = ContentEditor.Text` before switching — fixes R2-BUG-01
- `SetFontSizeAsync` (line 2970): Must stop applying `_currentFontSize` to tab items — only set `ContentEditor.FontSize`
- `RebuildTabList()`: Currently calls `CreateTabListItem` with `_currentFontSize` — should use fixed constant for tab titles instead
- Hamburger menu items: "Recover Sessions" visibility needs to check orphan count before showing
- `LoadTabsAsync` (line 182): Startup tab loading — add empty note cleanup before loading
- `ShowDragOverlayAsync`: Move-to-desktop overlay — add source desktop name, conditionally hide "keep here"

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 15-review-round-2-ui-bug-fixes*
*Context gathered: 2026-03-04*
