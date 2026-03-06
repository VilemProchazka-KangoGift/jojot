---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Polish & Stability
status: in_progress
last_updated: "2026-03-06T11:57:34Z"
progress:
  total_phases: 6
  completed_phases: 4
  total_plans: 19
  completed_plans: 18
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-03)

**Core value:** Instant note capture tied to your virtual desktop context -- switch desktops, switch notes, zero friction.
**Current focus:** v1.1 Polish & Stability -- Phase 15.1 in progress (recovery panel, rename, reorder fixes)

## Current Position

Phase: 15.1 of 15.1 (Recovery Panel, Rename & Reorder Fixes)
Plan: 1 of 2 (15.1-01 complete -- rename escape fix + in-place fade drag)
Status: Plan 15.1-01 complete, 15.1-02 pending
Last activity: 2026-03-06 -- Completed 15.1-01 (rename escape fix, drag ghost replaced with in-place fade)

Progress: [█████████░] 95% (18 plans complete across 6 phases; 15.1-02 + Phase 14 remaining)

## Performance Metrics

**Velocity (v1.0 baseline):**
- Total plans completed (v1.0): 31
- Average duration: ~15 min
- Total execution time: ~7.5 hours

**v1.1 plans:** 18 completed (1 in Phase 11, 2 in Phase 12, 2 in Phase 13, 0 in Phase 14, 12 in Phase 15, 1 in Phase 15.1)

*Updated after each plan completion*

## Accumulated Context

### Decisions

All v1.0 decisions logged in PROJECT.md Key Decisions table with outcomes.

Recent decisions affecting v1.1:
- v1.0: Custom Popup for menus (WPF ContextMenu can't use DynamicResource)
- v1.0: Code-behind, not MVVM -- all UI logic in MainWindow.xaml.cs

Phase 11-01 decisions:
- Guard field `_isRebuildingTabList` placed at class level (not local) so it persists across async event handler invocations
- Removed duplicate `SelectTabByNote(tab)` from `TogglePinAsync` -- `RebuildTabList` already calls it internally
- `UpdateTabItemDisplay` SelectedItem assignment moved inside unhook/rehook brackets to prevent async handler firing mid-rename (BUG-03)

Phase 12-01 decisions:
- Used #E3F2FD (light blue tint) for light mode and #1A3A4A (dark teal) for dark mode selected-tab background
- Replaced StackPanel row0 with Grid for column-based adaptive sizing instead of fixed MaxWidth
- Delete icon toggles Visibility to drive Auto column collapse rather than using fixed column widths
- DispatcherTimer delays Visibility.Collapsed until opacity animation completes for smooth transition

Phase 12-02 decisions:
- GridSplitter Width=4 for comfortable drag target while staying subtle
- Width persisted on DragCompleted (not continuously) to minimize DB writes
- CultureInfo.InvariantCulture for width formatting/parsing to avoid locale issues

Phase 13-01 decisions:
- Used existing c-text-primary token for tab label foreground (no new c-tab-text token needed)
- 13pt = 100% baseline for FontSizeToPercent

Phase 13-02 decisions:
- Added DeleteOlderSubmenu.Closed handler as unconditional safety net
- PreviewMouseDown on main Window catches all outside clicks before routing

Phase 15 decisions:
- Tab label and rename box font sizes fixed at 13pt regardless of editor font size
- Empty notes deleted on startup (unpinned only, before loading tabs)
- Content saved to _activeTab.Content before FlushAsync in SelectionChanged
- PauseHotkey/ResumeHotkey methods for recording workflow
- Autosave delay removed entirely (not just hidden)
- 22x22 Border with CornerRadius(3) used as button hit targets for tab pin/close
- DragAdorner uses RenderTargetBitmap snapshot at 0.5 opacity for ghost rendering (replaces live VisualBrush)
- Indicator suppressed at original slot and adjacent positions
- FileDropOverlay moved to root Grid for full-window coverage
- Dropped files insert at first position below pinned tabs
- Recovery panel converted from modal to sidebar (Width=320)
- One-panel-at-a-time: recovery and preferences mutually exclusive
- Open button removed from recovery cards (Adopt + Delete only)
- Live COM via GetAllDesktops for source name lookup in move overlay (replaced stale DB lookup)
- "Keep here" hidden when target desktop already has active window
- Pinned tab hover uses E77A unpin glyph (not multiplication sign), stays in Segoe Fluent Icons font
- Close icon uses E711 ChromeClose at 12pt (upgraded from 10pt per user feedback for bigger delete icon)
- Forward-then-backward scan in UpdateDropIndicator for separator ListBoxItems
- Window Preview (tunneling) drag events for full-window file drop coverage over TextBox
- Enter/leave counter pattern for reliable DragLeave (replaces position-based boundary check)
- UpdateSessionDesktopAsync updates guid + name + index with safety DELETE for UNIQUE constraint
- VirtualDesktopService.GetAllDesktops index lookup for "Desktop N" fallback names
- Close button created for ALL tabs (pinned + unpinned), hidden by default, shown on hover
- Unpinned tab column layout: Col 0=title(Star), Col 1=pin(Auto), Col 2=delete(Auto)
- CaptureMode.SubTree for drag Mouse.Capture so child events still route during capture
- _isCompletingDrag re-entrancy guard prevents LostMouseCapture double-fire from Mouse.Capture(null)
- Context-aware re-entry in ShowDragOverlayAsync: auto-dismiss on return, update for third desktop, no-op for same
- DragKeepHere uses fresh COM targetInfo.Name for window title (not stale _dragToDesktopName)
- AllowDrop=True on ContentEditor TextBox with PreviewDrag handlers to suppress text drops
- MinHeight=22 on row0 Grid prevents vertical jitter when hover icons toggle Visible/Collapsed
- TabList-level PreviewMouseMove fallback handler keeps drag ghost tracking in empty space (idempotent with TabItem handler)
- Registry fallback for desktop names via HKCU VirtualDesktops\Desktops\{GUID}\Name when COM GetName() returns empty
- Move overlay auto-dismisses with HideDragOverlayAsync + DeletePendingMoveAsync when window returns to correct desktop

Phase 15.1-01 decisions:
- Rename Escape check in Window_PreviewKeyDown placed after _isDragOverlayActive but before ConfirmationOverlay
- DragAdorner class and TabList_PreviewMouseMove_DragFallback fully removed (in-place fade replaces ghost)
- Drag start sets _dragItem.Opacity = 0.5 (was 0 + ghost adorner)
- Drop fade-in animation clears binding on completion via BeginAnimation(null)
- _isCompletingDrag re-entrancy guard retained (still needed for Mouse.Capture(null) re-fire)

### Pending Todos

None.

### Roadmap Evolution

- Phase 15 added: Review Round 2 -- UI/UX Bug Fixes & Polish (18 requirements from second manual review)
- Phase 15 gap closure: 15-06 and 15-07 added for UAT test failures (tab hover, drag ghost, file drop, etc.)
- Phase 15 gap closure: 15-08 added for UAT retest failures (tab hover layout, drag ghost visibility)
- Phase 15 gap closure: 15-09 added for UAT retest failures (file drop overlay, move overlay refresh)
- Phase 15 gap closure: 15-10 added for UAT retest2 failures (tab hover height, drag ghost empty-space)
- Phase 15 gap closure: 15-11 added for UAT retest2 failures (desktop name registry fallback, move overlay dismiss)
- Phase 15 completed: All 11 plans (15-01 through 15-11) executed, all requirements resolved
- Phase 15.1 inserted after Phase 15: Recovery panel, tab rename, and reorder fixes (URGENT)

### Blockers/Concerns

- DIST-01 (installer) requires deciding MSI vs MSIX and whether to bundle .NET 10 runtime
- R2-BUG-01 (note persistence) -- RESOLVED: Content saved before FlushAsync in SelectionChanged

## Session Continuity

Last session: 2026-03-06
Stopped at: Completed 15.1-01-PLAN.md (rename escape fix, drag ghost replaced with in-place fade)
Next: 15.1-02-PLAN.md (recovery panel fixes) then Phase 14 (Installer)
