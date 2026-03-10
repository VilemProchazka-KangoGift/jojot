---
phase: quick-5
plan: "01"
subsystem: ui
tags: [tab-title, live-update, binding, content-sync]
dependency_graph:
  requires: []
  provides: [live-tab-title-update]
  affects: [MainWindow.xaml.cs, NoteTab.Content, DisplayLabel]
tech_stack:
  added: []
  patterns: [wpf-binding-chain, setproperty-dependent-notifications]
key_files:
  created: []
  modified:
    - JoJot/Views/MainWindow.xaml.cs
key_decisions:
  - "Sync content in ContentEditor_TextChanged before NotifyTextChanged so model is always current when autosave fires"
metrics:
  duration: "~5 minutes"
  completed: "2026-03-10T13:31:07Z"
  tasks_completed: 1
  tasks_total: 1
  files_modified: 1
---

# Quick-5: Fix Tab Title Not Updating Live As User Types

**One-liner:** Added `_activeTab.Content = ContentEditor.Text` in `ContentEditor_TextChanged` so the XAML `DisplayLabel` binding updates the tab title on every keystroke.

## What Was Done

Added a single line to `ContentEditor_TextChanged` in `JoJot/Views/MainWindow.xaml.cs`:

```csharp
_activeTab.Content = ContentEditor.Text;
```

Placed before `_autosaveService.NotifyTextChanged()` so the model is always current when the autosave timer eventually fires.

## Root Cause

`ContentEditor_TextChanged` fired on every keystroke but only called `_autosaveService.NotifyTextChanged()`. It never wrote back to `NoteTab.Content`. The XAML binding `Text="{Binding DisplayLabel}"` depends on `NoteTab.Content` being current — `DisplayLabel` is a three-tier computed property (custom Name → first 30 chars of Content → "New note"). Without the sync, `NoteTab.Content` stayed stale until tab-switch called `SaveEditorStateToTab`, so the tab title only updated on tab switch or autosave flush.

## Why The Fix Is Safe

- `SetProperty` in `NoteTab.Content` has an equality check — no-op if value unchanged
- `PropertyChanged` for `DisplayLabel` and `IsPlaceholder` is lightweight (no DB, no layout)
- Autosave debounce is unchanged — this line does NOT trigger a save
- `_suppressTextChanged` guard already prevents this line from firing during programmatic text assignment (undo/redo, tab restore, tab switch)

## Task Summary

| Task | Description | Commit | Files Modified |
|------|-------------|--------|----------------|
| 1 | Sync editor text to NoteTab.Content on every keystroke | 98b526c | JoJot/Views/MainWindow.xaml.cs |

## Verification

- `dotnet build JoJot/JoJot.slnx` — succeeded, 0 warnings, 0 errors
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` — 1034 passed, 0 failed

## Deviations from Plan

None — plan executed exactly as written.

## Self-Check: PASSED

- [x] Modified file exists: JoJot/Views/MainWindow.xaml.cs
- [x] Commit 98b526c exists in git log
- [x] Build clean (0 warnings)
- [x] All 1034 tests pass
