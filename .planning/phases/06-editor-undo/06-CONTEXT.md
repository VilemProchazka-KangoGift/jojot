# Phase 6: Editor & Undo - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can write plain text that autosaves reliably, undo/redo across tab switches using a custom two-tier stack, copy full notes silently, and export to UTF-8 TXT files. The editor TextBox already exists from Phase 4 (Consolas 13pt, word-wrap, IsUndoEnabled=False). This phase adds autosave, the undo system, and copy/export behaviors.

</domain>

<decisions>
## Implementation Decisions

### Autosave feel
- Fully invisible — no save indicator, no status bar, no dot in title bar
- Save failures handled with silent retry on the next debounce cycle; error logged only
- Reset-on-keystroke debounce: timer resets each keystroke, save fires 500ms after the LAST keystroke
- On app close: block until save completes (synchronous flush), no optimistic fire-and-forget

### Undo granularity
- Autosave-aligned: each Ctrl+Z jumps to the previous autosave snapshot (~500ms typing gaps)
- No finer word-boundary or character-level undo — snapshots are the granularity unit
- Seamless tier transition: no visual indication when crossing from tier-1 (50 snapshots) to tier-2 (20 checkpoints). The two tiers are an implementation detail, not user-facing
- Linear undo/redo stack: typing after undo destroys the redo future (standard behavior)
- Initial content from database load counts as the first undo snapshot — Ctrl+Z can always restore to the loaded state

### Copy & export
- Ctrl+C with no selection copies entire note silently — no visual feedback (flash, toast, or otherwise)
- Save as TXT dialog remembers last used directory in-memory per session; resets on app launch
- Default filename: "JoJot note YYYY-MM-DD.txt" when tab has no name and no content
- No feedback after successful save — the OS dialog closing is sufficient confirmation

### Memory collapse UX
- Collapse is fully silent — no notification, no per-tab indicator
- 50MB budget is a hardcoded constant, not configurable
- No per-tab cap — active tab's undo stack is never touched regardless of size
- Undo stack survives the soft-delete period (Phase 5 toast) — if tab is restored within 4 seconds, its full undo history is intact

### Claude's Discretion
- Exact UndoStack class design and data structures
- Tier-2 checkpoint timing implementation
- Memory estimation approach for the 50MB budget
- How to intercept Ctrl+Z/Y before WPF's native handling
- Scroll offset save/restore implementation details

</decisions>

<specifics>
## Specific Ideas

- The autosave should feel like Apple Notes or Obsidian — completely invisible, the user trusts it works
- Undo should feel predictable: each Ctrl+Z is one meaningful chunk of work (the debounce interval worth of typing)
- The editor is the core of JoJot — reliability over cleverness. No data loss, ever.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ContentEditor` TextBox (MainWindow.xaml:80-87): Already configured with Consolas 13pt, AcceptsReturn, AcceptsTab, TextWrapping=Wrap, IsUndoEnabled=False
- `SaveCurrentTabContent()` (MainWindow.xaml.cs:411-424): Current immediate-save method — needs to be replaced with debounced autosave
- `DatabaseService.UpdateNoteContentAsync()`: Existing async write to SQLite for note content
- `NoteTab` model: Has `EditorScrollOffset` and `CursorPosition` fields already defined
- Toast infrastructure (Phase 5): Existing slide-up toast for deletion undo — pattern available if ever needed

### Established Patterns
- Fire-and-forget async DB writes: `_ = DatabaseService.UpdateNoteContentAsync(...)` — current pattern for saves
- Tab switch saves content to in-memory model before switching (TabList_SelectionChanged handler)
- Window_PreviewKeyDown handles global keyboard shortcuts (Ctrl+T, Ctrl+F, Ctrl+K, etc.)
- Phase 5 soft-delete uses CancellationTokenSource for timed operations — similar pattern useful for debounce timer

### Integration Points
- `TabList_SelectionChanged`: Where undo stack binding/unbinding needs to happen on tab switch
- `Window_PreviewKeyDown`: Where Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, Ctrl+S, and enhanced Ctrl+C need to be intercepted
- `OnClosing` override: Where synchronous flush must happen before window destruction
- `_pendingDeletion` (Phase 5): Undo stack should be stored alongside PendingDeletion record during soft-delete

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-editor-undo*
*Context gathered: 2026-03-03*
