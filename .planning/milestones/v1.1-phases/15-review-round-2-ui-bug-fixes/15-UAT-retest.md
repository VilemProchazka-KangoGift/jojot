---
status: diagnosed
phase: 15-review-round-2-ui-bug-fixes
source: 15-06-SUMMARY.md, 15-07-SUMMARY.md
started: 2026-03-05T10:00:00Z
updated: 2026-03-05T10:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Tab Hover Glyphs and Close Icon
expected: Hover over an unpinned tab: pin icon and close icon appear. Close icon should be a Fluent ChromeClose (X) matching pin icon visual weight. Pin and close icons change color on hover. Hover over a pinned tab's pin icon: it changes to a crossed-out pin glyph (not a red X character).
result: issue
reported: "fail. Design should be: Unpinned normal: title only. Unpinned hover: title | pin icon | delete icon (right-aligned, title shrinks, delete icon bigger, pin+delete change color on hover). Pinned normal: pin icon | title. Pinned hover: pin icon | title | delete icon (hovering pin changes to crossed-out pin like toolbar)."
severity: major

### 2. Drag Ghost Visibility
expected: Click and drag a tab to reorder. A visible semi-transparent ghost of the tab should follow your cursor (not invisible). The ghost should be a recognizable snapshot of the tab.
result: issue
reported: "no ghost appears. the dragged item turns invisible but as soon as i move to another tab it reappears in its original position. turn off the tab hover during drag and drop."
severity: major

### 3. Drop Position Accuracy
expected: While dragging a tab, the drop indicator should correctly predict where the tab will land. Dropping on the indicator should place the tab exactly at the indicated position, even near separator items.
result: pass

### 4. File Drop Over Entire Window
expected: Drag a text file from Explorer over the editor area. A drop overlay should appear covering the whole window (not just toolbar/tab area). Moving the file away from the window should dismiss the overlay reliably.
result: issue
reported: "partial - the indicator still doesn't appear when i initially move into editor area"
severity: major

### 5. Desktop Name in Move Overlay
expected: Trigger the move-to-desktop overlay. It should show "From: Desktop N" (using the actual desktop name or "Desktop N" for un-renamed desktops) instead of "Unknown desktop".
result: issue
reported: "'unknown desktop' error is gone. Still only shows desktop numbers for all cases. New bug: when I get the lock screen and move the window back without making the decision (or to another incorrect desktop) the lock doesn't refresh/disappear, shows info for the first incorrect desktop it was dropped in."
severity: major

### 6. Recover Sessions After Reparent
expected: Create a new virtual desktop, move JoJot window there, select "Keep here". Exit JoJot, remove the new desktop, restart on original desktop. "Recover Sessions" should appear in the menu with the orphaned session.
result: pass
notes: "Enhancement requests: redesign recovery menu to not be card-based; show orphaned desktop name; show more note info (note names + lengths + excerpts for first 5 notes)"

## Summary

total: 6
passed: 2
issues: 4
pending: 0
skipped: 0

## Gaps

- truth: "Unpinned tabs show pin+close on hover with proper layout; pinned tabs show crossed-out pin on hover"
  status: failed
  reason: "User reported: fail. Design should be: Unpinned normal: title only. Unpinned hover: title | pin icon | delete icon (right-aligned, title shrinks, delete icon bigger, pin+delete change color on hover). Pinned normal: pin icon | title. Pinned hover: pin icon | title | delete icon (hovering pin changes to crossed-out pin like toolbar)."
  severity: major
  test: 1
  root_cause: "Two issues: (1) Pinned tabs never get a close/delete button -- if (!tab.Pinned) guard at line 409 skips close button creation entirely. Hover show logic at lines 489-494 checks closeBtn != null which is always false for pinned. (2) Close icon FontSize 10 vs pin icon 12 -- visually too small."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 409: if (!tab.Pinned) guard prevents close button creation for pinned tabs"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 429: FontSize=10 for close icon vs FontSize=12 for pin icon"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines 489-514: hover show/hide logic doesn't handle pinned close button"
  missing:
    - "Create close button for pinned tabs too (restructure if guard at line 409)"
    - "Increase close icon FontSize from 10 to 12 to match pin icon weight"
    - "Add hover show/hide logic for pinned tab close button"
  debug_session: ".planning/debug/tab-pin-close-layout.md"

- truth: "Drag ghost follows cursor as visible semi-transparent snapshot"
  status: failed
  reason: "User reported: no ghost appears. the dragged item turns invisible but as soon as i move to another tab it reappears in its original position. turn off the tab hover during drag and drop."
  severity: major
  test: 2
  root_cause: "Mouse.Capture(TabList) at line 1392 uses default CaptureMode.Element which routes ALL mouse events to TabList only. TabItem_PreviewMouseMove (on ListBoxItem children) never fires after capture, so ghost position never updates. TabItem_PreviewMouseLeftButtonUp never fires either, so drag never completes normally. Additionally, outerBorder.MouseEnter/MouseLeave handlers (lines 477-514) have no _isDragging guard."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 1392: Mouse.Capture(TabList) uses CaptureMode.Element instead of SubTree"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines 477-514: hover handlers have no _isDragging guard"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines 128-143: LostMouseCapture -> CompleteDrag -> Mouse.Capture(null) re-entrancy"
  missing:
    - "Change Mouse.Capture(TabList) to Mouse.Capture(TabList, CaptureMode.SubTree)"
    - "Add if (_isDragging) return; guard in MouseEnter/MouseLeave handlers"
    - "Guard LostMouseCapture against re-entrancy"
  debug_session: ".planning/debug/tab-drag-ghost-still-broken.md"

- truth: "File drop overlay appears over entire window including editor area"
  status: failed
  reason: "User reported: partial - the indicator still doesn't appear when i initially move into editor area"
  severity: major
  test: 4
  root_cause: "AllowDrop=False on ContentEditor TextBox (line 250) causes TextBox internal OLE drag handler to silently consume Win32 drag notification with DROPEFFECT_NONE, preventing WPF routed events from ever entering the event system. PreviewDragEnter on Window never fires over editor. Fix is opposite of what was done: AllowDrop must be True so WPF tunneling events work."
  artifacts:
    - path: "JoJot/MainWindow.xaml"
      issue: "Line 250: AllowDrop=False on ContentEditor blocks WPF drag events entirely"
  missing:
    - "Change AllowDrop=False to AllowDrop=True on ContentEditor TextBox"
    - "PreviewDragEnter/PreviewDragOver with e.Handled=true will prevent TextBox text-drop behavior"
  debug_session: ".planning/debug/file-drop-overlay-editor-area.md"

- truth: "Move overlay shows desktop names and refreshes when window moves again without decision"
  status: failed
  reason: "User reported: 'unknown desktop' error is gone. Still only shows desktop numbers for all cases. New bug: when I get the lock screen and move the window back without making the decision (or to another incorrect desktop) the lock doesn't refresh/disappear, shows info for the first incorrect desktop it was dropped in."
  severity: major
  test: 5
  root_cause: "Three sub-issues: (1) Source name reads stale DB at line 3590 instead of live COM. (2) if (_isDragOverlayActive) return; at line 3568 blocks all re-entry to ShowDragOverlayAsync -- overlay never refreshes on subsequent moves. (3) DragKeepHere_Click at line 3684 passes stale _dragToDesktopName instead of fresh targetInfo.Name from COM."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 3590: source name from DatabaseService instead of live COM"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 3568: unconditional _isDragOverlayActive guard blocks overlay refresh"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line 3684: uses stale _dragToDesktopName instead of targetInfo.Name"
  missing:
    - "Query live COM name for source desktop (like target fallback already does)"
    - "Replace unconditional guard with context-aware logic: auto-dismiss if moved back, update in-place if moved to third desktop"
    - "Use targetInfo.Name in DragKeepHere_Click instead of stale _dragToDesktopName"
  debug_session: ".planning/debug/drag-overlay-name-and-refresh.md"
