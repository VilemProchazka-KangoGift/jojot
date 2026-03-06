---
status: resolved
trigger: "Tab drag opacity issues: flash-and-revert on start, stays faded after drop, hover active during drag"
created: 2026-03-06T00:00:00Z
updated: 2026-03-06T00:00:00Z
---

## Current Focus

hypothesis: Three distinct root causes identified for opacity bugs in the drag-reorder flow
test: Code audit of drag start, drop completion, and hover handler paths
expecting: N/A - diagnosis complete
next_action: Return diagnosis

## Symptoms

expected: (1) On drag start, tab fades to 50% and stays faded. (2) On drop, tab animates back to 100% opacity. (3) Hover effects suppressed during drag.
actual: (1) Tab flashes to 50% then reverts. (2) Tab stays faded after drop. (3) Hover effects still active during drag.
errors: None (visual bugs)
reproduction: Drag any tab to reorder
started: After Phase 15.1-01 replaced DragAdorner with in-place opacity fade

## Eliminated

(none - all three hypotheses confirmed)

## Evidence

- timestamp: 2026-03-06
  checked: Drag start code path (lines 1377-1414 MainWindow.xaml.cs)
  found: |
    _dragItem.Opacity = 0.5 at line 1406 sets the ListBoxItem opacity correctly.
    However, Mouse.Capture(TabList, CaptureMode.SubTree) at line 1410 causes
    LostMouseCapture to fire on the individual ListBoxItem that previously had
    implicit capture from PreviewMouseLeftButtonDown. This triggers the
    TabList.LostMouseCapture handler at line 146, which checks _isDragging (true)
    and calls _dragItem.Opacity = 1.0 at line 154, then calls CompleteDrag().
    CompleteDrag at line 1549 ALSO sets _dragItem.Opacity = 1.0, then ResetDragState
    at line 1634 sets it again. Net effect: opacity 0.5 is immediately reverted to 1.0.
  implication: ROOT CAUSE 1 - LostMouseCapture handler resets opacity and aborts drag

- timestamp: 2026-03-06
  checked: LostMouseCapture handler (lines 146-157)
  found: |
    The guard only checks _isCompletingDrag (line 149). It does NOT distinguish between
    "capture lost because we transferred it to TabList via Mouse.Capture(TabList)" vs
    "capture truly lost externally." When drag starts and Mouse.Capture(TabList) is called,
    the previous implicit capture on the ListBoxItem is released, firing LostMouseCapture
    on TabList (since it bubbles up). The _isDragging flag is already true (set at line 1400),
    so the handler enters the if-block and resets everything.
  implication: The handler needs to distinguish intentional capture transfer from real capture loss

- timestamp: 2026-03-06
  checked: Drop completion animation (lines 1570-1592)
  found: |
    After _tabs.Move(), RebuildTabList() is called at line 1570. This CLEARS ALL ListBoxItems
    and creates NEW ones via CreateTabListItem(). The old _dragItem is now a detached,
    orphaned ListBoxItem. Line 1549 sets _dragItem.Opacity = 1.0 on the OLD (orphaned) item,
    not the new one. The code at lines 1576-1591 correctly finds the NEW item by matching
    Tag == _dragTab, sets its opacity to 0.5, and starts a fade-in animation. However,
    SelectTabByNote(_dragTab) at line 1571 triggers TabList_SelectionChanged, which calls
    ApplyActiveHighlight on the new item. ApplyActiveHighlight (line 701) does NOT touch
    the item-level Opacity property -- it only sets border.Background and button visibility/opacity.

    The fade-in animation itself appears correct: 0.5 -> 1.0 over 150ms with CubicEase,
    then BeginAnimation(null) clears the animation clock. BUT there is a subtle WPF issue:
    the Completed event fires and calls item.BeginAnimation(UIElement.OpacityProperty, null).
    In WPF, BeginAnimation(property, null) removes the animation BUT also removes the
    animated value, reverting to the BASE value. The base value for a freshly created
    ListBoxItem is 1.0, so this should work correctly. The fade-in animation going
    From=0.5 To=1.0 should visually restore the item. If the animation IS running but
    the item appears stuck at 0.5, this suggests the animation never completes -- possibly
    because it's on a different item reference than expected, or the Completed event
    doesn't fire.

    HOWEVER: if ROOT CAUSE 1 is active (LostMouseCapture aborts drag), then the code never
    reaches lines 1570-1592 at all. The drag is cancelled before any move occurs. So the
    "stays faded" symptom may actually be: the drag is aborted (opacity reset to 1.0),
    user tries again, the tab just never moves. Need to verify if user ever sees a
    SUCCESSFUL move that stays faded, or if moves never succeed at all.
  implication: ROOT CAUSE 2 is conditional -- if ROOT CAUSE 1 is fixed, the animation path
    should work. But there IS a secondary risk: the lambda captures `item` variable which
    is the NEW ListBoxItem found in the foreach at line 1578. If RebuildTabList triggers
    another rebuild or selection change that replaces this item, the animation targets
    a stale reference.

- timestamp: 2026-03-06
  checked: Hover handler guards during drag (lines 493-531)
  found: |
    Both outerBorder.MouseEnter (line 496) and outerBorder.MouseLeave (line 516) have
    `if (_isDragging) return;` guards. These guards are correct IF _isDragging stays true
    during the drag. However, because ROOT CAUSE 1 resets _isDragging to false almost
    immediately after it's set to true, the guards become ineffective -- _isDragging is
    false for most of the user's drag gesture, so hover effects fire normally.
  implication: ROOT CAUSE 3 is a downstream consequence of ROOT CAUSE 1

- timestamp: 2026-03-06
  checked: XAML ListBoxItem template (MainWindow.xaml lines 17-30)
  found: |
    ListBoxItem template is bare `<ContentPresenter/>` with no triggers. No IsMouseOver
    trigger on ListBoxItem. No VisualStateManager. The only XAML triggers are on
    ToolbarButtonStyle (IsMouseOver for hover bg, IsEnabled for opacity 0.35), which
    do not affect tab ListBoxItems.
  implication: No XAML-level interference with opacity -- all opacity changes are code-behind

## Resolution

root_cause: |
  PRIMARY: LostMouseCapture handler (line 146-157) fires when Mouse.Capture(TabList, SubTree)
  transfers capture away from the individual ListBoxItem at drag start. The handler sees
  _isDragging=true and immediately resets opacity to 1.0, then calls CompleteDrag(), aborting
  the drag. This causes the "flash and revert" symptom and makes the _isDragging guards on
  hover handlers ineffective (drag lasts only milliseconds).

  SECONDARY (latent, blocked by primary): The drop animation path (lines 1570-1592) calls
  RebuildTabList() which destroys all ListBoxItems. The animation targets the correct NEW
  item (found via Tag match), but the Completed callback's `item` closure may reference
  a stale object if any subsequent rebuild occurs before the 150ms animation completes.

fix:
verification:
files_changed: []
