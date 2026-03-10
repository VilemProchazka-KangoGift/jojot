---
phase: 15-review-round-2-ui-bug-fixes
verified: 2026-03-05T12:15:00Z
status: passed
score: 10/10 must-haves verified
re_verification:
  previous_status: passed
  previous_score: 10/10
  uat_retest_gaps: 4
  gaps_closed:
    - "Unpinned tabs show pin+close on hover with proper layout; pinned tabs show crossed-out pin on hover"
    - "Drag ghost follows cursor as visible semi-transparent snapshot"
    - "File drop overlay appears over entire window including editor area"
    - "Move overlay shows desktop names and refreshes when window moves again without decision"
  gaps_remaining: []
  regressions: []
---

# Phase 15: Review Round 2 -- UI/UX Bug Fixes & Polish Verification Report

**Phase Goal:** Address all issues from the second manual review -- fix the critical note persistence bug, polish tab interactions, improve drag-and-drop visuals, refine preferences, redesign session recovery, and clean up startup and move-to-desktop flows
**Verified:** 2026-03-05T12:15:00Z
**Status:** PASSED
**Re-verification:** Yes -- after UAT retest gap closure (plans 15-08 and 15-09)

## Re-verification Context

The initial verification (2026-03-05T09:26:09Z) passed 10/10 truths. A subsequent UAT retest by the user found 4 issues that were not caught by code inspection:

1. Tab hover layout did not match user's exact specification (unpinned showed icons always, not on hover only)
2. Drag ghost never appeared (CaptureMode.Element prevented child events)
3. File drop overlay did not appear when entering over editor TextBox (AllowDrop=False blocked WPF tunneling)
4. Move overlay showed stale DB names and did not refresh on subsequent desktop moves

Gap closure plans 15-08 and 15-09 were created and executed. This re-verification confirms those gaps are closed with no regressions.

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Switching between tabs preserves all note content -- no text is lost | VERIFIED | `_activeTab.Content = ContentEditor.Text` at line 590 BEFORE `FlushAsync` at line 619 in `TabList_SelectionChanged` (unchanged) |
| 2 | "Recover Sessions" is hidden when no orphaned sessions exist | VERIFIED | `MenuRecover.Visibility = hasOrphans ? Visible : Collapsed` in `UpdateOrphanBadge()` (unchanged) |
| 3 | Font size reset button shows "100%" and tab labels/dates use fixed sizes without inconsistent sizing | VERIFIED | XAML "100%", labelBlock FontSize=13 (line 404), dates FontSize=10 (lines 483, 491) (unchanged) |
| 4 | Autosave delay is removed from preferences; hotkey recording disables the live hotkey | VERIFIED | No autosave delay UI in XAML; `HotkeyService.PauseHotkey()`/`ResumeHotkey()` in recording flow (unchanged) |
| 5 | Unpinned tabs show title-only normally, pin+delete on hover; pinned tabs show pin+title normally, delete on hover | VERIFIED | **[GAP CLOSED by 15-08]** Unpinned: col 0=title(Star), col 1=pin(Auto, hidden), col 2=delete(Auto, hidden) (lines 324-326, 397, 417). Pinned: col 0=pin(Auto, always visible), col 1=title(Star), col 2=delete(Auto, hidden) (lines 324-326, 356-357). Close button created for ALL tabs (line 433, no if-guard). Close icon FontSize=12 (line 452). Hover handlers show correct buttons per tab type (lines 500-538). |
| 6 | Dragging a tab shows a ghost cursor and hides the original; indicator lines only appear at valid new positions | VERIFIED | **[GAP CLOSED by 15-08]** `Mouse.Capture(TabList, CaptureMode.SubTree)` at line 1418. DragAdorner created BEFORE opacity=0 (lines 1403-1414). `_isDragging` guard in hover handlers (lines 503, 523). `_isCompletingDrag` re-entrancy guard (lines 32, 150, 1552, 1596). Indicator suppression at original/adjacent positions (unchanged). |
| 7 | File drop works over the entire window including editor area and places the new tab at the top (below pinned) | VERIFIED | **[GAP CLOSED by 15-09]** `AllowDrop="True"` on ContentEditor (XAML line 250). PreviewDragEnter/PreviewDragOver handlers on ContentEditor suppress non-file drops (lines 129-144). Window-level tunneling events route correctly. Files insert at `pinnedCount` position (unchanged). |
| 8 | Recover Sessions appears as a sidebar with desktop name context | VERIFIED | RecoveryPanel Border with TranslateTransform slide, Width=320, tab previews, Adopt/Delete only (unchanged) |
| 9 | Empty notes are silently cleaned up on startup | VERIFIED | `DeleteEmptyNotesAsync` called in `LoadTabsAsync` before note loading; excludes pinned notes (unchanged) |
| 10 | Move-to-desktop shows live COM source name, refreshes on subsequent moves, and hides "keep here" when target has a window | VERIFIED | **[GAP CLOSED by 15-09]** Source name from `VirtualDesktopService.GetAllDesktops()` (line 3650), zero uses of `DatabaseService.GetDesktopNameAsync` in entire file. Context-aware re-entry (lines 3604-3639): auto-dismiss when moved back, no-op on same target, update in-place for third desktop. `OnWindowActivated_CheckMisplaced` no longer blocks overlay refresh (line 3868 is comment, no guard). `DragKeepHere_Click` uses `targetInfo?.Name` (line 3743). `DragKeepHereBtn.Visibility = Collapsed` when targetHasSession (line 3693). |

**Score:** 10/10 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `JoJot/MainWindow.xaml.cs` | Tab hover layout redesign, drag ghost fix, move overlay re-entry, file drop handlers | VERIFIED | All patterns present and wired; builds with 0 errors |
| `JoJot/MainWindow.xaml` | AllowDrop=True on ContentEditor, window-level drag events | VERIFIED | Line 250: `AllowDrop="True"`, lines 13-15: Window AllowDrop and Preview events |
| `JoJot/Services/DatabaseService.cs` | DeleteEmptyNotesAsync, InsertPendingMoveAsync, DeletePendingMoveAsync | VERIFIED | All methods present (unchanged from initial verification) |
| `JoJot/Services/HotkeyService.cs` | PauseHotkey, ResumeHotkey methods | VERIFIED | Both static methods present (unchanged) |

### Key Link Verification (Gap Closure Focus)

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| ContentEditor TextBox | Window PreviewDragEnter | AllowDrop="True" enables WPF tunneling | WIRED | XAML line 250: `AllowDrop="True"`; Constructor lines 129-144: PreviewDragEnter/Over suppress non-file drops |
| ShowDragOverlayAsync | VirtualDesktopService.GetAllDesktops | Live COM lookup for source name | WIRED | Line 3650: `VirtualDesktopService.GetAllDesktops()`, no `DatabaseService.GetDesktopNameAsync` in file |
| ShowDragOverlayAsync re-entry | Auto-dismiss / update / no-op | Context-aware guard replaces unconditional return | WIRED | Lines 3604-3639: checks toGuid vs _dragFromDesktopGuid (dismiss), _dragToDesktopGuid (no-op), else update |
| OnWindowActivated_CheckMisplaced | ShowDragOverlayAsync | No unconditional guard blocking re-entry | WIRED | Line 3868: comment only, no return statement; calls ShowDragOverlayAsync at line 3900 |
| DragKeepHere_Click | targetInfo?.Name | Fresh COM name for title | WIRED | Line 3740: `VirtualDesktopService.GetAllDesktops()`, line 3743: `targetInfo?.Name ?? _dragToDesktopName` |
| CreateTabListItem hover handlers | _isDragging guard | Hover suppressed during drag | WIRED | Lines 503, 523: `if (_isDragging) return;` in MouseEnter and MouseLeave |
| TabItem_PreviewMouseMove | Mouse.Capture | CaptureMode.SubTree for child event routing | WIRED | Line 1418: `Mouse.Capture(TabList, CaptureMode.SubTree)` |
| CompleteDrag | _isCompletingDrag | Re-entrancy guard for LostMouseCapture | WIRED | Line 1552: `_isCompletingDrag = true`, line 1596: `finally { _isCompletingDrag = false; }`, line 150: `if (_isCompletingDrag) return;` |
| CreateTabListItem | closeBtn for ALL tabs | No if-guard restricting to unpinned only | WIRED | Lines 433-473: closeBtn created unconditionally, lines 516-518/535-537: shown/hidden for all tabs on hover |

### Key Link Verification (Previously Passed -- Regression Check)

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| TabList_SelectionChanged | _activeTab.Content | ContentEditor.Text assignment before FlushAsync | WIRED | Line 590 still present |
| CreateTabListItem() | labelBlock.FontSize | Fixed constant 13 | WIRED | Line 404: `FontSize = 13` |
| LoadTabsAsync() | DeleteEmptyNotesAsync() | Cleanup call before loading notes | WIRED | Still called before note loading |
| UpdateOrphanBadge() | MenuRecover visibility | Collapsed/Visible toggle | WIRED | Still toggles correctly |
| HotkeyRecord_Click | HotkeyService.PauseHotkey() | Unregister before recording | WIRED | Still calls PauseHotkey/ResumeHotkey |
| ShowRecoveryPanel | HidePreferencesPanel | One-panel-at-a-time | WIRED | Still closes other panel first |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| R2-BUG-01 | 15-01 | Note persistence on tab switch | SATISFIED | Content saved before FlushAsync (line 590) |
| R2-FONT-01 | 15-01 | Reset button shows "100%" | SATISFIED | XAML "100%" label |
| R2-FONT-02 | 15-01 | Tab titles fixed size (not scaled) | SATISFIED | FontSize=13 constant (line 404) |
| R2-FONT-03 | 15-01 | Tab dates fixed size (per locked decision) | SATISFIED | FontSize=10 constant (lines 483, 491) |
| R2-FONT-04 | 15-01 | Consistent tab font sizes | SATISFIED | All tabs use same fixed constants |
| R2-STARTUP-01 | 15-01 | Silent empty note cleanup on startup | SATISFIED | DeleteEmptyNotesAsync called before loading |
| R2-MENU-01 | 15-02, 15-07 | Hide "Recover Sessions" when no orphans | SATISFIED | MenuRecover.Visibility toggled |
| R2-PREF-01 | 15-02 | Remove autosave delay preference | SATISFIED | No autosave delay UI |
| R2-PREF-02 | 15-02 | Disable hotkey during recording | SATISFIED | PauseHotkey/ResumeHotkey |
| R2-TAB-01 | 15-03, 15-06, **15-08** | Adequate hit targets, hover layout per spec | SATISFIED | 22x22 Border, unpinned title-only normal, pin+delete on hover; pinned pin+title normal, delete on hover |
| R2-TAB-02 | 15-03, 15-06, **15-08** | Close icon matches pin icon visual weight | SATISFIED | FontSize=12 for close icon (line 452) matching pin icon FontSize=12 (line 345) |
| R2-TAB-03 | 15-03, **15-08** | Unpinned tabs have pin button | SATISFIED | Pin button in col 1 for unpinned, shown on hover (lines 397, 510-514) |
| R2-DND-01 | 15-04, 15-06, **15-08** | Ghost cursor on drag, original invisible | SATISFIED | CaptureMode.SubTree (line 1418), DragAdorner snapshot, hover suppressed |
| R2-DND-02 | 15-04 | No indicator at useless positions | SATISFIED | Suppression at original and adjacent indices (unchanged) |
| R2-DROP-01 | 15-04, 15-07, **15-09** | File drop entire window, first position | SATISFIED | AllowDrop=True on ContentEditor (XAML 250), PreviewDrag handlers (lines 129-144), pinnedCount insertion |
| R2-RECOVER-01 | 15-05 | Recovery sidebar with context | SATISFIED | Border sidebar Width=320, tab previews, Adopt/Delete only |
| R2-MOVE-01 | 15-05, 15-07, **15-09** | Source desktop name in move overlay | SATISFIED | Live COM via GetAllDesktops (line 3650), context-aware re-entry (lines 3604-3639), fresh name in KeepHere (line 3743) |
| R2-MOVE-02 | 15-05 | Hide "keep here" when target occupied | SATISFIED | DragKeepHereBtn.Visibility = Collapsed when targetHasSession (line 3693) |

**All 18 requirements satisfied.**

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| MainWindow.xaml.cs | 676 | CS4014 warning (fire-and-forget async call) | Info | Pre-existing warning, not introduced by plans 15-08/15-09 |

No TODO/FIXME/placeholder markers, no empty implementations, no stub returns found in modified files.

### Build Verification

```
Build succeeded.
    1 Warning(s) [pre-existing CS4014]
    0 Error(s)
```

### Commit Verification

All 4 commits from gap closure plans exist in git history:

| Plan | Commit | Description |
|------|--------|-------------|
| 15-08 Task 1 | `16445e5` | feat: redesign tab hover layout per user specification |
| 15-08 Task 2 | `22468ac` | fix: drag ghost visibility and suppress hover during drag |
| 15-09 Task 1 | `561845b` | fix: enable file drop overlay over editor TextBox |
| 15-09 Task 2 | `2ac034c` | fix: move overlay refresh, live COM names, and auto-dismiss |

### Human Verification Required

### 1. Tab Hover Layout Matches User Spec

**Test:** Hover over an unpinned tab -- verify it shows title only normally, then pin icon + delete icon appear on hover (right-aligned). Hover over a pinned tab -- verify pin icon + title shown normally, delete icon appears on hover. Hover the pinned tab's pin icon -- it should change to crossed-out pin glyph (E77A).
**Expected:** Unpinned: title only at rest, title + pin + delete on hover. Pinned: pin + title at rest, pin + title + delete on hover. Pin icon on pinned tab changes to unpin glyph on hover.
**Why human:** Column layout, icon visibility transitions, and glyph rendering require visual testing.

### 2. Drag Ghost Follows Cursor

**Test:** Click and drag a tab to reorder. Observe the ghost following the cursor.
**Expected:** Semi-transparent tab ghost (RenderTargetBitmap snapshot) follows cursor. Original tab slot is invisible (opacity 0). No hover effects fire on other tabs during drag.
**Why human:** CaptureMode.SubTree behavior and adorner rendering require real interaction.

### 3. File Drop Over Editor TextBox

**Test:** Drag a .txt file from Windows Explorer and enter the window over the editor text area (not the toolbar or tab panel).
**Expected:** File drop overlay appears immediately when entering over the editor area. Dropping the file creates a new tab.
**Why human:** AllowDrop=True interaction with WPF tunneling and TextBox internal OLE handler requires real drag testing.

### 4. Move Overlay Refresh on Subsequent Moves

**Test:** Move the JoJot window to Desktop 2. While the overlay is showing, move the window to Desktop 3. Then move it back to Desktop 1 (original).
**Expected:** First move: overlay shows "From: Desktop 1, Moved to Desktop 2". Second move: overlay updates in-place to show "Moved to Desktop 3". Move back to Desktop 1: overlay auto-dismisses.
**Why human:** Virtual desktop COM API behavior and window activation events require real multi-desktop testing.

### 5. Move Overlay Shows Live COM Names

**Test:** Rename a virtual desktop in Windows Task View (e.g., to "Work"). Move the JoJot window to a different desktop. Check the source name in the overlay.
**Expected:** Overlay shows "From: Work" (the renamed desktop name), not "Desktop 1" or "Unknown desktop".
**Why human:** COM name resolution for renamed desktops requires real Windows virtual desktop interaction.

### Gaps Summary

All 4 UAT retest gaps have been closed:

1. **Tab hover layout** -- Redesigned with conditional column placement. Unpinned tabs show title-only at rest, pin+delete on hover. Pinned tabs show pin+title at rest, delete on hover. Close button created for all tabs. Close icon FontSize increased to 12.
2. **Drag ghost** -- CaptureMode.SubTree enables child event routing so ghost position updates. Hover effects suppressed during drag. CompleteDrag has re-entrancy guard.
3. **File drop over editor** -- AllowDrop=True on ContentEditor allows WPF tunneling events. PreviewDragEnter/PreviewDragOver handlers suppress non-file text drops.
4. **Move overlay** -- Source name from live COM (GetAllDesktops), not stale DB. Context-aware re-entry: auto-dismiss on return to original, update in-place for third desktop, no-op for same. DragKeepHere uses fresh COM name. OnWindowActivated_CheckMisplaced no longer blocks re-entry.

No regressions detected in previously passing truths.

---

_Verified: 2026-03-05T12:15:00Z_
_Verifier: Claude (gsd-verifier)_
