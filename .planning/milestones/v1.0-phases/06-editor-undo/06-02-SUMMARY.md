# Plan 06-02: Autosave wiring, undo/redo, sync flush — Summary

**Status:** Complete
**Duration:** ~5 min
**Commit:** 9c99dc3

## What was built

Wired AutosaveService and UndoManager into MainWindow.xaml.cs:

1. **Autosave debounce** — ContentEditor.TextChanged triggers AutosaveService.NotifyTextChanged() for reset-on-keystroke debounce. `_suppressTextChanged` flag prevents programmatic text assignments (tab switch, undo/redo) from triggering autosave.

2. **Undo/Redo shortcuts** — Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z intercepted in Window_PreviewKeyDown (placed FIRST to prevent WPF native undo). PerformUndo/PerformRedo set _suppressTextChanged and restore content from per-tab UndoStack.

3. **Tab switch undo binding** — TabList_SelectionChanged stops autosave timer, saves scroll offset, flushes content, then binds arriving tab's UndoStack with PushInitialContent if stack is empty.

4. **Synchronous flush on close** — OnClosing uses `.GetAwaiter().GetResult()` for blocking DB write. FlushAndClose stops autosave before closing.

5. **Scroll offset save/restore** — Saved via GetScrollViewer in tab switch; restored via Dispatcher.BeginInvoke at DispatcherPriority.Loaded.

6. **5-minute checkpoint timer** — Creates tier-2 checkpoints during active editing sessions.

7. **Undo stack cleanup** — CommitPendingDeletionAsync removes undo stack on permanent deletion. Soft-deleted tabs retain their undo stack for restoration.

## Key decisions

- [05-01]: Undo stack is NOT removed during soft-delete period — per user decision, undo survives 4-second toast window
- GetScrollViewer uses VisualTreeHelper recursion to find ScrollViewer inside TextBox
- Scroll offset restored at DispatcherPriority.Loaded to ensure layout is complete first

## Self-Check: PASSED

- [x] Ctrl+Z/Ctrl+Y/Ctrl+Shift+Z intercepted before WPF native handling
- [x] Tab switch binds correct per-tab UndoStack
- [x] Synchronous flush on app close
- [x] _suppressTextChanged prevents false triggers
- [x] Scroll offset saved and restored on tab switch
- [x] Compiles without C# errors

## Key files

### Modified
- `JoJot/MainWindow.xaml.cs`
