---
phase: quick-11
plan: 01
subsystem: undo-redo
tags: [bug-fix, undo, redo, editor]
dependency_graph:
  requires: []
  provides: [correct-redo-after-undo]
  affects: [MainWindow.PerformUndo]
tech_stack:
  added: []
  patterns: [push-before-undo]
key_files:
  modified:
    - JoJot/Views/MainWindow.xaml.cs
    - JoJot.Tests/Services/UndoStackTests.cs
key_decisions:
  - Added PushSnapshot call before Undo in PerformUndo to make current content the redo target
metrics:
  duration: "~10 minutes"
  completed: "2026-03-13T12:53:01Z"
  tasks_completed: 1
  files_changed: 2
---

# Phase quick-11 Plan 01: Autosave Editor State Before Undo (Redo Fix) Summary

**One-liner:** Single PushSnapshot call inserted before Undo in PerformUndo fixes Ctrl+Z/Ctrl+Y round-trip when typing occurs between autosave snapshots.

## What Was Built

Fixed the bug where Ctrl+Z then Ctrl+Y did not restore the user's text typed between autosave debounce intervals (500ms).

**Root cause:** `PerformUndo()` called `UndoManager.Instance.Undo()` directly without first pushing the current editor content. If autosave hadn't fired yet for the latest typing, there was no forward entry for redo to return to.

**Fix (one line):** Before calling `Undo()`, call `PushSnapshot(_activeTab.Id, ContentEditor.Text)`. `PushSnapshot` handles deduplication — when content matches the current index it's a no-op, so existing undo/redo behavior is unaffected when the autosave had already captured the state.

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | Add tests + fix PerformUndo to capture content before undo | 2734181 |

## Tests Added

Three new tests in `UndoStackTests` under `// --- Undo captures current content for redo ---`:

1. `PushSnapshotBeforeUndo_EnablesRedoToUnsavedContent` — proves redo returns unsaved typing after push-before-undo
2. `PushSnapshot_IsNoop_WhenContentMatchesCurrentIndex` — proves dedup prevents duplicate entries
3. `PushSnapshotBeforeUndo_FullRoundTrip` — full Ctrl+Z/Ctrl+Z/Ctrl+Y/Ctrl+Y cycle with unsaved content

All 27 UndoStackTests pass. 1069/1069 non-debug-environment tests pass (4 `AppEnvironmentTests` fail only under Release config — pre-existing, unrelated to this change).

## Deviations from Plan

None — plan executed exactly as written. Tests were confirmed to pass via `-c Release` build to work around the running app locking its Debug DLL.

## Self-Check

- [x] `JoJot/Views/MainWindow.xaml.cs` modified — PushSnapshot added before Undo
- [x] `JoJot.Tests/Services/UndoStackTests.cs` modified — 3 new tests added
- [x] Commit 2734181 exists and includes both files
