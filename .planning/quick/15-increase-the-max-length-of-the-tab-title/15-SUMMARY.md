---
phase: quick-15
plan: "01"
subsystem: models, views, viewmodels, tests
tags: [truncation, display-label, tab-title, toast, filename, tests]
dependency_graph:
  requires: []
  provides: [45-char tab display label, 45-char toast truncation, 45-char filename preview]
  affects: [NoteTab.DisplayLabel, MainWindow.TabDeletion, MainWindowViewModel.GetDefaultFilename]
tech_stack:
  added: []
  patterns: [constant-driven truncation limit]
key_files:
  modified:
    - JoJot/Models/NoteTab.cs
    - JoJot/Views/MainWindow.TabDeletion.cs
    - JoJot/ViewModels/MainWindowViewModel.cs
    - JoJot.Tests/Models/NoteTabTests.cs
    - JoJot.Tests/Models/NoteTabBoundaryTests.cs
    - JoJot.Tests/ViewModels/EditorStateTests.cs
    - JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs
decisions:
  - "Updated NoteTabBoundaryTests.cs (not in original plan) to fix three tests that hardcoded 30/31-char boundaries"
metrics:
  duration: "~8 minutes"
  completed: "2026-03-14"
  tasks_completed: 2
  files_modified: 7
---

# Phase quick-15 Plan 01: Increase Tab Title Max Length Summary

**One-liner:** Increased tab title truncation limit from 30 to 45 characters across DisplayLabel, toast messages, and Save-As filename generation.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Update truncation constant and all hardcoded 30-char limits to 45 | 05bdd09 | NoteTab.cs, MainWindow.TabDeletion.cs, MainWindowViewModel.cs |
| 2 | Update all tests to expect 45-character truncation | 7b5b3b1 | NoteTabTests.cs, NoteTabBoundaryTests.cs, EditorStateTests.cs, ViewModelCoverage2Tests.cs |

## Changes Made

### Source Files

**JoJot/Models/NoteTab.cs**
- `DisplayLabelMaxLength` constant: `30` → `45`
- XML doc comment: "first ~30 chars" → "first ~45 chars"

**JoJot/Views/MainWindow.TabDeletion.cs**
- `UpdateToastContent`: `rawLabel.Length > 30 ? rawLabel[..30]` → `> 45 ? rawLabel[..45]`
- XML doc comment: "Truncates raw label to 30 chars" → "45 chars"

**JoJot/ViewModels/MainWindowViewModel.cs**
- `GetDefaultFilename`: `preview.Length > 30` → `> 45`, `preview[..30]` → `preview[..45]`
- XML doc comment: "first 30 chars" → "first 45 chars"
- `GetCleanupExcerpt` comment: "first 30 chars" → "first 45 chars"

### Test Files

**JoJot.Tests/Models/NoteTabTests.cs**
- Renamed `DisplayLabel_TruncatesAt30Chars_WhenContentIsLong` → `DisplayLabel_TruncatesAt45Chars_WhenContentIsLong`; updated to 45 'A's, asserts HaveLength(45)
- Renamed `DisplayLabel_ReturnsFullContent_WhenContentIs30CharsOrLess` → `WhenContentIs45CharsOrLess`; updated to 45 'B's
- `DisplayLabel_TruncatesAfterNewlineStripping`: updated to 4 groups of 15 chars, asserts length 45 and 45-char prefix

**JoJot.Tests/Models/NoteTabBoundaryTests.cs** (deviation — see below)
- `DisplayLabel_Content30Chars_NoTruncation` → `DisplayLabel_Content45Chars_NoTruncation`; updated to 45 'X's
- `DisplayLabel_Content31Chars_Truncated` → `DisplayLabel_Content46Chars_Truncated`; updated to 46 'Y's, expects 45
- `DisplayLabel_Content29Chars_NoTruncation` → `DisplayLabel_Content44Chars_NoTruncation`; updated to 44 'Z's

**JoJot.Tests/ViewModels/EditorStateTests.cs**
- `GetDefaultFilename_TruncatesLongContent`: expected value updated to `new string('a', 45) + ".txt"`

**JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs**
- `GetDefaultFilename_LongContent_Truncates`: expected value updated to `new string('A', 45) + ".txt"`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated NoteTabBoundaryTests.cs (not in original plan)**
- **Found during:** Task 2 — full test run revealed 3 failing tests in this unlisted file
- **Issue:** `NoteTabBoundaryTests.cs` had three tests hardcoded to 30/31-char boundaries (`DisplayLabel_Content30Chars_NoTruncation`, `DisplayLabel_Content31Chars_Truncated`, `DisplayLabel_Content29Chars_NoTruncation`) that became incorrect after the constant changed to 45
- **Fix:** Updated all three tests to use 44/45/46-char boundaries matching the new limit
- **Files modified:** `JoJot.Tests/Models/NoteTabBoundaryTests.cs`
- **Commit:** 7b5b3b1

## Verification

- `dotnet build JoJot/JoJot.slnx` — passed, 0 warnings, 0 errors
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` — 1075 passed, 0 failed, 0 skipped
- Grep for `> 30`, `[..30]`, `MaxLength = 30` in truncation contexts — zero matches

## Self-Check: PASSED

- `JoJot/Models/NoteTab.cs` — FOUND, DisplayLabelMaxLength = 45
- `JoJot/Views/MainWindow.TabDeletion.cs` — FOUND, rawLabel.Length > 45
- `JoJot/ViewModels/MainWindowViewModel.cs` — FOUND, preview.Length > 45
- Task 1 commit 05bdd09 — FOUND
- Task 2 commit 7b5b3b1 — FOUND
