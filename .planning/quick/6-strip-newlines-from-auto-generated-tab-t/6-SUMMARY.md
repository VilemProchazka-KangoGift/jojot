---
phase: quick-6
plan: "01"
subsystem: models
tags: [display-label, notetab, newlines, content-derived-title]
dependency_graph:
  requires: []
  provides: [newline-free-display-labels]
  affects: [tab-bar-rendering]
tech_stack:
  added: []
  patterns: [string-replace-chain, in-place-space-collapse]
key_files:
  modified:
    - JoJot/Models/NoteTab.cs
    - JoJot.Tests/Models/NoteTabTests.cs
decisions:
  - "Use string.Replace chain (not Regex) for newline stripping — consistent with existing GetCleanupExcerpt pattern in MainWindowViewModel"
  - "Custom Name property is returned as-is; newline stripping only applies to content-derived labels"
metrics:
  duration: "~3 minutes"
  completed: "2026-03-10T13:37:38Z"
  tasks_completed: 1
  files_modified: 2
---

# Phase quick-6 Plan 01: Strip Newlines from Auto-Generated Tab Titles Summary

**One-liner:** NoteTab.DisplayLabel now strips \r\n/\r/\n and collapses consecutive spaces in content-derived titles using string.Replace chaining.

## What Was Built

When a tab has no custom name, `DisplayLabel` falls back to the first 30 characters of `Content`. If the content started with multi-line text (e.g., "Hello\nWorld"), the tab title previously rendered with a literal line break, causing layout issues in the tab bar.

The content-fallback branch in `NoteTab.DisplayLabel` now:
1. Trims the content
2. Replaces `\r\n`, `\r`, and `\n` with a space
3. Collapses runs of multiple spaces into one
4. Truncates to 30 characters of the cleaned text (not the raw text)

The `Name` branch is untouched — custom names set by the user are returned as-is.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Strip newlines from content-derived DisplayLabel | 1482d2d | NoteTab.cs, NoteTabTests.cs |

## Test Results

- All 1039 tests pass (5 new NoteTabTests added)
- New tests cover: `\n` replacement, `\r\n` replacement, multiple-newline collapsing, truncation after cleaning, custom name passthrough

## Deviations from Plan

None — plan executed exactly as written.
