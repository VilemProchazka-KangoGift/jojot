# Phase 1: In-Editor Find Panel — Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Add a find-and-replace side panel to the editor, triggered by toolbar button or Ctrl+F/Ctrl+H. Replace the existing inline EditorFindBar with a full-featured side panel following the established panel pattern (PreferencesPanel, CleanupPanel, RecoveryPanel). Includes real-time search-as-you-type, match highlighting via adorner overlay, case/whole-word toggles, and replace operations.

</domain>

<decisions>
## Implementation Decisions

### Panel Design
- Side panel UserControl in `Controls/`, following PreferencesPanel pattern (right-aligned, slide-in, 300-320px wide)
- Toolbar gets a search icon button to open the panel
- Ctrl+F opens find mode, Ctrl+H opens find+replace mode (replace row visible)
- Opening the find panel closes any other open side panel (one panel at a time)
- Real-time search as you type — no submit/search button needed
- Panel stays open when switching tabs; re-searches the new tab's content automatically

### Find Behavior
- Selection auto-populates the find input when Ctrl+F is pressed with text selected
- Search query persists across tabs (same term carries over when searching different notes)
- Enter cycles to next match, Shift+Enter cycles to previous match
- Escape closes the panel and returns focus to editor

### Replace Operations
- Two operations: Replace (single, replaces current match and auto-advances to next) and Replace All
- Replace All shows a brief inline count ("5 replacements made")
- Replace All is undoable as a single Ctrl+Z action (one undo checkpoint)

### Match Option Toggles
- Case sensitivity (Aa) and Whole word (W) toggle buttons
- Positioned between the find input and the match counter
- Both off by default (case-insensitive, partial word matching)
- Toggles apply to both find and replace matching

### Visual Highlighting
- All matches highlighted in the editor using an adorner overlay on the existing TextBox
- Active match: stronger/brighter highlight color (e.g., orange)
- Other matches: softer highlight color (e.g., light yellow)
- Highlight colors defined in both LightTheme.xaml and DarkTheme.xaml (theme-aware)

### Cleanup
- Remove existing inline EditorFindBar (MainWindow.xaml Grid at line ~380) and all related code
- Tab sidebar search (SearchBox) remains — different use case (filtering tabs)

### Claude's Discretion
- Adorner implementation details (positioning, re-render triggers)
- Panel slide animation (speed, easing)
- Exact highlight colors for light/dark themes
- Panel width (300-320px range)
- Icon choices for toolbar button and toggle buttons

</decisions>

<specifics>
## Specific Ideas

- Ctrl+F/Ctrl+H convention matches VS Code and Notepad++ — familiar to users
- "No textbox in editor or toolbar — just a toolbar button and the side panel" (user was explicit about this)
- Real-time search means the find input has a TextChanged handler, not a submit button
- The tab search feature (SearchBox in sidebar) is a completely separate feature and stays as-is

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `PreferencesPanel` (Controls/PreferencesPanel.xaml): Template for side panel UserControl with slide-in animation, header+close, scrollable content
- `MainWindowViewModel.FindAllMatches()`: Static method for case-insensitive string matching — returns list of match positions
- `MainWindowViewModel.CycleIndex()`: Wrapping index cycling for next/previous match
- `MainWindowViewModel.FormatFindCountText()`: Formats "1/5" or "No matches" display text
- `FindEngineTests` (28 tests): Existing test coverage for find engine logic
- `UndoManager`/`UndoStack`: Per-tab undo with checkpoint snapshots — Replace All needs to create a single checkpoint

### Established Patterns
- Side panels: UserControl with `Visibility="Collapsed"`, `TranslateTransform` for slide animation, `Panel.ZIndex` layering (80-85), right-aligned in main Grid
- `CloseAllSidePanels()` in ViewModel — find panel needs to integrate with this
- Theme colors via `DynamicResource` keys in LightTheme.xaml/DarkTheme.xaml
- Keyboard shortcuts: Simple ones via InputBindings, context-dependent ones in PreviewKeyDown
- Ctrl+F currently handled in PreviewKeyDown (context-dependent: editor focused vs tab search focused)

### Integration Points
- Toolbar: Add search icon button (alongside existing toolbar buttons)
- `MainWindow.Keyboard.cs`: Update Ctrl+F handler to open side panel instead of inline bar; add Ctrl+H handler
- `MainWindow.Search.cs`: Remove inline EditorFindBar methods, add side panel show/hide methods
- `MainWindow.xaml`: Remove EditorFindBar Grid, add FindReplacePanel UserControl
- `MainWindow.xaml.cs`: Remove `_findMatches`/`_currentFindIndex` fields, integrate with new panel
- Theme files: Add new highlight color resource keys (find-match-bg, find-match-active-bg)
- ViewModel: May need `IsFindPanelOpen` property for panel state management

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-add-in-editor-find-panel-with-ctrl-f*
*Context gathered: 2026-03-10*
