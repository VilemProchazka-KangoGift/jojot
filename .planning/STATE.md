---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Polish & Stability
status: in_progress
last_updated: "2026-03-10T08:31:34Z"
progress:
  total_phases: 7
  completed_phases: 7
  total_plans: 26
  completed_plans: 26
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-03)

**Core value:** Instant note capture tied to your virtual desktop context -- switch desktops, switch notes, zero friction.
**Current focus:** v1.1 Polish & Stability -- Phase 14 (Installer) plan 01 complete, plan 02 remaining

## Current Position

Phase: 14 (Installer) -- IN PROGRESS
Plan: 1 of 2 (14-01 complete -- metadata, icon, and Inno Setup script)
Status: Phase 14 plan 01 complete, plan 02 (build and verify installer) remaining
Last activity: 2026-03-10 -- Completed 14-01 (CalVer metadata, app icon, Inno Setup script)

Progress: [█████████░] 99% (26 plans complete across 8 phases; 14-02 remaining)

## Performance Metrics

**Velocity (v1.0 baseline):**
- Total plans completed (v1.0): 31
- Average duration: ~15 min
- Total execution time: ~7.5 hours

**v1.1 plans:** 26 completed (1 in Phase 11, 2 in Phase 12, 2 in Phase 13, 1 in Phase 14, 12 in Phase 15, 6 in Phase 15.1, 2 in Phase 16)

| Phase | Plan | Duration | Tasks | Files |
|-------|------|----------|-------|-------|
| 16-tab-cleanup-panel | 01 | 7min | 2 | 2 |
| 16-tab-cleanup-panel | 02 | 3min | 1 | 1 |
| 14-installer | 01 | 3min | 2 | 3 |

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

Phase 15.1-02 decisions:
- Existing GetNoteNamesForDesktopAsync preserved alongside new method (may be used elsewhere)
- CreateRecoveryRow returns FrameworkElement (not Border) since flat layout uses StackPanel
- Wrapper StackPanel holds row + divider to return single element; last row skips divider
- DockPanel with LastChildFill=false for Adopt (left) / Delete (right) button alignment
- Tab name shown with normal weight, excerpt as italic em-dash suffix using Inline Runs

Phase 15.1-03 decisions:
- _isTransferringCapture guard mirrors existing _isCompletingDrag pattern for consistency
- Fix is surgical: only three changes (field, handler guard, try/finally wrapper) to one file

Phase 15.1-04 decisions:
- Container horizontal margin zeroed (ScrollViewer already provides 16px)
- Desktop name upgraded from SemiBold 13pt to Bold 14pt for visual hierarchy
- Divider horizontal margin zeroed for full-width alignment within ScrollViewer padding

Phase 15.1-05 decisions:
- Removed local Opacity=0.5 assignment instead of setting Opacity=1.0 in Completed handler (From=0.5 on DoubleAnimation handles initial value without polluting local value store)
- All 6 pinBtn/closeBtn hover handlers guarded with _isDragging check to suppress visual artifacts during drag

Phase 15.1-06 decisions:
- Changed GetDesktopNameFromRegistry from private to internal for cross-class access (minimal visibility increase, same assembly)
- Registry lookup placed before nameBlock creation so existing null-coalescing fallback still applies

Phase 16-01 decisions:
- Used E74D (delete icon) for cleanup menu item instead of E9F5 for consistency
- FontFamily fully qualified as System.Windows.Media.FontFamily to resolve ambiguity with System.Drawing
- Cleanup panel follows identical pattern to RecoveryPanel (Width=320, slide animation, one-panel-at-a-time)
- GetCleanupCandidates filters in-memory _tabs collection, not database

Phase 16-02 decisions:
- UndoManager stacks removed for deleted tabs during cleanup (mirrors CommitPendingDeletionAsync pattern)
- SaveCurrentTabContent + CommitPendingDeletionAsync called before cleanup deletion to ensure data consistency

Phase 14-01 decisions:
- ASCII-safe publisher name (Vilem Prochazka) in .csproj to avoid PE metadata encoding issues
- PNG-compressed ICO entries for all sizes (Vista+ compatible, simpler than BMP)
- Stable AppId GUID {B7E45A2C-8D31-4F6A-9E52-1C3D7A8B9F04} for upgrade detection across versions
- Segoe UI Bold for J lettermark in app icon (Windows system font, always available)

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
- Phase 15.1 original scope completed: All 2 plans (15.1-01 through 15.1-02) executed, verification passed 14/14
- Phase 15.1 gap closure: 15.1-03 and 15.1-04 added for UAT failures (drag opacity, recovery row layout)
- Phase 15.1 gap closure complete: All 4 plans (15.1-01 through 15.1-04) executed
- Phase 15.1 second gap closure: 15.1-05 and 15.1-06 added for UAT retest failures (drag opacity, tab preview indent)
- Phase 15.1 fully complete: All 6 plans (15.1-01 through 15.1-06) executed
- Phase 16 added: Tab Cleanup Panel — replace Delete menu with cleanup side panel (age filter, pinned toggle, preview list, confirmation)
- Phase 16 completed: All 2 plans (16-01 and 16-02) executed -- cleanup panel UI + delete action with confirmation

### Blockers/Concerns

- DIST-01 (installer) -- RESOLVED: Inno Setup chosen, self-contained .NET 10 runtime bundled, script authored in 14-01
- R2-BUG-01 (note persistence) -- RESOLVED: Content saved before FlushAsync in SelectionChanged

## Session Continuity

Last session: 2026-03-10
Stopped at: Completed 14-01-PLAN.md (CalVer metadata, app icon, Inno Setup script)
Next: 14-02-PLAN.md (build installer and human verification of install/launch/uninstall)
