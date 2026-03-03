---
phase: 05-deletion-toast
plan: 02
subsystem: ui
tags: [wpf, keyboard-shortcut, mouse-events, animation, soft-delete, delete-triggers]

# Dependency graph
requires:
  - phase: 05-deletion-toast
    plan: 01
    provides: DeleteTabAsync, DeleteMultipleAsync, toast engine
provides:
  - Ctrl+W keyboard shortcut routing to DeleteTabAsync
  - Middle-click on tab ListBoxItem routing to DeleteTabAsync
  - Hover x icon (TextBlock) on each tab with AnimateOpacity fade-in/out
  - AnimateOpacity static helper for DoubleAnimation on UIElement.OpacityProperty
affects: [future-delete-triggers, 06-context-menu-delete]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - PreviewKeyDown tunneling for Ctrl+W before ContentEditor consumes Key.W
    - PreviewMouseDown with ChangedButton == Middle for middle-click tab delete
    - AnimateOpacity using DoubleAnimation.BeginAnimation on UIElement.OpacityProperty
    - Delete icon as TextBlock child of tab Grid with Grid.SetRowSpan(2) overlay

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml.cs

key-decisions:
  - "PreviewMouseDown used instead of PreviewMouseButtonDown — correct WPF event name for mouse button tunneling on UIElement"
  - "Original outerBorder MouseEnter/MouseLeave handlers removed and merged into new combined handlers that include both hover background and deleteIcon opacity animation — avoids double-subscription"
  - "AnimateOpacity placed near ShowToast/HideToast methods (toast animation section) for logical grouping"

requirements-completed: [TDEL-01, TDEL-03, TDEL-04]

# Metrics
duration: 6min
completed: 2026-03-02
---

# Phase 5 Plan 02: Delete Triggers Summary

**Three delete triggers (Ctrl+W, middle-click, hover x icon) all routing through DeleteTabAsync for undo-capable soft-delete**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-02T22:27:57Z
- **Completed:** 2026-03-02T22:33:32Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- Ctrl+W in `Window_PreviewKeyDown` calls `DeleteTabAsync(_activeTab)` with `e.Handled = true`; fires before ContentEditor can consume Key.W via PreviewKeyDown tunneling
- Middle-click on any tab `ListBoxItem` via `PreviewMouseDown` (checking `e.ChangedButton == MouseButton.Middle`) calls `DeleteTabAsync(tab)` with `e.Handled = true` to prevent WPF scroll-drag
- `AnimateOpacity` static helper animates `UIElement.OpacityProperty` via `DoubleAnimation.BeginAnimation`
- x icon (`TextBlock`, "\u00D7", 12px) added to each tab in `CreateTabListItem` as an overlay child of the inner `Grid` with `Grid.SetRowSpan(2)`, initially `Opacity=0`
- Icon fades in (0→1, 100ms) on tab hover; fades out (1→0, 100ms) when mouse leaves
- Icon color: muted gray (#888888) default, red (#e74c3c) on icon hover
- Click on x calls `DeleteTabAsync(tab)` with `e.Handled = true` to prevent ListBoxItem selection

## Task Commits

1. **Task 1: Ctrl+W and middle-click** - `2dedc41`
   - Added Ctrl+W block in `Window_PreviewKeyDown`
   - Added `PreviewMouseDown` handler in `CreateTabListItem` for middle-click
2. **Task 2: Hover x icon** - `923bf62`
   - Added `AnimateOpacity` static helper
   - Added deleteIcon `TextBlock` with fade animation, color change, and click-to-delete

## Files Created/Modified

- `JoJot/MainWindow.xaml.cs` — Ctrl+W handler in `Window_PreviewKeyDown`, middle-click handler in `CreateTabListItem`, `AnimateOpacity` helper, x icon `TextBlock` in `CreateTabListItem` with hover animation and color change

## Decisions Made

- **PreviewMouseDown vs PreviewMouseButtonDown:** WPF `ListBoxItem` exposes `PreviewMouseDown` (not `PreviewMouseButtonDown`) for tunneling mouse button events. Used `PreviewMouseDown` with `e.ChangedButton == MouseButton.Middle` check.
- **Merged hover handlers:** Original `outerBorder.MouseEnter`/`MouseLeave` handlers (background only) were removed and replaced by new combined handlers that handle both background and delete icon opacity. This avoids two separate event subscriptions on the same event.
- **AnimateOpacity placement:** Placed in the toast animation section of the file (near `ShowToast`/`HideToast`) for logical grouping with other animation helpers.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed wrong WPF event name for mouse button tunneling**
- **Found during:** Task 1 build verification
- **Issue:** Plan specified `item.PreviewMouseButtonDown` but WPF `ListBoxItem` only exposes `PreviewMouseDown` (a `MouseButtonEventArgs` event) — `PreviewMouseButtonDown` does not exist on `UIElement`
- **Fix:** Changed to `item.PreviewMouseDown` — functionally identical; `MouseButtonEventArgs.ChangedButton` check still works
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Commit:** 2dedc41

**2. [Rule 1 - Bug] Removed original hover handlers before adding combined ones**
- **Found during:** Task 2 implementation
- **Issue:** Plan said to "extend" existing `outerBorder.MouseEnter`/`MouseLeave` handlers, but `deleteIcon` is declared after the grid (which is after the original handlers). Keeping both sets would double-subscribe the background logic.
- **Fix:** Removed original handlers and wrote new combined handlers (background + opacity) in the delete icon section, placed after deleteIcon is in scope.
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Commit:** 923bf62

## Self-Check: PASSED

- `JoJot/MainWindow.xaml.cs` exists and modified: FOUND
- Commit 2dedc41 exists: FOUND
- Commit 923bf62 exists: FOUND
- Build: zero errors, zero warnings
