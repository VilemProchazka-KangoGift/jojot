---
phase: quick-16
plan: 01
subsystem: models
tags: [tab-title, display-label, truncation, tdd]
dependency_graph:
  requires: []
  provides: [ellipsis-on-truncated-tab-titles]
  affects: [JoJot/Models/NoteTab.cs]
tech_stack:
  added: []
  patterns: [tdd-red-green]
key_files:
  modified:
    - JoJot/Models/NoteTab.cs
    - JoJot.Tests/Models/NoteTabTests.cs
    - JoJot.Tests/Models/NoteTabBoundaryTests.cs
decisions:
  - Truncation point stays at 45 content characters; "..." appended after (total display 48 chars max)
  - Custom Name and placeholder "New note" are never affected by ellipsis logic
metrics:
  duration: "~5 minutes"
  completed: "2026-03-13"
  tasks_completed: 2
  files_changed: 3
---

# Phase quick-16 Plan 01: Ellipsis on Truncated Tab Titles Summary

**One-liner:** Append "..." to auto-generated tab titles when note content is truncated at 45 characters.

## What Was Built

When a tab has no custom name and its content exceeds 45 characters, `DisplayLabel` now appends `"..."` to the truncated result. Previously the title silently cut off at 45 chars with no visual cue. Custom names and the "New note" placeholder are unaffected.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 (RED) | Update tests to expect ellipsis on truncated DisplayLabel | 5532534 | NoteTabTests.cs, NoteTabBoundaryTests.cs |
| 2 (GREEN) | Add ellipsis to DisplayLabel when content is truncated | e3fcdd5 | NoteTab.cs |

## Implementation Details

Single-line change in `NoteTab.cs` `DisplayLabel` getter:

```csharp
// Before
return cleaned[..DisplayLabelMaxLength];

// After
return cleaned[..DisplayLabelMaxLength] + "...";
```

`DisplayLabelMaxLength` remains 45 — the truncation point is unchanged, only the suffix is added.

## Tests Updated

- `DisplayLabel_TruncatesAt45Chars_WhenContentIsLong`: now expects length 48, ends with "..."
- `DisplayLabel_TruncatesAfterNewlineStripping`: now expects length 48, ends with "..."
- `DisplayLabel_Content46Chars_Truncated` (boundary test): now expects length 48, ends with "..."

Tests that stay unchanged (no ellipsis expected):
- `DisplayLabel_ReturnsFullContent_WhenContentIs45CharsOrLess`
- `DisplayLabel_Content45Chars_NoTruncation`
- `DisplayLabel_Content44Chars_NoTruncation`
- `DisplayLabel_ReturnsName_WhenNameIsSet`
- `DisplayLabel_ReturnsPlaceholder_*`

## Verification

- 1075 tests pass (0 failures)
- Build: 0 warnings, 0 errors

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

- `JoJot/Models/NoteTab.cs` — modified, contains `"..."`
- `JoJot.Tests/Models/NoteTabTests.cs` — modified
- `JoJot.Tests/Models/NoteTabBoundaryTests.cs` — modified
- Commits 5532534 and e3fcdd5 exist in git log
