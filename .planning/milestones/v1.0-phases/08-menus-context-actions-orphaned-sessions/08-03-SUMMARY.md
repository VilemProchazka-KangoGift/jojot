---
phase: 08-menus-context-actions-orphaned-sessions
plan: 03
subsystem: ui
tags: [wpf, sqlite, orphan-recovery, badge-notification, flyout-panel, animation]

# Dependency graph
requires:
  - phase: 08-menus-context-actions-orphaned-sessions
    provides: 08-01 hamburger menu with OrphanBadge and MenuRecoverText elements, 08-02 confirmation overlay infrastructure
  - phase: 02-virtual-desktop-integration
    provides: VirtualDesktopService.MatchSessionsAsync with orphan tracking
  - phase: 01-foundation
    provides: DatabaseService with SQLite write lock pattern

provides:
  - VirtualDesktopService.OrphanedSessionGuids populated after session matching
  - VirtualDesktopService.SetOrphanedSessionGuids for recovery action updates
  - DatabaseService.GetOrphanedSessionInfoAsync (desktop name, tab count, last updated)
  - DatabaseService.MigrateTabsAsync (moves notes from orphan to target desktop)
  - DatabaseService.DeleteSessionAndNotesAsync (permanently removes session + notes)
  - Recovery flyout panel (sidebar overlay with slide animation, session cards)
  - Adopt/Open/Delete actions on each session card
  - Badge dot on hamburger button when orphans exist
  - Accent-colored "Recover sessions" menu text when orphans exist
  - App.OpenWindowForOrphanAsync for opening orphan window

affects:
  - 09-hotkey-focus: orphan badge state persists across app restarts via OrphanedSessionGuids
  - 10-crash-recovery: orphan tracking lives in VirtualDesktopService static state

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Static list in VirtualDesktopService tracks orphaned GUIDs after session matching
    - SetResourceReference for dynamic cards created in code-behind (theme-aware without binding)
    - Local function (CreateCardButton) inside CreateRecoveryCard for card button factory
    - MigrateTabsAsync uses base+sort_order formula to append migrated notes after existing unpinned
    - DeleteSessionAndNotesAsync after MigrateTabsAsync is safe — notes already migrated, finds 0

key-files:
  created: []
  modified:
    - JoJot/Services/VirtualDesktopService.cs
    - JoJot/Services/DatabaseService.cs
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs

key-decisions:
  - "OrphanedSessionGuids stored as IReadOnlyList<string> static property — populated by MatchSessionsAsync, consumed by recovery panel"
  - "SetOrphanedSessionGuids method allows recovery panel to update list after each action without re-running MatchSessionsAsync"
  - "MigrateTabsAsync unpins source tabs during migration — moved tabs appear below existing unpinned tabs on target"
  - "DeleteSessionAndNotesAsync safe to call after MigrateTabsAsync — notes already moved, deletes 0 notes + session row"
  - "Recovery panel slides in from left (TranslateTransform X: -180 to 0) as a sibling overlay inside Grid at Column 0"
  - "RefreshAfterOrphanAction always calls LoadTabsAsync — covers Adopt case (new tabs) and general consistency"
  - "Badge and MenuRecoverText color updated by UpdateOrphanBadge() at startup (App.OnAppStartup step 9.1)"

patterns-established:
  - "Orphan recovery flow: MatchSessionsAsync → OrphanedSessionGuids → recovery panel → per-action updates"
  - "Card action buttons use local CreateCardButton factory with isDestructive flag for red styling"
  - "Adopt pattern: MigrateTabsAsync then DeleteSessionAndNotesAsync (safe sequence — notes already moved)"

requirements-completed: [MENU-02, ORPH-01, ORPH-02, ORPH-03, ORPH-04]

# Metrics
duration: 15min
completed: 2026-03-03
---

# Phase 8 Plan 03: Orphaned Session Recovery Summary

**Orphan session detection with badge notification and sidebar flyout recovery panel — VirtualDesktopService exposes orphaned GUIDs, DatabaseService provides query/migrate/delete methods, MainWindow shows animated flyout with Adopt/Open/Delete session cards**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-03T09:30:00Z
- **Completed:** 2026-03-03T09:57:15Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- VirtualDesktopService now populates `OrphanedSessionGuids` after `MatchSessionsAsync` completes — orphan GUIDs are stored as an `IReadOnlyList<string>` and can be updated after recovery actions via `SetOrphanedSessionGuids`
- DatabaseService gained three orphan query methods: `GetOrphanedSessionInfoAsync` (fetches name, tab count, last updated), `MigrateTabsAsync` (moves notes with sort_order continuity, unpins during migration), `DeleteSessionAndNotesAsync` (removes session row and all its notes)
- Recovery flyout panel overlays the sidebar (Column 0) using a Grid wrapper, slides in from the left with 150ms QuadraticEase animation, displays a scrollable list of session cards each showing desktop name, tab count, date, and Adopt/Open/Delete buttons
- Badge dot (7px, accent color) appears on the hamburger button when orphans exist; "Recover sessions" menu item text switches to accent color — both updated at startup and cleared when the last orphan is resolved

## Task Commits

Each task was committed atomically:

1. **Task 1: Add orphan detection to VirtualDesktopService and orphan DB methods to DatabaseService** - `946ab27` (feat)
2. **Task 2: Implement recovery flyout panel and badge in MainWindow, wire startup badge in App** - `3be4583`, `55aa7ca` (feat, docs)

**Plan metadata:** (created in this execution)

## Files Created/Modified
- `JoJot/Services/VirtualDesktopService.cs` - Added `OrphanedSessionGuids` property, `SetOrphanedSessionGuids`, populated orphan list in `MatchSessionsAsync`
- `JoJot/Services/DatabaseService.cs` - Added `GetOrphanedSessionInfoAsync`, `MigrateTabsAsync`, `DeleteSessionAndNotesAsync`
- `JoJot/MainWindow.xaml` - Added recovery flyout `Border` (RecoveryPanel, RecoveryPanelTranslate, RecoverySessionList) as Grid overlay on Column 0
- `JoJot/MainWindow.xaml.cs` - Added `ShowRecoveryPanel`, `HideRecoveryPanel`, `RecoveryClose_Click`, `CreateRecoveryCard`, `RemoveOrphanGuid`, `RefreshAfterOrphanAction`, `UpdateOrphanBadge`, `_recoveryPanelOpen` field
- `JoJot/App.xaml.cs` - Added `OpenWindowForOrphanAsync`, wired `mainWindow.UpdateOrphanBadge()` at startup step 9.1

## Decisions Made
- `OrphanedSessionGuids` is an `IReadOnlyList<string>` static property exposed from `VirtualDesktopService` — populating it in `MatchSessionsAsync` keeps the detection logic co-located with the matching
- `SetOrphanedSessionGuids` method (internal update) allows recovery actions to decrement the list without re-running session matching
- `MigrateTabsAsync` assigns `sort_order = base + sort_order` where base = max existing sort_order + 1 on target, so migrated tabs always appear below all existing unpinned tabs; source tabs are unpinned during migration
- Calling `DeleteSessionAndNotesAsync` after `MigrateTabsAsync` is semantically correct — notes have already been moved so the DELETE finds 0 notes and only removes the session row
- `RefreshAfterOrphanAction` always calls `LoadTabsAsync` to handle both the Adopt case (new tabs visible) and general consistency after any recovery action
- `UpdateOrphanBadge` is `public` so App.xaml.cs can call it at startup (step 9.1) after window creation

## Deviations from Plan

None - plan executed exactly as written. The previous agents (Plans 01 and 02) had pre-implemented much of the Plan 03 infrastructure as forward-looking stubs; Plan 03 execution completed the full implementation as specified.

## Issues Encountered
- Plan 01 and 02 executions had pre-implemented recovery panel XAML stubs and recovery panel code stubs (as dependencies for Plan 03). When Plan 03 executed, all the code matched the plan specification. The code was already fully committed by the time Plan 03 ran; Task 1 DB methods were the primary new additions.

## Next Phase Readiness
- All ORPH-01 through ORPH-04 requirements complete — orphan detection, listing, recovery actions, badge notification all functional
- MENU-02 (Recover sessions menu item) fully wired with badge and flyout panel
- Phase 9 (Hotkey & Focus) can build on top of the stable window management infrastructure established in Phase 8

---
*Phase: 08-menus-context-actions-orphaned-sessions*
*Completed: 2026-03-03*
