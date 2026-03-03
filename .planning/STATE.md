---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in-progress
last_updated: "2026-03-03T09:50:58Z"
progress:
  total_phases: 10
  completed_phases: 7
  total_plans: 21
  completed_plans: 19
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.
**Current focus:** Phase 8 — Menus, Context Actions & Orphaned Sessions

## Current Position

Phase: 8 of 10 (Menus, Context Actions & Orphaned Sessions)
Plan: 1 of 3 complete
Status: Phase 8 in progress — Plan 01 complete
Last activity: 2026-03-03 — Phase 8 Plan 01: hamburger menu + tab context menu complete (4 min)

Progress: [███████████████] 73%

## Performance Metrics

**Velocity:**
- Total plans completed: 18
- Average duration: ~3.8 min
- Total execution time: ~67 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1. Foundation | 3 | ~11 min | ~3.7 min |
| 2. Virtual Desktop Integration | 3 | ~16 min | ~5.3 min |
| 3. Window & Session Management | 2 | ~6 min | ~3.0 min |
| 4. Tab Management | 3 | ~13 min | ~4.3 min |
| 5. Deletion & Toast | 2 | ~10 min | ~5.0 min |
| 6. Editor & Undo | 3 | ~11 min | ~3.7 min |
| 7. Theming & Toolbar | 2 | ~10 min | ~5.0 min |
| 8. Menus & Context Actions | 1/3 | ~4 min | ~4.0 min |

**Recent Trend:**
- Last 5 plans: 06-03 (~2 min), 07-01 (~5 min), 07-02 (~5 min), 08-01 (~4 min)
- Trend: Phase 8 Plan 01 on pace (~4 min, XAML-heavy UI work)

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Pre-Phase 1]: PublishAot=true is incompatible with WPF; use PublishReadyToRun=true instead (confirmed blocker, resolved in requirements)
- [Pre-Phase 1]: Custom undo/redo required; WPF native TextBox undo clears on tab switch and cannot be used
- [Pre-Phase 1]: Single process + named pipe IPC; not multi-process — one SQLite connection, consistent state
- [01-01]: Do NOT use Cache=Shared with WAL mode (Microsoft.Data.Sqlite docs explicit warning)
- [01-01]: SemaphoreSlim(1,1) for write serialization — async-compatible unlike lock; all DB operations go through ExecuteNonQueryAsync
- [01-01]: PRAGMA quick_check over integrity_check at startup — O(N) vs O(NlogN), sufficient for table existence verification
- [01-02]: PipeOptions.Asynchronous required on NamedPipeServerStream — without it WaitForConnectionAsync ignores CancellationToken on Windows
- [01-02]: Client Connect(timeoutMs) wrapped in Task.Run — async overload lacks timeout parameter; synchronous overload blocks
- [01-02]: DispatcherPriority.ApplicationIdle for ActivateFromIpc — yields to pending WPF rendering before raising window
- [01-02]: OnClosing hides instead of closing (e.Cancel=true); only FlushAndClose calls real Close() for PROC-05/PROC-06
- [01-03]: GC.KeepAlive(_singleInstanceMutex) required — managed mutex can be collected if field appears unused; KeepAlive pins it for process lifetime
- [01-03]: ShutdownMode.OnExplicitShutdown set in code not XAML — must be set before window creation; XAML parses before OnAppStartup
- [01-03]: DispatcherUnhandledException sets args.Handled=true — prevents WPF crash dialog per "degrade gracefully" spec
- [01-03]: Environment.Exit(0) in second-instance path — ensures clean exit even if WPF internals try to keep process alive

- [02-01]: Raw COM interop with [ComImport] — no NuGet packages (none support .NET 10); build-specific GUID dictionary with floor-key lookup
- [02-01]: IVirtualDesktopNotificationService is optional — if unavailable, detection still works but live updates disabled
- [02-01]: COM boundary isolation: all COM types in JoJot/Interop/, VirtualDesktopService public API uses string/int/DesktopInfo only
- [02-02]: Tier 2 name match skips ambiguous cases (0 or 2+ desktops share name); Tier 3 requires strict one-to-one match
- [02-02]: Orphaned sessions preserved in DB for Phase 8 recovery panel; never auto-deleted
- [02-02]: UpdateSessionAsync cascades GUID changes to notes.desktop_guid for FK consistency
- [02-03]: COM notification listener implements IVirtualDesktopNotification with [ComVisible(true)]
- [02-03]: Database update from rename callback is fire-and-forget (Task.Run) to avoid blocking COM thread
- [02-03]: Window title uses Unicode escape \u2014 for em-dash to ensure correct encoding

- [03-01]: UseWindowsForms=true requires `<Using Remove="System.Windows.Forms" />` to avoid WPF Application ambiguity
- [03-01]: ColumnExistsAsync via PRAGMA table_info for idempotent migrations (SQLite lacks IF NOT EXISTS for columns)
- [03-01]: ClampToNearestScreen checks 50x50px region intersection, snaps to nearest screen by Manhattan distance
- [03-02]: Windows destroyed on close (not hidden) — WPF cannot reopen after Close(); fresh instances via IPC
- [03-02]: HandleIpcCommand is async void — acceptable as IPC event handler on Dispatcher
- [03-02]: Desktop switch events logged but no auto-create (user decision honored)
- [03-02]: Welcome note uses VirtualDesktopService.CurrentDesktopGuid instead of hardcoded 'default'

- [04-01]: No INotifyPropertyChanged — project uses code-behind pattern, not MVVM binding
- [04-01]: DateTime.Parse for SQLite datetime strings (converts UTC to local)
- [04-02]: System.Windows.Media fully qualified for Color/Brushes (UseWindowsForms=true causes System.Drawing conflict)
- [04-02]: ListBoxItem template overridden to remove default selection highlight (manual accent border instead)
- [04-02]: Hover effect via MouseEnter/MouseLeave on Border (not ListBoxItem style triggers)
- [04-03]: Rename TextBox embedded in each tab item (hidden by default) rather than creating on-the-fly
- [04-03]: Drag uses raw mouse events (not DragDrop.DoDragDrop) — cleaner for intra-ListBox reorder
- [04-03]: Zone enforcement by checking Pinned state equality between drag source and drop target
- [04-03]: ObservableCollection.Move() for single-notification reorder (no flicker)
- [04-03]: System.Windows.Point fully qualified to avoid System.Drawing ambiguity

- [05-01]: Storyboard.SetTarget uses object reference not element name — avoids WPF name-scope exceptions when animating programmatically
- [05-01]: CommitPendingDeletionAsync nulls _pendingDeletion before Cancel+Dispose to prevent double-dispose races
- [05-01]: UndoDeleteAsync inserts tabs in ascending originalIndex order with Math.Min clamping to handle shifted indexes correctly
- [05-01]: ShowToast content-swap only (no re-animation) when toast already Visible — TOST-04 replace behavior
- [05-01]: FlushAndClose calls CommitPendingDeletionAsync fire-and-forget — process stays alive long enough for DB write to complete
- [Phase 05-02]: PreviewMouseDown used instead of PreviewMouseButtonDown — correct WPF event name for UIElement mouse button tunneling
- [Phase 05-02]: Original outerBorder hover handlers merged with delete icon opacity animation to avoid double-subscription

- [06-01]: UndoStack uses combined index across tier-2 (coarse) and tier-1 (fine) lists — tier-2 occupies lower indices, tier-1 upper
- [06-01]: Memory estimation: string.Length * 2 for .NET UTF-16 strings; 50MB budget hardcoded constant
- [06-01]: Collapse strategy: phase 1 samples every 5th tier-1 entry into tier-2, phase 2 evicts oldest tier-2 entries
- [06-01]: AutosaveService.OnTimerTick is async void — acceptable as DispatcherTimer event handler
- [06-02]: _suppressTextChanged flag guards all programmatic ContentEditor.Text assignments (tab switch, undo, redo)
- [06-02]: OnClosing uses .GetAwaiter().GetResult() for synchronous DB flush — acceptable only in shutdown path
- [06-02]: Scroll offset restored via Dispatcher.BeginInvoke at DispatcherPriority.Loaded for correct layout timing
- [06-02]: Initial content pushed to UndoStack on first tab selection (not on load) via TabList_SelectionChanged
- [06-03]: Clipboard.SetText wrapped in try/catch for ExternalException — clipboard locked by other apps
- [06-03]: SaveFileDialog.InitialDirectory tracked per-session in _lastSaveDirectory field (resets on launch)
- [06-03]: SanitizeFilename replaces Path.GetInvalidFileNameChars() with underscore, trims trailing dots/spaces

- [07-01]: 12 color tokens (not just 10) — added c-toast-bg and c-toast-fg for toast overlay since toast colors differ from main UI in both themes
- [07-01]: SetResourceReference preferred over GetBrush for code-behind color assignments — ensures instant update on theme switch
- [07-01]: ApplyActiveHighlight changed from static to instance method — needed FindResource access for theme-aware accent brush
- [07-01]: CaretBrush added to ContentEditor — ensures cursor is visible in both light and dark themes
- [07-02]: Clone uses glyph \uF413 instead of same glyph as Copy (\uE8C8) — visually distinct in toolbar
- [07-02]: UpdateToolbarState called on tab switch, undo/redo, and pin toggle — not on every text change (too frequent)
- [07-02]: Toolbar copy handler duplicates EDIT-06 behavior (selection or full content) rather than depending on keyboard shortcut path
- [07-02]: ApplicationCommands.Paste used for toolbar paste — focuses editor first to ensure correct paste target

- [08-01]: Custom Popup used for menus (not WPF ContextMenu) — WPF ContextMenu cannot use DynamicResource for background without complex template override
- [08-01]: "Delete older than" submenu shown on MouseEnter (not click) — more discoverable, matches Windows 11 flyout patterns
- [08-01]: HamburgerMenu.Closed event closes DeleteOlderSubmenu — prevents dangling submenu on parent StaysOpen=false dismiss
- [08-01]: Context menu delegates to existing ToolbarPin/Clone/Save handlers by setting _activeTab first — no behavior duplication
- [08-01]: ExitApplication uses App.GetAllWindows() — keeps _windows field private while enabling cross-window access for Exit

### Pending Todos

None yet.

### Blockers/Concerns

- [RESOLVED Phase 2 risk]: Virtual desktop COM GUIDs differ between Windows 11 23H2 and 24H2 — resolved with build-specific GUID dictionary in ComGuids.cs
- [Phase 9 risk]: RegisterHotKey and SetForegroundWindow have STA thread and focus-stealing gotchas — worth a focused research pass before Phase 9 implementation
- [Phase 1 action]: Measure actual startup time against ReadyToRun binary on a clean machine; baseline must be established in Phase 1 before feature work begins

## Session Continuity

Last session: 2026-03-03
Stopped at: Phase 8 Plan 01 complete — hamburger menu + tab context menu executed, verified, SUMMARY created
Resume file: .planning/phases/08-menus-context-actions-orphaned-sessions/08-01-SUMMARY.md
