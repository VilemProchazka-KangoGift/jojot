---
status: diagnosed
trigger: "Tab drag ghost not working + items dropping at wrong positions"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:01:00Z
---

## Current Focus

hypothesis: Ghost invisible due to VisualBrush capturing opacity-0 element; drop position confusion caused by missing ghost + separator indicator failure
test: Exhaustive code trace of drag-and-drop logic
expecting: Confirmed ghost rendering bug; index arithmetic is correct but indicator has silent failures
next_action: Return diagnosis

## Symptoms

expected: Drag ghost should appear during tab drag; tab should drop at the position indicated by the visual indicator
actual: Ghost is failing; tabs drop at wrong position (different from indicator)
errors: Unknown
reproduction: Drag a tab to reorder
started: After R2-DND-01 implementation (Phase 15-04)

## Eliminated

- hypothesis: Off-by-one error in CalculateCollectionIndex
  evidence: Traced 15+ scenarios including with/without separators, forward/backward drags, edge cases. All produce correct results. The newIndex adjustment (newIndex-- when newIndex > oldIndex) is correct for ObservableCollection.Move semantics.
  timestamp: 2026-03-05

- hypothesis: Mouse capture prevents drag events from firing
  evidence: WPF Mouse.Capture(TabList) still allows hit-testing within TabList's subtree. PreviewMouseMove on child ListBoxItems still fires when mouse is over them. All ListBoxItems share the same handler.
  timestamp: 2026-03-05

- hypothesis: Scroll offset causes coordinate mismatch between mouse position and item position
  evidence: Both e.GetPosition(TabList) and TransformToAncestor(TabList) operate in TabList's coordinate space. The ScrollViewer's TranslateTransform is part of the visual tree and is accounted for by TransformToAncestor.
  timestamp: 2026-03-05

- hypothesis: RebuildTabList during drag invalidates _dragInsertIndex
  evidence: No mechanism would trigger RebuildTabList during an active drag in normal operation. All RebuildTabList callers are user-initiated actions that are blocked during drag.
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05
  checked: DragAdorner constructor and drag start sequence (lines 1361-1382, 3791-3817)
  found: _dragItem.Opacity is set to 0 at line 1369 BEFORE DragAdorner is created at line 1375. DragAdorner uses VisualBrush(dragSource) at line 3801. VisualBrush renders the live visual of the source, which has Opacity=0. Result: ghost adorner renders completely transparent.
  implication: This is the confirmed root cause for the invisible ghost. The ghost IS being created and positioned correctly, but renders nothing because its source visual is invisible.

- timestamp: 2026-03-05
  checked: Indicator rendering when _dragInsertIndex points to separator (lines 1456-1464)
  found: When _dragInsertIndex resolves to the separator's ListBox index (e.g., index 1 in [P1(0), Sep(1), U1(2), U2(3)]), the code at line 1459 checks `targetItem.Content is Border border`. The separator's Content is a Separator control, not a Border, so the cast silently fails. No indicator is drawn, but _dragInsertIndex remains set and the drop still executes.
  implication: In pinned+unpinned scenarios, drops can occur without any visible indicator, making the result appear random/wrong.

- timestamp: 2026-03-05
  checked: Invisible dragged item's effect on edge distance calculation (lines 1407-1442)
  found: The loop does NOT skip the dragged item. The invisible item's top/bottom edges compete in the closest-edge calculation. Combined with the suppression logic (lines 1448-1453), this creates a "dead zone" around the original position where the indicator cannot appear. The user sees a gap (invisible item) and items below it, but cannot get feedback near the gap.
  implication: UX confusion — the invisible item taking space shifts visual perception of where items are, and the dead zone makes it hard to drop near the original position.

- timestamp: 2026-03-05
  checked: Index arithmetic in CompleteDrag (lines 1498-1520) and CalculateCollectionIndex (lines 1528-1537)
  found: CalculateCollectionIndex correctly counts NoteTab items before the target ListBox index. The newIndex-- adjustment for the Move-after-Remove semantic is correct. Verified with 15+ trace scenarios including separator, pinned/unpinned, forward/backward, edge positions.
  implication: The index math itself is sound. The "wrong position" report is likely caused by the combination of invisible ghost (no visual feedback of what's being dragged), separator indicator failure (no indicator but drop executes), and the dead zone around the original position.

## Resolution

root_cause: Two bugs causing the combined symptom. (1) Ghost adorner is invisible because VisualBrush captures the drag source element AFTER its Opacity is set to 0 (line 1369 before line 1375). The VisualBrush renders the live visual which is transparent. (2) When _dragInsertIndex resolves to a separator ListBoxItem, the indicator fails to render (separator Content is not a Border), but the drop still executes — causing drops with no visual indicator in pinned+unpinned scenarios.
fix:
verification:
files_changed: []
