# Phase 11: Critical Bug Fixes - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Eliminate three specific bugs that prevent normal tab operations: stack overflow crashes on pin/unpin (BUG-01) and delete (BUG-02), and app freeze on tab rename (BUG-03). The app must run without crashes or freezes during normal tab operations. No new features — pure stability fixes.

</domain>

<decisions>
## Implementation Decisions

### Fix strategy
- Fix the root event cascade pattern rather than patching each bug individually
- All three bugs share a common cause: `RebuildTabList()` → `SelectTabByNote()` → `TabList_SelectionChanged` re-entry loop
- A single structural fix to the event handling pattern prevents this entire class of bug from recurring
- Keep changes scoped to the event cascade mechanism — don't refactor unrelated code

### Pin/unpin fix (BUG-01)
- Same visual behavior as intended — tab jumps to pinned zone instantly, just without crashing
- No animation or smooth reorder needed — that's a polish item for another phase
- Pin/unpin must work repeatedly (toggling back and forth) without degradation

### Delete fix (BUG-02)
- Soft-delete with undo toast stays exactly the same — just eliminate the stack overflow
- Fix any visual glitches (flicker, jump) that are side effects of the crash fix if they appear
- Both single-tab and bulk delete must work

### Rename freeze fix (BUG-03)
- Fix the focus loop that causes the freeze
- Keep current rename behaviors: Escape cancels (restores original name), Enter/LostFocus commits
- Empty name clears custom name and reverts to content preview fallback (existing TABS-07 behavior)
- Double-click to rename works on all tabs (pinned and unpinned) — consistent behavior
- No max character limit on names, but display truncates with ellipsis in the tab panel

### Claude's Discretion
- Exact re-entry guard mechanism (boolean flag, event unhook/rehook timing, etc.)
- Whether to use a shared guard or per-operation guards
- Any defensive null checks needed around the fix
- Rename TextBox focus management approach to break the freeze loop

</decisions>

<specifics>
## Specific Ideas

- The root cause appears to be event cascade: `RebuildTabList()` rehooks `SelectionChanged`, then `SelectTabByNote()` triggers it, which may call code that modifies the collection again
- Rename freeze likely involves `LostFocus` → `CommitRename()` → focus change → `LostFocus` loop
- All three fixes should be verifiable by the success criteria: repeated pin/unpin, delete, and rename without crash or freeze

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `RebuildTabList()` already unhooks/rehooks `SelectionChanged` — pattern exists but timing is wrong
- `CancelRename()` and `CommitRename()` methods exist and handle the rename lifecycle
- `_activeRename` tuple tracks rename state — can be used as a guard

### Established Patterns
- Event unhook/rehook: `TabList.SelectionChanged -= TabList_SelectionChanged` used in multiple places
- `_tabs` ObservableCollection with manual `RebuildTabList()` — not using WPF data binding for tab list
- Async fire-and-forget pattern: `_ = DeleteTabAsync(tab)` used throughout

### Integration Points
- `MainWindow.xaml.cs` lines ~1059-1077: `TogglePinAsync` — pin/unpin logic
- `MainWindow.xaml.cs` lines ~1346-1358: `DeleteTabAsync` — delete logic
- `MainWindow.xaml.cs` lines ~972-991: `BeginRename` — rename start
- `MainWindow.xaml.cs` lines ~997-1015: `CommitRename` — rename commit
- `MainWindow.xaml.cs` lines ~187-230: `RebuildTabList` — central rebuild method
- `MainWindow.xaml.cs` lines ~429-499: `TabList_SelectionChanged` — selection handler

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 11-critical-bug-fixes*
*Context gathered: 2026-03-03*
