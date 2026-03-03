# Plan 06-01: UndoStack, UndoManager, AutosaveService — Summary

**Status:** Complete
**Duration:** ~4 min
**Commit:** f7dc918

## What was built

Three new service classes implementing the core undo/redo and autosave infrastructure:

1. **UndoStack** (`JoJot/Services/UndoStack.cs`) — Per-tab two-tier undo stack with 50 fine-grained snapshots (tier-1) and 20 coarse checkpoints (tier-2). Seamless pointer traversal across both tiers. Linear stack: typing after undo destroys redo future. PushInitialContent sets the "floor" for database-loaded content.

2. **UndoManager** (`JoJot/Services/UndoManager.cs`) — Singleton managing all UndoStacks. Enforces 50MB global memory budget with LRU collapse strategy (oldest inactive tabs collapsed first, active tab never touched).

3. **AutosaveService** (`JoJot/Services/AutosaveService.cs`) — DispatcherTimer-based reset-on-keystroke debounce with EDIT-03 write frequency cap. Configurable via contentProvider/onSaveCompleted delegates for loose coupling.

## Key decisions

- Used `Lazy<UndoManager>` for thread-safe singleton initialization
- Memory estimation: `string.Length * 2` (sizeof(char) = 2 in .NET UTF-16)
- Collapse strategy: phase 1 collapses tier-1 into tier-2 (sampling every 5th entry), phase 2 evicts oldest tier-2 entries
- AutosaveService pushes undo snapshot on both tick and flush to ensure consistency

## Self-Check: PASSED

- [x] UndoStack supports two-tier storage with seamless traversal
- [x] UndoManager enforces 50MB budget with LRU collapse
- [x] AutosaveService provides debounced save with frequency cap
- [x] All three files compile without C# errors

## Key files

### Created
- `JoJot/Services/UndoStack.cs`
- `JoJot/Services/UndoManager.cs`
- `JoJot/Services/AutosaveService.cs`
