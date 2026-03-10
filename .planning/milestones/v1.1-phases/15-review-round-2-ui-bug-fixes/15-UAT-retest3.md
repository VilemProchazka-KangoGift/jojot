---
status: diagnosed
phase: 15-review-round-2-ui-bug-fixes
source: 15-10-SUMMARY.md, 15-11-SUMMARY.md
started: 2026-03-05T14:00:00Z
updated: 2026-03-05T14:05:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Tab Hover Height Stability
expected: Hover over an unpinned tab. Pin and close icons appear on hover. The tab row should NOT change height when icons appear/disappear -- no vertical jitter or size jump.
result: pass

### 2. Drag Ghost Visible and Tracking
expected: Click and drag a tab to reorder. A visible semi-transparent ghost of the tab follows your cursor. The ghost should track smoothly even when the cursor moves into empty space between tab items. Hover effects should NOT fire on other tabs while dragging.
result: issue
reported: "fail. New approach: remove ghost, fade out the dragged item by 80%, still need to disable the hover backgrounds while dragging"
severity: major

### 3. Desktop Names Show Actual Names
expected: If your virtual desktops have custom names, window titles and move overlay should show those actual names (not generic "Desktop N"). For un-renamed desktops, "Desktop N" is expected.
result: pass

### 4. Move Overlay Auto-Dismiss on Return
expected: Trigger the move overlay by moving the window to a wrong desktop. While the overlay is showing, move the window back to its original desktop. The overlay should auto-dismiss (not stay stuck showing old info).
result: pass

## Summary

total: 4
passed: 3
issues: 1
pending: 0
skipped: 0

## Gaps

- truth: "Tab drag reorder provides clear visual feedback without ghost adorner"
  status: failed
  reason: "User reported: fail. New approach: remove ghost, fade out the dragged item by 80%, still need to disable the hover backgrounds while dragging"
  severity: major
  test: 2
  root_cause: "Design change: DragAdorner approach is unreliable (VisualBrush capture issues, tracking gaps). User wants simpler approach: keep dragged item in-place at 20% opacity instead of invisible+ghost. Hover backgrounds on other tabs still fire during drag because _isDragging guard is missing or incomplete in hover handlers."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "DragAdorner creation, positioning, and cleanup code (~lines 1370-1420) should be removed"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "TabItem drag start sets Opacity=0 (invisible); should be Opacity=0.2 (faded)"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Hover MouseEnter/MouseLeave handlers missing or incomplete _isDragging guard"
  missing:
    - "Remove DragAdorner class and all adorner creation/update/cleanup code"
    - "Change dragged item Opacity from 0 to 0.2 on drag start"
    - "Restore Opacity to 1.0 on drag complete/cancel"
    - "Add _isDragging guard to all tab hover MouseEnter/MouseLeave handlers"
  debug_session: ""
