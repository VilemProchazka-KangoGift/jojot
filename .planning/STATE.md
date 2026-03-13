---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: unknown
last_updated: "2026-03-10T18:04:46.608Z"
progress:
  total_phases: 1
  completed_phases: 1
  total_plans: 3
  completed_plans: 3
---

---
gsd_state_version: 1.0
milestone: v1.2
milestone_name: Find & Replace
status: in-progress
last_updated: "2026-03-10"
progress:
  total_phases: 1
  completed_phases: 0
  total_plans: 3
  completed_plans: 3
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-10)

**Core value:** Instant note capture tied to your virtual desktop context -- switch desktops, switch notes, zero friction.
**Current focus:** Phase 01 - Add in-editor find panel with Ctrl+F (3/3 plans complete, awaiting human verification)

## Current Position

Milestone: v1.2 Find & Replace -- AWAITING VERIFICATION
Phase: 01-add-in-editor-find-panel-with-ctrl-f (3/3 plans complete)
Current plan: 01-03-PLAN.md (checkpoint:human-verify pending)
Last activity: 2026-03-14 - Completed quick task 17: fix timestamps to use local time zone in NoteStore

## Accumulated Context

### Decisions

- Extended FindAllMatches with default params (caseSensitive=false, wholeWord=false) for full backward compatibility
- Word boundary check uses IsLetterOrDigit — underscore is not a word character, matching common editor behavior
- FindReplacePanel raises typed FindChangedEventArgs carrying Query + options for single-event delivery to parent
- Used System.Windows.Media.Brush explicitly to resolve ambiguity in WPF+WinForms project
- Adorner resolves brushes via FindResource on each OnRender (not cached) for always-current theme colors
- Added ThemeService.ThemeChanged event (not just PreferencesPanel hook) to also cover system auto-follow
- Single RunSearch entry point updates matches, adorner, and panel counter in one consistent call

### Pending Todos

None.

### Roadmap Evolution

- Phase 1 added: Add in-editor find panel with Ctrl+F

### Blockers/Concerns

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 2 | do not share notes db between debug and production installed app | 2026-03-10 | 1a61e93 | [2-do-not-share-notes-db-between-debug-and-](./quick/2-do-not-share-notes-db-between-debug-and-/) |
| 3 | fix drag-and-drop reorder visual indicators regression | 2026-03-10 | b4b5497 | [3-the-drag-and-drop-reorder-feature-regres](./quick/3-the-drag-and-drop-reorder-feature-regres/) |
| 4 | drag-reorder fade-in animation | 2026-03-10 | 96c7f8e | [4-during-drag-and-drop-reorder-when-the-ta](./quick/4-during-drag-and-drop-reorder-when-the-ta/) |
| 5 | fix tab title not updating live as user types | 2026-03-10 | 98b526c | [5-fix-tab-title-not-updating-live-as-user-](./quick/5-fix-tab-title-not-updating-live-as-user-/) |
| 6 | strip newlines from auto-generated tab titles | 2026-03-10 | 1482d2d | [6-strip-newlines-from-auto-generated-tab-t](./quick/6-strip-newlines-from-auto-generated-tab-t/) |
| 7 | when pinning a tab it should appear at the top of the pinned group | 2026-03-13 | 51369f9 | [7-when-pinning-a-tab-it-should-appear-on-t](./quick/7-when-pinning-a-tab-it-should-appear-on-t/) |
| 8 | handle OS shutdown/logoff so JoJot does not block Windows session end | 2026-03-13 | ec90b1e | [8-when-shutting-down-windows-os-all-jojot-](./quick/8-when-shutting-down-windows-os-all-jojot-/) |
| 9 | recovery panel should disappear after acting on orphaned sessions | 2026-03-13 | d389c0e | [9-recovery-panel-should-disappear-after-ac](./quick/9-recovery-panel-should-disappear-after-ac/) |
| 11 | capture editor content before undo so redo restores unsaved typing | 2026-03-13 | 2734181 | [11-autosave-editor-state-before-undo-so-red](./quick/11-autosave-editor-state-before-undo-so-red/) |
| 12 | fix global hotkey recording: Win key suppression, same-combo detection, Escape cancel | 2026-03-13 | 3fbaf75 | [12-fix-global-hotkey-record-shortcut-loses-](./quick/12-fix-global-hotkey-record-shortcut-loses-/) |
| 13 | right-align tab close (X) button glyph to match updated timestamp right edge | 2026-03-14 | 28407a3 | [13-right-align-the-tab-x-remove-button-it-s](./quick/13-right-align-the-tab-x-remove-button-it-s/) |
| 14 | fix tab highlight missing on window open and after pin toggle | 2026-03-14 | 3a49f9f | [14-fix-tab-highlight-missing-on-window-open](./quick/14-fix-tab-highlight-missing-on-window-open/) |
| 15 | increase tab title max display length from 30 to 45 characters | 2026-03-14 | 7b5b3b1 | [15-increase-the-max-length-of-the-tab-title](./quick/15-increase-the-max-length-of-the-tab-title/) |
| 16 | add ellipsis to auto-generated tab titles when content is truncated | 2026-03-14 | e3fcdd5 | [16-when-auto-naming-the-tabs-from-the-edito](./quick/16-when-auto-naming-the-tabs-from-the-edito/) |
| 17 | fix timestamps to use local time zone in NoteStore | 2026-03-14 | 1c1d410 | [17-fix-timestamps-to-use-local-time-zone-in](./quick/17-fix-timestamps-to-use-local-time-zone-in/) |

## Session Continuity

Last session: 2026-03-14
Stopped at: Completed quick task 17 — fix timestamps to use local time zone in NoteStore
Resume file: None
