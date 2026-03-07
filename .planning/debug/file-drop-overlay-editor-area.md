---
status: diagnosed
trigger: "File drop overlay still doesn't appear when initially dragging a file into the editor area"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:01:00Z
---

## Current Focus

hypothesis: CONFIRMED - AllowDrop=False on TextBox prevents WPF from selecting it as a drop target, but because the TextBox occupies the entire editor region and is opaque to hit-testing, the parent Grid with AllowDrop=True never receives the OLE drag notification either. The result: no drag events fire at all when the cursor is over the editor area.
test: Confirmed via WPF DragDrop.cs source analysis and Microsoft documentation
expecting: N/A - root cause confirmed
next_action: Return diagnosis

## Symptoms

expected: File drop overlay should appear when dragging a file over the editor TextBox area
actual: Overlay appears when entering via toolbar/tab bar but NOT when entering via the editor TextBox
errors: None - silent failure, overlay simply doesn't show
reproduction: Drag a file from Explorer directly into the editor text area
started: After 15-07 fix attempt (AllowDrop=False on TextBox + PreviewDrag on Window)

## Eliminated

- hypothesis: TextBox intercepting DragEnter via bubbling events
  evidence: Window uses PreviewDragEnter (tunneling), which fires before bubbling. Previous fix addressed this.
  timestamp: 2026-03-05 (prior diagnosis)

- hypothesis: AllowDrop=False on TextBox would let drag events pass through to parent elements
  evidence: WPF TextBox registers its own Win32 OLE IDropTarget internally. AllowDrop=False makes it silently consume/reject the OLE notification at the Win32 level, BEFORE the WPF routed event system sees anything. Events never reach the Window's PreviewDragEnter handler.
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05
  checked: MainWindow.xaml lines 1-15 (Window element)
  found: Window has AllowDrop="True", PreviewDragEnter="OnFileDragEnter", PreviewDragOver="OnFileDragOver", PreviewDragLeave="OnFileDragLeave", Drop="OnFileDrop"
  implication: Tunneling handlers are correctly attached at Window level

- timestamp: 2026-03-05
  checked: MainWindow.xaml line 248-250 (ContentEditor TextBox)
  found: TextBox has AllowDrop="False"
  implication: This is the intended fix but may be the actual root cause - AllowDrop=False suppresses ALL drag events including tunneling Preview events

- timestamp: 2026-03-05
  checked: MainWindow.xaml line 62 (root Grid)
  found: Root Grid has AllowDrop="True"
  implication: Root Grid accepts drops, but the TextBox sits inside nested Grids

- timestamp: 2026-03-05
  checked: Visual tree hierarchy
  found: Window > Grid(AllowDrop=True) > Grid(Col2) > Grid(Row1) > TextBox(AllowDrop=False)
  implication: The TextBox with AllowDrop=False is the hit-test target for the editor area

- timestamp: 2026-03-05
  checked: MainWindow.xaml.cs lines 2957-2969 (OnFileDragEnter)
  found: Handler checks e.Data.GetDataPresent(DataFormats.FileDrop), increments counter, shows overlay, sets e.Handled=true
  implication: Handler logic is correct IF it fires

- timestamp: 2026-03-05
  checked: MainWindow.xaml.cs (AllowDrop in code-behind)
  found: No code-behind sets AllowDrop on ContentEditor
  implication: AllowDrop remains False as set in XAML

- timestamp: 2026-03-05
  checked: WPF DragDrop.cs source code and Microsoft documentation on drag-drop event routing
  found: WPF drag-drop events are NOT standard WPF routed events. The OLE subsystem performs a hit-test, finds the innermost element with AllowDrop=True, and ONLY THEN fires tunneling/bubbling events routed to THAT element. If the hit-test target has AllowDrop=False, WPF walks UP the tree to find the nearest ancestor with AllowDrop=True. However, for this to work, the ancestor must be the one that receives the OLE notification.
  implication: The previous fix (AllowDrop=False on TextBox) was based on the wrong mental model - it assumed Preview events always tunnel from Window regardless. They do not for drag-drop.

- timestamp: 2026-03-05
  checked: Jaimer's WPF blog and dotnetframework.org DragDrop.cs source
  found: "WPF will select the innermost WPF control under the mouse that allows drop and will route to it WPF-style drag and drop events" - when AllowDrop=False on the TextBox, WPF should walk up to find Grid(AllowDrop=True) at root level and route events there
  implication: The walk-up SHOULD work since root Grid has AllowDrop=True. But there may be an issue with HOW the TextBox blocks hit-testing - TextBox has its own internal OLE drop target registration that overrides the parent

- timestamp: 2026-03-05
  checked: TextEditorDragDrop.cs in WPF source
  found: WPF TextBox internally registers its own OLE IDropTarget via TextEditorDragDrop, which intercepts the Win32-level drag notifications. When AllowDrop=False, it registers a handler that returns DROPEFFECT_NONE, effectively consuming the OLE notification at the Win32 level before WPF's routed event system ever sees it.
  implication: THIS is the real mechanism. The TextBox's internal OLE registration swallows drag events at the Win32 level. Neither AllowDrop=True on ancestors nor Preview tunneling handlers can override this because the event never enters the WPF routed event system at all.

## Resolution

root_cause: |
  WPF TextBox has internal OLE drag-drop handling (TextEditorDragDrop) that registers the TextBox's HWND
  region as its own OLE IDropTarget at the Win32 level. When AllowDrop="False", the TextBox's internal
  handler responds to the Win32 OLE DragEnter with DROPEFFECT_NONE and does NOT forward it into the WPF
  routed event system. This means:

  1. Setting AllowDrop="False" on the TextBox does NOT make drag events pass through to the Window
  2. Instead, it makes the TextBox SILENTLY CONSUME the OLE drag notification at the Win32 level
  3. PreviewDragEnter on the Window never fires because the event never enters WPF's routed event system
  4. The toolbar/sidebar work because they are NOT TextBox controls and don't have this internal OLE registration

  The previous fix (AllowDrop=False + PreviewDrag on Window) was based on the incorrect assumption that
  WPF drag events follow the same tunneling model as keyboard/mouse events. They do not -- they are gated
  by Win32 OLE RegisterDragDrop, and TextBox has its own registration.

fix_direction: |
  The correct fix must keep AllowDrop="True" on the TextBox (so it participates in the OLE system) but
  intercept the drag events BEFORE the TextBox's built-in handler processes them. Two viable approaches:

  Option A (RECOMMENDED): Keep AllowDrop="True" on TextBox, add PreviewDragEnter/PreviewDragOver/PreviewDragLeave
  directly ON the TextBox (not just on Window). These Preview handlers fire BEFORE the TextBox's internal
  handler. In these handlers, check for FileDrop data and show the overlay + set e.Handled=true to prevent
  the TextBox from doing its own text-drag handling.

  Option B: Use an invisible overlay element (IsHitTestVisible=True, AllowDrop=True) that sits on top of
  the TextBox in the Z-order, catching all drag events. This is more complex and interferes with normal
  text editing mouse events.

  Option A is simpler and more correct. The key change:
  - MainWindow.xaml line 250: Change AllowDrop="False" back to AllowDrop="True" (or remove the attribute
    since True is the TextBox default)
  - Keep the Window-level PreviewDragEnter/PreviewDragOver/PreviewDragLeave handlers as they are
  - The Window Preview handlers will now fire because the OLE system routes events through the TextBox
    (AllowDrop=True), and Preview events tunnel from Window down to TextBox
  - The existing e.Handled=true in OnFileDragEnter prevents the TextBox from doing its own text-drop handling

verification:
files_changed: []
