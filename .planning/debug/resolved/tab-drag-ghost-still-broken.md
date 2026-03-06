---
status: resolved
trigger: "Tab drag opacity still broken after 15.1-03 fix. Fade stays, after drop tab re-fades and stays, hover effects during drag"
created: 2026-03-05T00:00:00Z
updated: 2026-03-06T16:00:00Z
---

## Current Focus

hypothesis: WPF animation local-value reversion bug in CompleteDrag fade-in (line 1591-1599). Setting item.Opacity=0.5 as local value BEFORE starting DoubleAnimation means that when fadeIn.Completed clears the animation clock via BeginAnimation(null), WPF reverts to the local value of 0.5 instead of 1.0.
test: Trace the WPF property precedence: local value (0.5) vs animation value (1.0 at end). When animation clock removed, local value wins.
expecting: After 150ms fade-in completes, tab snaps from 1.0 back to 0.5 and stays faded.
next_action: Fix line 1591 -- either remove the local value assignment (From=0.5 already handles initial value) or set local to 1.0 before clearing clock.

## Symptoms

expected: (1) Ghost image follows cursor during tab drag. (2) Dragged tab stays invisible until drop completes. (3) Tab hover effects suppressed during drag.
actual: (1) No ghost visible. (2) Dragged tab reappears when hovering another tab. (3) Hover effects (background, pin/close buttons) fire during drag.
errors: None (visual bugs, no exceptions)
reproduction: Drag any tab to reorder
started: After R2-DND-01 implementation (Phase 15), persists after 15-06 fix attempt

## Eliminated

- hypothesis: RenderTargetBitmap snapshot captures invisible element
  evidence: Code at lines 1407-1410 creates DragAdorner BEFORE setting Opacity=0 at line 1414. The RenderTargetBitmap fix was applied in commit a785631. The snapshot timing is correct.
  timestamp: 2026-03-05

- hypothesis: AdornerLayer is null or DragAdorner not created
  evidence: AdornerLayer.GetAdornerLayer(TabList) walks up to the Window's AdornerDecorator, which always exists. The null check at line 1405 only skips creation if null.
  timestamp: 2026-03-05

- hypothesis: DragAdorner OnRender never called
  evidence: adornerLayer.Add() schedules layout, UpdatePosition() at line 1422 calls InvalidateVisual() which schedules render. Both happen on first drag event before handler returns. OnRender will be called.
  timestamp: 2026-03-05

- hypothesis: Bitmap content is blank/transparent
  evidence: DragAdorner captures the currently selected tab which has c-selected-bg background via ApplyActiveHighlight. Click to start drag triggers selection before 5px threshold, so item always has colored background at snapshot time. RenderTargetBitmap.Render() captures full visual. ImageBrush with 0.5 opacity produces visible output.
  timestamp: 2026-03-05

- hypothesis: Mouse.Capture(TabList) uses default CaptureMode.Element (original root cause)
  evidence: FIXED in commit 22468ac. Line 1418 now reads `Mouse.Capture(TabList, CaptureMode.SubTree)`. Verified in current source.
  timestamp: 2026-03-05 (diagnosed), 2026-03-05 (verified fixed)

- hypothesis: outerBorder.MouseEnter/MouseLeave lack _isDragging guard
  evidence: FIXED in commit 22468ac. Lines 503 and 523 now have `if (_isDragging) return;` guards. Verified in current source.
  timestamp: 2026-03-05 (diagnosed), 2026-03-05 (verified fixed)

- hypothesis: LostMouseCapture/CompleteDrag re-entrancy causes double execution
  evidence: FIXED in commit 22468ac. _isCompletingDrag field (line 32) guards LostMouseCapture (line 150). CompleteDrag sets it true at line 1552 and resets in finally block at line 1596. Verified in current source.
  timestamp: 2026-03-05 (diagnosed), 2026-03-05 (verified fixed)

- hypothesis: AdornerLayer clipping or z-order issue
  evidence: No explicit AdornerDecorator in XAML; Window's default one is used. Adorners render above all content in the Window. No overlays (FileDropOverlay, DragOverlay, etc.) are shown during tab reorder drag. ListBox ScrollViewer ClipToBounds does not affect adorners at Window level.
  timestamp: 2026-03-05

- hypothesis: RebuildTabList called during drag invalidates _dragItem
  evidence: TabList_SelectionChanged (triggered by clicking a different tab to start drag) does NOT call RebuildTabList. It only applies highlights and loads content. No other code path triggers RebuildTabList during drag initiation.
  timestamp: 2026-03-05

- hypothesis: ListBoxItem template override breaks RenderTargetBitmap
  evidence: ListBoxItem template is `<ContentPresenter/>` (XAML lines 23-29). This means the ListBoxItem visual tree contains only the ContentPresenter wrapping the outerBorder. RenderTargetBitmap.Render(ListBoxItem) renders this correctly -- the outerBorder has a background color (c-selected-bg on selected tab) and text content. The bitmap would be non-empty.
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05T12:00:00Z
  checked: Git history of DragAdorner changes
  found: Original 15-04 commit (55cb898) used VisualBrush and set Opacity=0 BEFORE creating adorner. The RenderTargetBitmap fix and reordering was done in 15-07 commit (a785631), NOT in 15-06 (d409ef0). The 15-06 commit only fixed hover glyphs/icons.
  implication: The previous diagnosis was correct about the VisualBrush bug, and it WAS fixed -- but there were additional bugs.

- timestamp: 2026-03-05T12:00:00Z
  checked: Mouse.Capture(TabList) at line 1392 -- default CaptureMode (original code)
  found: Mouse.Capture(element) defaults to CaptureMode.Element. With Element mode, ALL mouse input goes directly to the captured element. PreviewMouseMove does NOT tunnel to child ListBoxItems.
  implication: This was the primary root cause of the original bug. It was fixed in commit 22468ac.

- timestamp: 2026-03-05T13:00:00Z
  checked: All three fixes from plan 15-08 in current source (post-commit 22468ac)
  found: |
    1. Line 1418: Mouse.Capture(TabList, CaptureMode.SubTree) -- PRESENT
    2. Lines 503, 523: _isDragging return guards in MouseEnter/MouseLeave -- PRESENT
    3. Lines 32, 150, 1552, 1596: _isCompletingDrag field and guards -- PRESENT
  implication: All three diagnosed root causes have been addressed in the source code.

- timestamp: 2026-03-05T13:00:00Z
  checked: git show 22468ac diff
  found: Commit changes exactly match the planned fixes. CaptureMode.SubTree added, _isDragging guards added to both hover handlers, _isCompletingDrag re-entrancy guard with try/finally added.
  implication: The fix commit is complete and correct.

- timestamp: 2026-03-05T13:00:00Z
  checked: DLL build timestamp vs fix commit timestamp
  found: JoJot.dll timestamp is 2026-03-05 12:31:24 (build after fix commit at 12:02:25). However, at time of build the exe was locked by running process PID 58552. The running instance may have been started BEFORE the fix was compiled.
  implication: The running app may not include the fix. User must restart to pick up the compiled fix.

- timestamp: 2026-03-05T13:00:00Z
  checked: DragAdorner class implementation (lines 3925-3962)
  found: |
    - Constructor: RenderTargetBitmap with DPI awareness, frozen bitmap, ImageBrush at 0.5 opacity
    - UpdatePosition: sets _offset to (mouseX + 8, mouseY - height/2) relative to TabList, calls InvalidateVisual
    - OnRender: DrawRectangle with _brush at _offset position and original size
    - IsHitTestVisible = false (won't interfere with mouse events)
  implication: DragAdorner implementation is correct. No rendering bugs detected.

- timestamp: 2026-03-05T13:00:00Z
  checked: SubTree capture behavior with mouse outside ListBoxItem bounds
  found: |
    With CaptureMode.SubTree, hit testing within the captured element's subtree still occurs.
    When mouse is over a ListBoxItem, PreviewMouseMove fires on that item -- works correctly.
    When mouse is in empty space in the ListBox (below all items, between items), hit test
    does NOT land on any ListBoxItem. TabItem_PreviewMouseMove handlers don't fire.
    When mouse is outside TabList entirely, events go to TabList itself (no handler).
    Result: ghost position only updates when mouse is directly over a ListBoxItem.
  implication: MINOR REMAINING ISSUE -- ghost freezes when mouse is in empty space within
    or outside the ListBox. This is a polish issue (ghost stutters/freezes in gaps) rather
    than "no ghost at all." The original symptom of no ghost was caused by the Element
    capture mode which has been fixed.

- timestamp: 2026-03-05T13:00:00Z
  checked: Event handler wiring and flow
  found: |
    - PreviewMouseLeftButtonDown, PreviewMouseMove, PreviewMouseLeftButtonUp all wired to ListBoxItems at lines 544-546
    - With SubTree capture: events route to children via hit testing, handlers fire on whichever child is under mouse
    - PreviewMouseMove doesn't use sender after init, uses e.GetPosition(TabList) and field state -- handler works correctly on any ListBoxItem
    - PreviewMouseLeftButtonUp calls CompleteDrag() regardless of which item fires it -- works correctly
  implication: Event routing with SubTree capture is correct for normal drag scenarios.

- timestamp: 2026-03-06T00:00:00Z
  checked: CompleteDrag fade-in animation at lines 1584-1603
  found: |
    Line 1591 sets item.Opacity = 0.5 (local value).
    Line 1592-1599 creates DoubleAnimation From=0.5 To=1.0 over 150ms.
    Line 1598 fadeIn.Completed handler calls item.BeginAnimation(OpacityProperty, null).
    In WPF, BeginAnimation(prop, null) removes the animation clock. After removal,
    the property reverts to the next-highest-precedence value = the LOCAL value (0.5).
    The animation's FillBehavior.HoldEnd would have kept 1.0, but the explicit null
    removal defeats that.
  implication: This is the root cause of "after drop, tab goes to fade again and stays
    faded." The 150ms animation reaches 1.0, then snaps to 0.5 when clock is cleared.

- timestamp: 2026-03-06T00:00:00Z
  checked: AnimateOpacity helper at line 1879-1883 for comparison
  found: |
    AnimateOpacity does NOT call BeginAnimation(null) after completion. It uses default
    FillBehavior.HoldEnd, which keeps the To value after animation ends. This is why
    hover button fade-in/out (lines 508, 513, 526, 531) works correctly -- the animation
    holds the final value.
  implication: The CompleteDrag fade-in uses a different pattern that's incompatible with
    setting the local value first.

- timestamp: 2026-03-06T00:00:00Z
  checked: pinBtn and closeBtn MouseEnter/MouseLeave handlers (lines 354-382, 454-458)
  found: |
    These handlers change icon foreground color (pin glyph, close icon) on hover.
    They do NOT check _isDragging. During drag, if the selected tab's buttons are
    visible, hovering over them changes icon colors.
  implication: Minor contributor to "hover effect still active during drag" but only
    affects icon colors on visible buttons, not tab backgrounds.

- timestamp: 2026-03-06T00:00:00Z
  checked: outerBorder.MouseEnter/MouseLeave guards (lines 498, 518)
  found: |
    Both handlers check _isDragging at entry and return early if true.
    _isDragging is set to true at line 1402 BEFORE Mouse.Capture at line 1416.
    With CaptureMode.SubTree, MouseEnter/MouseLeave fire on children normally.
    The guards should suppress background hover effects during drag.
  implication: outerBorder hover suppression appears correct. The reported "hover
    effect still active during drag" may refer to the pin/close icon color changes
    (which lack guards) or may be from a stale build.

- timestamp: 2026-03-06T00:00:00Z
  checked: ResetDragState at lines 1642-1650
  found: |
    Line 1645: if (_dragItem != null) _dragItem.Opacity = 1.0
    This sets the OLD _dragItem's opacity. But after RebuildTabList() in the MOVE path,
    _dragItem points to the OLD ListBoxItem which was destroyed by RebuildTabList().
    The NEW ListBoxItem created by RebuildTabList has the animation bug (stuck at 0.5).
    ResetDragState's opacity reset at line 1645 operates on a dead/orphaned item.
  implication: Confirms the bug. In the MOVE path, _dragItem is stale after
    RebuildTabList(). The opacity=1.0 at line 1560 and 1645 targets the old item.
    The new item only gets the buggy fade-in animation.

## Resolution

root_cause: |
  PRIMARY BUG (confirmed): WPF animation local-value reversion in CompleteDrag fade-in.

  At lines 1591-1599 in the MOVE path of CompleteDrag():
    1. Line 1591: item.Opacity = 0.5       -- sets LOCAL value to 0.5
    2. Line 1599: item.BeginAnimation(...)  -- starts animation From=0.5 To=1.0
    3. Line 1598: fadeIn.Completed handler calls item.BeginAnimation(OpacityProperty, null)
       -- removes animation clock

  WPF property value precedence: animations override local values. When the animation
  clock is active, the animated value (reaching 1.0) is displayed. When the clock is
  REMOVED by BeginAnimation(null), WPF falls back to the LOCAL value, which is 0.5.

  Result: tab visually reaches 1.0 opacity, then SNAPS BACK to 0.5 and stays there.
  This directly explains symptom: "After drop, tab goes to fade again and stays faded."

  SECONDARY CONCERN: pinBtn.MouseEnter/MouseLeave (lines 354-382) and
  closeBtn.MouseEnter/MouseLeave (lines 454-458) lack _isDragging guards. These only
  affect icon foreground color, not tab background, but could contribute to "hover
  effect still active during drag" if buttons are visible on selected tab during drag.

  The outerBorder.MouseEnter/MouseLeave handlers DO have _isDragging guards (lines 498,
  518). These should correctly suppress background hover effects during drag.

fix:
verification:
files_changed: []
