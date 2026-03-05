---
status: diagnosed
phase: 15-review-round-2-ui-bug-fixes
source: 15-01-SUMMARY.md, 15-02-SUMMARY.md, 15-03-SUMMARY.md, 15-04-SUMMARY.md, 15-05-SUMMARY.md
started: 2026-03-05T00:00:00Z
updated: 2026-03-05T01:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Tab Content Preserved on Switch
expected: Open two or more tabs with different content. Switch between them. Content in each tab should be fully preserved -- no data loss or reversion to stale text.
result: pass

### 2. Tab Labels Fixed at 13pt
expected: Change the editor font size (Ctrl+scroll or font menu). Tab labels and rename boxes should stay at a fixed 13pt size regardless of the editor font scaling.
result: pass

### 3. Font Reset Button Shows "100%"
expected: The font size reset button in the toolbar/menu should display "100%" (not "Reset to 13pt" or similar).
result: pass

### 4. Empty Notes Cleaned Up on Startup
expected: Create a new empty tab (no content), close JoJot, reopen it. The empty unpinned tab should be gone -- deleted during startup cleanup.
result: pass

### 5. Recover Sessions Menu Visibility
expected: The "Recover Sessions" menu item should only be visible when orphaned sessions exist (from other desktops). If no orphans, the menu item should be hidden.
result: issue
reported: "I created a new desktop, moved the window there, selected 'move here', exited the program, removed the new desktop, started the program again on the original desktop. There's no 'Recover Sessions' in the menu."
severity: major

### 6. Hotkey Paused During Recording
expected: Open preferences and start recording a new hotkey. While recording, the global Win+Shift+N hotkey should NOT fire. After canceling or closing preferences, the hotkey should work again.
result: pass

### 7. Autosave Delay Preference Removed
expected: Open preferences panel. There should be no "autosave delay" option -- it has been completely removed from the UI.
result: pass

### 8. Tab Pin/Close Buttons on Hover
expected: Hover over an unpinned tab. A pin button (left) and close X button (right) should appear with comfortable 22x22 hit targets. Moving the mouse away should hide them.
result: issue
reported: "Unpinned normal: title only. Unpinned hover: title | pin icon | delete icon -- title shrinks to make room, delete icon should be bigger, pin and delete change color on hover. Pinned normal: pin icon | title. Pinned hover: pin icon | title | delete icon -- hovering pin icon changes it to crossed out pin (same as toolbar)."
severity: major

### 9. Pinned Tab Hover-to-Unpin
expected: Look at a pinned tab -- it should show a pin icon. Hover over the pin icon and it should swap to a red X for unpin. Clicking it should unpin the tab.
result: issue
reported: "See test 8 -- pinned tab hover behavior doesn't match desired design (crossed out pin icon, delete icon on hover)"
severity: major

### 10. Selected Tab Buttons Visible
expected: Click a tab to select it. The pin and close buttons should be visible on the selected tab WITHOUT needing to hover over it.
result: skipped
reason: User doesn't want this behavior -- buttons should only appear on hover per revised design (test 8)

### 11. Drag Ghost Adorner
expected: Click and drag a tab to reorder. A semi-transparent ghost of the tab should follow your cursor. The original tab should become invisible but preserve its space in the list.
result: issue
reported: "fail. plus the ordering is broken, item is falling into wrong position than what the indicator suggests"
severity: major

### 12. Smart Drop Indicators
expected: While dragging a tab, drop indicators should NOT appear at positions that wouldn't actually change the tab's order (i.e., at its original position or immediately adjacent).
result: pass

### 13. Full-Window File Drop
expected: Drag a text file from Explorer and drop it anywhere in the JoJot window (including the sidebar/tab area, not just the editor). A drop overlay should appear, and the file should be imported as the first unpinned tab (not at the end).
result: issue
reported: "Moving the file over the editor doesn't do anything. Once moved over the toolbar, tab bar or preferences sidebar the overlay appears. If I then change my mind and move away, the overlay stays and there's no way to dismiss it."
severity: major

### 14. Recovery Sidebar Slide Animation
expected: Click "Recover Sessions" (when orphans exist). The recovery panel should slide in from the right side as a sidebar (not a modal dialog), similar to the preferences panel animation.
result: skipped
reason: Recovery panel not accessible (blocked by test 5 issue)

### 15. Recovery Cards Show Tab Previews
expected: In the recovery sidebar, each recovery card should show a preview list of tab/note names from that session (up to 5 names, italic, trimmed).
result: skipped
reason: Recovery panel not accessible (blocked by test 5 issue)

### 16. One-Panel-at-a-Time
expected: Open the preferences panel, then trigger recovery sidebar (or vice versa). Opening one panel should automatically close the other -- both should never be open simultaneously.
result: skipped
reason: Recovery panel not accessible (blocked by test 5 issue)

### 17. Source Desktop Name in Move Overlay
expected: When the move-to-desktop overlay appears (e.g., when JoJot detects you changed desktops), it should show "From: {desktop name}" above the title, indicating which desktop the session came from.
result: issue
reported: "It says unknown desktop. Also even in the title it still says Desktop 5 - doesn't use desktop name."
severity: major

### 18. Keep-Here Hidden When Occupied
expected: If the target desktop already has an active JoJot window, the "Keep here" button in the move-to-desktop overlay should be hidden (since that desktop is already served).
result: pass

## Summary

total: 18
passed: 8
issues: 6
pending: 0
skipped: 4

## Gaps

- truth: "Recover Sessions menu item visible when orphaned sessions exist"
  status: failed
  reason: "User reported: I created a new desktop, moved the window there, selected 'move here', exited the program, removed the new desktop, started the program again on the original desktop. There's no 'Recover Sessions' in the menu."
  severity: major
  test: 5
  root_cause: "DragKeepHere_Click calls UpdateSessionDesktopGuidAsync which only updates desktop_guid, leaving stale desktop_name and desktop_index. On restart, Tier 3 index matching silently reassigns the session to a remaining desktop with matching index, consuming it and preventing orphan detection."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "DragKeepHere_Click (line ~3616) does not update session name/index after reparenting"
    - path: "JoJot/Services/DatabaseService.cs"
      issue: "UpdateSessionDesktopGuidAsync (line ~1225) only updates desktop_guid column, not desktop_name or desktop_index"
    - path: "JoJot/Services/VirtualDesktopService.cs"
      issue: "Tier 3 index matching (line ~275) silently consumes sessions with coincidentally matching stale indices"
  missing:
    - "Update desktop_name and desktop_index in UpdateSessionDesktopGuidAsync (or add a new method) when reparenting a session"
  debug_session: ".planning/debug/recover-sessions-not-shown.md"

- truth: "Unpinned tabs show pin+close on hover with proper layout; pinned tabs show crossed-out pin on hover"
  status: failed
  reason: "User reported: Unpinned normal: title only. Unpinned hover: title | pin icon | delete icon -- title shrinks to make room, delete icon should be bigger, pin and delete change color on hover. Pinned normal: pin icon | title. Pinned hover: pin icon | title | delete icon -- hovering pin icon changes it to crossed out pin (same as toolbar)."
  severity: major
  test: 8
  root_cause: "Three issues in CreateTabListItem(): (1) pinned tab hover swaps pin to red X character instead of Segoe Fluent Icons unpin glyph E77A; (2) close icon uses small multiplication sign at FontSize 14; (3) unpinned pin button has no hover color change."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line ~336: pinned hover uses \\u00D7 (Segoe UI) instead of \\uE77A (Segoe Fluent Icons unpin glyph)"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line ~417: close icon \\u00D7 at FontSize 14 is visually small"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines ~350-355: unpinned pin button has no MouseEnter/MouseLeave color handlers"
  missing:
    - "Use \\uE77A Segoe Fluent Icons for pinned hover (not red X)"
    - "Increase close icon size or switch to Segoe Fluent Icons \\uE711"
    - "Add hover color change to unpinned pin button"
  debug_session: ".planning/debug/tab-pin-close-layout.md"

- truth: "Tab drag shows semi-transparent ghost following cursor; item drops at indicated position"
  status: failed
  reason: "User reported: fail. plus the ordering is broken, item is falling into wrong position than what the indicator suggests"
  severity: major
  test: 11
  root_cause: "Two bugs: (1) DragAdorner VisualBrush captures the source element AFTER its opacity is set to 0, rendering an invisible ghost; (2) when _dragInsertIndex points to a separator ListBoxItem, the indicator silently fails (Content is Separator, not Border) but the drop still executes."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line ~1369: _dragItem.Opacity = 0 set BEFORE DragAdorner creation at line ~1375; VisualBrush paints transparent element"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Line ~1459: indicator rendering assumes all ListBoxItems have Border content, fails silently for separator items"
  missing:
    - "Create DragAdorner BEFORE setting opacity to 0, or use RenderTargetBitmap snapshot"
    - "Handle separator ListBoxItems in indicator rendering (fall through to adjacent item)"
  debug_session: ".planning/debug/tab-drag-ghost-drop-position.md"

- truth: "File drop overlay appears over entire window including editor, and dismisses when dragging away"
  status: failed
  reason: "User reported: Moving the file over the editor doesn't do anything. Once moved over the toolbar, tab bar or preferences sidebar the overlay appears. If I then change my mind and move away, the overlay stays and there's no way to dismiss it."
  severity: major
  test: 13
  root_cause: "Two bugs: (1) WPF TextBox has AllowDrop=True by default, intercepting DragEnter before it bubbles to root Grid handler; (2) DragLeave boundary check using e.GetPosition is unreliable because DragLeave fires on internal child-element transitions, not just window exit."
  artifacts:
    - path: "JoJot/MainWindow.xaml"
      issue: "Line ~246: ContentEditor TextBox lacks AllowDrop=False, intercepts drag events"
    - path: "JoJot/MainWindow.xaml"
      issue: "Line ~59: Root Grid drag handlers rely on bubbling which TextBox blocks"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "Lines ~2947-2956: OnFileDragLeave boundary check unreliable for detecting true window exit"
  missing:
    - "Use PreviewDragEnter/PreviewDragOver (tunneling) on root Grid or Window instead of bubbling DragEnter"
    - "Fix DragLeave to reliably detect true window exit (e.g., enter/leave counting or PointToScreen bounds check)"
  debug_session: ".planning/debug/file-drop-overlay-bugs.md"

- truth: "Move overlay shows source desktop name (not 'unknown desktop'); title uses desktop name not 'Desktop N'"
  status: failed
  reason: "User reported: It says unknown desktop. Also even in the title it still says Desktop 5 - doesn't use desktop name."
  severity: major
  test: 17
  root_cause: "COM IVirtualDesktop.GetName() returns empty string for un-renamed desktops (Windows generates 'Desktop N' labels client-side in the shell, not via COM). ShowDragOverlayAsync has no index-based fallback like UpdateDesktopTitle does, so it shows 'Unknown desktop'. Title showing 'Desktop 5' is actually correct behavior for un-renamed desktops."
  artifacts:
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "ShowDragOverlayAsync (line ~3543-3552) uses GetDesktopNameAsync with no fallback for empty names"
    - path: "JoJot/MainWindow.xaml.cs"
      issue: "UpdateDesktopTitle (line ~1893) already has correct 'Desktop {index+1}' fallback pattern that overlay should mirror"
  missing:
    - "Add index-based fallback in ShowDragOverlayAsync: when desktop_name is empty, generate 'Desktop {index+1}'"
  debug_session: ".planning/debug/move-overlay-unknown-desktop.md"
