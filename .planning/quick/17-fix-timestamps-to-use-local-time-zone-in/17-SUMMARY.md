---
phase: quick-17
plan: "01"
subsystem: NoteStore
tags: [bug-fix, timestamps, local-time, sqlite]
dependency_graph:
  requires: []
  provides: [local-time-timestamps-in-notestore]
  affects: [NoteTab display relative time]
tech_stack:
  added: []
  patterns: [Clock.Now for local time, Clock.UtcNow reserved for move-detection]
key_files:
  modified:
    - JoJot/Services/NoteStore.cs
    - JoJot.Tests/Services/NoteStoreTests.cs
    - JoJot.Tests/Services/CoverageBoostTests.cs
decisions:
  - "Use Clock.Now (local time) in NoteStore for all user-facing timestamps; Clock.UtcNow left unchanged in PendingMoveStore (move-detection, not user-facing)"
metrics:
  duration: "5 minutes"
  completed: "2026-03-14"
  tasks_completed: 1
  files_modified: 3
---

# Phase quick-17 Plan 01: Fix Timestamps to Use Local Time Zone Summary

**One-liner:** Replace Clock.UtcNow with Clock.Now in all 4 NoteStore timestamp assignments so relative-time display is correct for non-UTC users.

## What Was Built

NoteStore was writing `DatabaseCore.Clock.UtcNow` (UTC) to the database for `CreatedAt` and `UpdatedAt`, while all in-memory UI code used `DateTime.Now` (local time) and `NoteTab.FormatRelativeTime` compared against `SystemClock.Instance.Now` (local time). For any user outside UTC this caused a visible offset — a UTC+1 user would see a tab they just edited show "1 hour ago" instead of "Just now".

The fix changes all 4 timestamp assignments in NoteStore to use `DatabaseCore.Clock.Now` (local time), making database writes consistent with in-memory updates and display formatting.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 (RED) | Update test assertions to clock.Now | 216d9b2 | JoJot.Tests/Services/NoteStoreTests.cs |
| 1 (GREEN) | Change NoteStore to Clock.Now | 1c1d410 | JoJot/Services/NoteStore.cs, JoJot.Tests/Services/CoverageBoostTests.cs |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CoverageBoostTests.GetNotePreviews_IncludesTimestamps also asserted clock.UtcNow**
- **Found during:** Task 1 (GREEN phase) — full test suite run
- **Issue:** `CoverageBoostTests.GetNotePreviews_IncludesTimestamps` asserted `clock.UtcNow` for `CreatedAt`/`UpdatedAt`, breaking after NoteStore was fixed to use `Clock.Now`
- **Fix:** Updated assertions to `clock.Now` to match the new local-time contract
- **Files modified:** JoJot.Tests/Services/CoverageBoostTests.cs
- **Commit:** 1c1d410

## Verification

- `grep "Clock.UtcNow" JoJot/Services/NoteStore.cs` returns nothing (0 occurrences)
- `grep "Clock.Now" JoJot/Services/NoteStore.cs` returns 4 matches (lines 47, 82, 109, 136)
- All 1086 tests pass

## Self-Check: PASSED
