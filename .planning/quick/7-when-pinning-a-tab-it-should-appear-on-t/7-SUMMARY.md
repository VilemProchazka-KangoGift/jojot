---
phase: quick-7
plan: 01
subsystem: tab-management
tags: [pinning, ux, sort-order, tdd]
dependency_graph:
  requires: []
  provides: [pin-to-top behavior]
  affects: [MainWindowViewModel.ReorderAfterPinToggle, MainWindow.Tabs.TogglePinAsync]
tech_stack:
  added: []
  patterns: [optional-parameter-backward-compat, tdd-red-green]
key_files:
  created: []
  modified:
    - JoJot/ViewModels/MainWindowViewModel.cs
    - JoJot/Views/MainWindow.Tabs.cs
    - JoJot.Tests/ViewModels/TabCrudTests.cs
decisions:
  - Used optional NoteTab? justToggled = null parameter for full backward compatibility with zero-arg callers
  - Sorting remaining group members by SortOrder preserves stable relative ordering after a toggle
key_decisions:
  - Optional parameter (not overload) keeps existing callers unchanged with no-arg backward compat
metrics:
  duration: ~8 minutes
  completed: "2026-03-13T08:51:03Z"
  tasks_completed: 1
  tasks_total: 1
  files_modified: 3
---

# Quick Task 7: When Pinning a Tab It Should Appear at the Top Summary

**One-liner:** Added `justToggled` parameter to `ReorderAfterPinToggle` so pinning places the tab at position 0 of its new group via TDD.

## What Was Built

Modified `ReorderAfterPinToggle` in `MainWindowViewModel` to accept an optional `NoteTab? justToggled = null` parameter. When provided, the just-toggled tab is placed at the top of its destination group (pinned → top of pinned; unpinned → top of unpinned). Remaining tabs in each group are sorted by their existing SortOrder, preserving relative ordering. The zero-argument call path is fully backward-compatible (sorts both groups by SortOrder unchanged). The call site in `TogglePinAsync` was updated to pass the tab being toggled.

## Tasks

| # | Name | Commit | Result |
|---|------|--------|--------|
| RED | Add failing tests for pin-to-top behavior | 47a69e4 | 7 new test cases, all compile-fail |
| GREEN | Implement ReorderAfterPinToggle with justToggled | 51369f9 | All 1071 tests pass |

## Test Coverage Added

- Pinning a tab with existing pins places it at position 0
- Pinning when no other pins exist places it at index 0
- Unpinning places tab at top of unpinned group (right after all pinned)
- Existing pinned tabs maintain relative order after a new pin (shift down by 1)
- Existing unpinned tabs maintain relative order after a pin
- Null argument preserves backward-compat sort-by-SortOrder behavior
- SortOrder values are reassigned sequentially after justToggled reorder

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

- `JoJot/ViewModels/MainWindowViewModel.cs` - modified, contains `ReorderAfterPinToggle(NoteTab? justToggled = null)`
- `JoJot/Views/MainWindow.Tabs.cs` - modified, call site passes `tab`
- `JoJot.Tests/ViewModels/TabCrudTests.cs` - modified, 7 new tests
- Commits 47a69e4 and 51369f9 exist in git log
- All 1071 tests pass
