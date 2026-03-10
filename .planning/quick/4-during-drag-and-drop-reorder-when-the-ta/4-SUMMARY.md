---
phase: quick-4
status: complete
commit: 96c7f8e
---

## Summary

Fixed drag-and-drop reorder fade-in animation that was silently failing.

### Root Cause

After `RebuildTabList()` recreates all ListBoxItems, the visual tree for new items isn't ready for `FindNamedDescendant` lookups or `BeginAnimation` calls — even with `UpdateLayout()` or `Dispatcher.InvokeAsync` at `Loaded` priority.

### Solution

Moved the animation trigger into `TabItemBorder_Loaded` (the existing `Loaded` handler for the OuterBorder in the DataTemplate), which fires when the border is guaranteed to be in the visual tree. The animation itself is deferred via `Dispatcher.BeginInvoke` at `Input` priority so `BeginAnimation` works reliably.

### Changes

| File | Change |
|------|--------|
| `JoJot/Views/MainWindow.xaml.cs` | Added `_fadeInTab` field to signal which tab needs fade-in |
| `JoJot/Views/MainWindow.TabDrag.cs` | `CompleteDrag` sets `_fadeInTab` before `RebuildTabList` instead of inline animation |
| `JoJot/Views/MainWindow.Tabs.cs` | `TabItemBorder_Loaded` checks `_fadeInTab` and starts deferred fade-in (0.5→1.0, 400ms, CubicEase EaseIn) |

### Animation Parameters

- From: 0.5 opacity → To: 1.0 opacity
- Duration: 400ms
- Easing: CubicEase (EaseIn)
- Trigger: `TabItemBorder_Loaded` + `Dispatcher.BeginInvoke` at `Input` priority
