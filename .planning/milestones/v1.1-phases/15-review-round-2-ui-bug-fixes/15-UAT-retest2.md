---
status: diagnosed
phase: 15-review-round-2-ui-bug-fixes
source: 15-08-SUMMARY.md, 15-09-SUMMARY.md
started: 2026-03-05T12:00:00Z
updated: 2026-03-05T13:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Tab Hover Layout (Unpinned)
expected: Hover over an unpinned tab. Normal state shows title only. On hover: title shrinks, pin icon and delete icon appear to the right. Both icons change color on hover. Delete icon is visually the same weight as pin icon.
result: issue
reported: "pass but when the icons appear, they change the vertical size of the tab (they are higher than the title)"
severity: cosmetic

### 2. Tab Hover Layout (Pinned)
expected: Pinned tab shows pin icon + title normally. On hover: delete icon appears to the right. Hovering over the pin icon changes it to a crossed-out pin glyph (unpin icon, not a red X character). Delete icon also appears on hover.
result: pass

### 3. Drag Ghost Visible
expected: Click and drag a tab to reorder. A visible semi-transparent ghost of the tab follows your cursor. The original tab becomes invisible but preserves its space. Hover effects do NOT fire on other tabs while dragging.
result: issue
reported: "fail. no ghost. hover still there."
severity: major

### 4. File Drop Over Editor
expected: Drag a text file from Explorer over the editor text area. The drop overlay should appear immediately (not only when moving to toolbar/tab area). Moving the file away from the window should dismiss the overlay.
result: pass

### 5. Move Overlay Desktop Names
expected: Trigger the move-to-desktop overlay by moving the window to a different virtual desktop. It should show actual desktop names (or "Desktop N" for un-renamed desktops), not "Unknown desktop".
result: issue
reported: "it shows Desktop N. Everywhere including the window title. All of my desktops are named."
severity: major

### 6. Move Overlay Refresh on Re-move
expected: With the move overlay showing, move the window to yet another desktop (or back to original). The overlay should refresh with new info (or auto-dismiss if moved back to original desktop). It should NOT stay stuck showing the first desktop's info.
result: issue
reported: "fail"
severity: major

## Summary

total: 6
passed: 2
issues: 4
pending: 0
skipped: 0

## Gaps

- truth: "Tab hover icons should not change vertical size of tab"
  status: failed
  reason: "User reported: pass but when the icons appear, they change the vertical size of the tab (they are higher than the title)"
  severity: cosmetic
  test: 1
  root_cause: "row0 Grid in CreateTabListItem() has no MinHeight. Pin/close icon containers are 22x22px Border elements that start Collapsed. Title TextBlock at FontSize 13 is ~17-18px. When icons become Visible on hover, row grows from ~18px to 22px."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "row0 Grid (line ~323) has Height=Auto with no MinHeight, causing vertical growth when 22px icon containers become visible"
  missing:
    - "Add row0.MinHeight = 22 after Grid creation to match icon container height"
  debug_session: ".planning/debug/tab-hover-icon-height.md"

- truth: "Drag ghost follows cursor as visible semi-transparent snapshot; hover suppressed during drag"
  status: failed
  reason: "User reported: fail. no ghost. hover still there."
  severity: major
  test: 3
  root_cause: "All three fixes (CaptureMode.SubTree, _isDragging hover guard, _isCompletingDrag re-entrancy) were correctly applied in commit 22468ac. User likely ran an old binary (build showed exe locked by running process). Need to confirm with fresh build. Minor remaining gap: ghost freezes when mouse is between ListBoxItems because no handler fires in empty space."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 1418: CaptureMode.SubTree correctly applied"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines 503/523: _isDragging hover guards correctly applied"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Ghost only updates on ListBoxItem PreviewMouseMove, not in empty ListBox space"
  missing:
    - "Verify user tests with fresh build (kill old process, clean rebuild)"
    - "Add TabList.PreviewMouseMove fallback handler to update ghost in empty space between items"
  debug_session: ".planning/debug/tab-drag-ghost-still-broken.md"

- truth: "Move overlay and window title show actual desktop names for named desktops"
  status: failed
  reason: "User reported: it shows Desktop N. Everywhere including the window title. All of my desktops are named."
  severity: major
  test: 5
  root_cause: "Windows 11 25H2 (build 26200) changed COM GUIDs. ComGuids._buildMap only has entries for builds 22621 (22H2/23H2) and 26100 (24H2). Build 26200 falls back to 26100 GUIDs but IVirtualDesktop vtable layout shifted, causing GetName() to always return empty string. IVirtualDesktopNotificationService also fails with E_NOINTERFACE (0x80004002)."
  artifacts:
    - path: "JoJot/Interop/ComGuids.cs"
      issue: "Missing build 26200 (25H2) GuidSet entry; falls back to 26100 which has wrong GUIDs"
    - path: "JoJot/Interop/VirtualDesktopInterop.cs"
      issue: "GetName() at lines 129/183 returns empty; catch blocks silently swallow exceptions"
    - path: "JoJot/Interop/ComInterfaces.cs"
      issue: "IVirtualDesktop vtable layout may not match 25H2's actual layout"
  missing:
    - "Add registry-based name fallback: read from HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops\\Desktops\\{GUID}\\Name when GetName() returns empty"
  debug_session: ".planning/debug/desktop-name-com-lookup.md"

- truth: "Move overlay refreshes or auto-dismisses when window is moved again"
  status: failed
  reason: "User reported: fail"
  severity: major
  test: 6
  root_cause: "OnWindowActivated_CheckMisplaced detects when window returns to correct desktop (else if (_isMisplaced) branch) but only clears _isMisplaced and title badge. Does not dismiss the drag overlay or clean up pending_moves. DetectMovedWindow also doesn't fire WindowMovedToDesktop when window returns home (guids match)."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "OnWindowActivated_CheckMisplaced else-if branch (lines ~3902-3911) doesn't call HideDragOverlayAsync()"
    - path: "JoJot/Services/VirtualDesktopService.cs"
      issue: "DetectMovedWindow (line ~525) doesn't fire event when window returns to home desktop"
  missing:
    - "Add HideDragOverlayAsync() and DeletePendingMoveAsync(_desktopGuid) in else-if (_isMisplaced) branch when _isDragOverlayActive is true"
  debug_session: ".planning/debug/drag-overlay-name-and-refresh.md"
