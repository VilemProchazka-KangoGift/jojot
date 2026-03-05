---
status: diagnosed
trigger: "File drop overlay not covering editor area + not dismissing when dragging away"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:00:00Z
---

## Current Focus

hypothesis: Two bugs confirmed — (1) TextBox swallows DragEnter so overlay never shows over editor, (2) DragLeave boundary check uses wrong coordinate space
test: Code reading and WPF behavior analysis
expecting: Confirmed
next_action: Return diagnosis

## Symptoms

expected: Dragging a file from Explorer over any part of the window (including editor) shows a drop overlay; dragging away from the window dismisses it
actual: (1) Overlay only appears over toolbar/tabs/sidebar, NOT over the editor TextBox. (2) Once overlay appears, dragging away from window does not dismiss it.
errors: none
reproduction: Drag any file from Explorer over the JoJot window
started: Current behavior

## Eliminated

(none — both hypotheses confirmed on first analysis)

## Evidence

- timestamp: 2026-03-05
  checked: MainWindow.xaml root Grid (line 58-60)
  found: Root Grid has AllowDrop="True" with DragEnter/DragOver/DragLeave/Drop handlers
  implication: This is the intended full-window drop zone

- timestamp: 2026-03-05
  checked: ContentEditor TextBox (line 246-256)
  found: TextBox has NO explicit AllowDrop setting. WPF TextBox defaults AllowDrop="True" for text drag-drop support. TextBox has its own built-in DragEnter/DragOver/Drop handlers that handle text drops and mark events as Handled.
  implication: TextBox intercepts DragEnter before it can bubble up to the root Grid, so OnFileDragEnter never fires when dragging over the editor area

- timestamp: 2026-03-05
  checked: OnFileDragLeave handler (line 2947-2956)
  found: Uses `e.GetPosition(this)` where `this` is the Window, then checks `pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight`. The Window's ActualWidth/ActualHeight includes the non-client area (title bar, borders), but GetPosition(this) returns coordinates relative to the Window's client area. Additionally, DragLeave fires when moving between child elements within the root Grid (e.g., from toolbar to editor), not just when leaving the window.
  implication: The boundary check is unreliable — coordinates don't align properly with bounds, and child-element transitions generate spurious DragLeave events that may or may not pass the check

- timestamp: 2026-03-05
  checked: FileDropOverlay element (line 329-342)
  found: Has AllowDrop="True" with DragOver and Drop handlers, but NO DragEnter or DragLeave handlers. Has Panel.ZIndex="50".
  implication: Once visible, the overlay itself handles DragOver/Drop correctly. But it sits at ZIndex 50 which is below DragOverlay at ZIndex 200.

## Resolution

root_cause: Two distinct bugs — (1) WPF TextBox has built-in AllowDrop=True and handles DragEnter internally, preventing the event from bubbling to the root Grid's OnFileDragEnter handler. (2) OnFileDragLeave uses an unreliable boundary check that compares client-area-relative coordinates against window dimensions, and also fires on child-element transitions within the window, not just true window exits.
fix: (pending)
verification: (pending)
files_changed: []
