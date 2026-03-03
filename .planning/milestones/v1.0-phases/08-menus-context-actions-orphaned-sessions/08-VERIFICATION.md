---
phase: 08-menus-context-actions-orphaned-sessions
verified: 2026-03-03T11:30:00Z
status: human_needed
score: 11/11 must-haves verified
human_verification:
  - test: "Open the app and click the hamburger button (left of search box)"
    expected: "Themed popup menu opens with 7 items: Recover sessions, Delete older than (with arrow), Delete all except pinned, Delete all, separator, Preferences, Exit — all with Segoe Fluent Icons glyphs and hover highlights"
    why_human: "Cannot verify visual rendering, icon glyph rendering, or hover highlight behavior programmatically"
  - test: "Hover over 'Delete older than...' in the hamburger menu"
    expected: "Submenu flies out to the right showing 7 days / 14 days / 30 days / 90 days options"
    why_human: "Mouse hover interaction and submenu positioning require runtime testing"
  - test: "Click 'Delete older than 7 days' with tabs older than 7 days present"
    expected: "Confirmation dialog appears (themed card, not MessageBox) with correct count of affected notes, Cancel and Delete buttons; Escape dismisses, Enter confirms; after confirm, toast shows 'N notes deleted' with undo"
    why_human: "Modal dialog appearance, keyboard interaction, and toast timing require human observation"
  - test: "Right-click a tab"
    expected: "Themed context menu popup appears at mouse position with: Rename (F2), Pin/Unpin (Ctrl+P, text reflects current state), Clone to new tab (Ctrl+K), Save as TXT (Ctrl+S), Delete (Ctrl+W), Delete all below — each with Segoe Fluent Icons and hover highlights"
    why_human: "Context menu visual styling, icon rendering, and shortcut labels require visual verification"
  - test: "Right-click a tab and select 'Delete all below' (with tabs below it)"
    expected: "All non-pinned tabs below the right-clicked tab are deleted; pinned tabs below are preserved; toast shows 'N notes deleted'"
    why_human: "Pinned-tab skip behavior and correct tab range require runtime testing with data"
  - test: "With orphaned sessions present (need DB setup), click hamburger menu"
    expected: "Badge dot (7px, accent color) visible on hamburger button; 'Recover sessions' text is accent-colored"
    why_human: "Badge and color change require orphaned sessions to exist in DB — cannot create orphans programmatically in verification"
  - test: "Click 'Recover sessions' with orphaned sessions present"
    expected: "Recovery flyout slides in from the left (150ms ease-out animation), showing session cards each with desktop name (or 'Unknown desktop'), tab count, last updated date, and Adopt/Open/Delete buttons"
    why_human: "Animation, card layout, and data display require runtime orphan data to verify"
  - test: "Click 'Adopt' on a recovery card"
    expected: "Tabs migrate to current desktop at the bottom of the unpinned zone; the card disappears; badge dot disappears if that was the last orphan; tab list reloads showing the adopted tabs"
    why_human: "Migration correctness and UI refresh require end-to-end runtime testing"
  - test: "Click Exit from the hamburger menu with the app open"
    expected: "App closes immediately, flushing all content across all windows"
    why_human: "Exit behavior with multi-window state requires runtime verification"
---

# Phase 8: Menus, Context Actions, Orphaned Sessions — Verification Report

**Phase Goal:** Hamburger menu, tab context menus, bulk note management, orphaned session recovery
**Verified:** 2026-03-03T11:30:00Z
**Status:** human_needed — All automated checks PASSED (11/11 must-haves). Human testing required for visual/interactive behavior.
**Re-verification:** No — initial verification.

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                         | Status     | Evidence                                                                                      |
|----|-----------------------------------------------------------------------------------------------|------------|-----------------------------------------------------------------------------------------------|
| 1  | Hamburger button (E700 glyph) appears left of search box in sidebar header                   | VERIFIED   | `MainWindow.xaml` lines 75-90: `x:Name="HamburgerButton"`, glyph `&#xE700;`, `Grid.Column="0"` |
| 2  | Hamburger popup has 7 items with separators matching spec order                               | VERIFIED   | XAML lines 302-454: Recover, separator, Delete older, Delete except pinned, Delete all, separator, Preferences, Exit |
| 3  | Exit calls FlushAndClose on all windows and Environment.Exit(0)                               | VERIFIED   | `MenuExit_Click` → `ExitApplication(app)` → `app.GetAllWindows()` → `window.FlushAndClose()` → `Environment.Exit(0)` |
| 4  | Right-clicking a tab shows context menu with correct items                                    | VERIFIED   | `CreateTabListItem` wires `MouseRightButtonUp` → `BuildTabContextMenu` returning 7-item popup |
| 5  | Pin/Unpin context item text dynamically reflects tab state                                    | VERIFIED   | `BuildTabContextMenu` line: `string pinText = tab.Pinned ? "Unpin" : "Pin"` (line ~490)       |
| 6  | Delete all below uses DeleteMultipleAsync with filtered tab list                              | VERIFIED   | Line 2362-2368: `_tabs.Skip(tabIndex + 1)` → `DeleteMultipleAsync(belowTabs)`                |
| 7  | Bulk delete confirmation is a custom modal overlay (not MessageBox)                          | VERIFIED   | `ConfirmationOverlay` Grid at Panel.ZIndex=100 in XAML; `ShowConfirmation()` sets Title/Message and shows overlay |
| 8  | Escape/Enter/backdrop-click handle confirmation dialog                                        | VERIFIED   | `Window_PreviewKeyDown` lines 628-646; `ConfirmOverlayBackdrop_Click`; `ConfirmCancel_Click`  |
| 9  | VirtualDesktopService.OrphanedSessionGuids populated after MatchSessionsAsync                | VERIFIED   | `VirtualDesktopService.cs` lines 288-294: orphanedGuids computed and assigned to `OrphanedSessionGuids` |
| 10 | Recovery flyout panel XAML with session list and close button exists                         | VERIFIED   | `MainWindow.xaml` lines 125-153: `RecoveryPanel` Border with `RecoverySessionList` StackPanel and `RecoveryClose_Click` |
| 11 | Badge dot appears/disappears based on orphan count; wired at startup                         | VERIFIED   | `UpdateOrphanBadge()` public method; `App.xaml.cs` line 143 calls it after window creation    |

**Score:** 11/11 truths verified (automated)

### Required Artifacts

| Artifact                                      | Provides                                              | Status    | Details                                                             |
|-----------------------------------------------|-------------------------------------------------------|-----------|---------------------------------------------------------------------|
| `JoJot/MainWindow.xaml`                       | Hamburger button, popup, context menu XAML, recovery flyout, confirmation overlay | VERIFIED | 497 lines; all named elements present and wired |
| `JoJot/MainWindow.xaml.cs`                    | All menu/context/orphan handlers                      | VERIFIED  | Build 0 errors; 2230+ lines; all Phase 8 methods present           |
| `JoJot/Services/DatabaseService.cs`           | GetOrphanedSessionInfoAsync, MigrateTabsAsync, DeleteSessionAndNotesAsync | VERIFIED | Lines 702-822; full implementations with write-lock pattern        |
| `JoJot/Services/VirtualDesktopService.cs`     | OrphanedSessionGuids, SetOrphanedSessionGuids         | VERIFIED  | Lines 131-145; property and setter present; populated in MatchSessionsAsync |
| `JoJot/App.xaml.cs`                           | GetAllWindows(), OpenWindowForOrphanAsync(), UpdateOrphanBadge startup call | VERIFIED | Lines 143, 280, 286; all present and substantive |

### Key Link Verification

| From                          | To                                | Via                              | Status  | Details                                                                     |
|-------------------------------|-----------------------------------|----------------------------------|---------|-----------------------------------------------------------------------------|
| HamburgerButton Click         | HamburgerMenu.IsOpen toggle       | `HamburgerButton_Click`          | WIRED   | Line 1784-1787: `HamburgerMenu.IsOpen = !HamburgerMenu.IsOpen`              |
| MenuRecover_Click             | ShowRecoveryPanel()               | Direct call                      | WIRED   | Line 1852-1856: calls `ShowRecoveryPanel()` (fully implemented, not stub)   |
| ShowRecoveryPanel             | DatabaseService.GetOrphanedSessionInfoAsync | await in async void     | WIRED   | Line 1876: `var orphanInfos = await DatabaseService.GetOrphanedSessionInfoAsync(orphanGuids)` |
| Adopt button                  | MigrateTabsAsync + DeleteSessionAndNotesAsync | async lambda click      | WIRED   | Lines 1998-2004: calls both in sequence then RemoveOrphanGuid + RefreshAfterOrphanAction |
| Open button                   | App.OpenWindowForOrphanAsync      | async lambda click               | WIRED   | Lines 2010-2015: `await app.OpenWindowForOrphanAsync(guid)`                 |
| Delete button                 | DatabaseService.DeleteSessionAndNotesAsync | async lambda click      | WIRED   | Lines 2021-2025: await then RemoveOrphanGuid + RefreshAfterOrphanAction     |
| MenuDeleteOlder7_Click        | ConfirmAndDeleteOlderThanAsync(7) | fire-and-forget `_ =`            | WIRED   | Line 1813: `_ = ConfirmAndDeleteOlderThanAsync(7)`                          |
| ConfirmAndDeleteOlderThanAsync | ShowConfirmation → DeleteMultipleAsync | Action callback           | WIRED   | Lines 2123-2135: ShowConfirmation stores `() => _ = DeleteMultipleAsync(candidates)` |
| MenuExit_Click                | ExitApplication → FlushAndClose → Environment.Exit(0) | Dispatcher.BeginInvoke | WIRED | Lines 2095-2114: full chain verified                                      |
| MatchSessionsAsync            | OrphanedSessionGuids              | Direct property assignment       | WIRED   | Line 293: `OrphanedSessionGuids = orphanedGuids`                            |
| App startup (step 9.1)        | UpdateOrphanBadge                 | Direct method call               | WIRED   | App.xaml.cs line 143: `mainWindow.UpdateOrphanBadge()`                      |
| UpdateOrphanBadge             | OrphanBadge visibility + MenuRecoverText color | SetResourceReference  | WIRED | Lines 2074-2079: both elements updated based on orphan count              |

### Requirements Coverage

| Requirement | Source Plan | Description                                        | Status    | Evidence                                                                        |
|-------------|-------------|----------------------------------------------------|-----------|---------------------------------------------------------------------------------|
| MENU-01     | Plan 01     | Hamburger menu with 7 items                        | SATISFIED | HamburgerButton + HamburgerMenu Popup verified in XAML; all 7 items present     |
| MENU-02     | Plan 03     | Recover sessions panel + badge                     | SATISFIED | ShowRecoveryPanel(), RecoveryPanel XAML, UpdateOrphanBadge() all wired          |
| MENU-03     | Plan 02     | Delete older than N days with confirmation + count | SATISFIED | ConfirmAndDeleteOlderThanAsync counts by updated_at cutoff, calls ShowConfirmation |
| MENU-04     | Plan 02     | Delete all except pinned + Delete all with confirmation | SATISFIED | ConfirmAndDeleteExceptPinnedAsync + ConfirmAndDeleteAllAsync both implemented  |
| MENU-05     | Plan 02     | Bulk delete toast "N notes deleted" with undo      | SATISFIED | All three ConfirmAndDelete* delegate to existing DeleteMultipleAsync (Phase 5 toast) |
| CTXM-01     | Plan 01     | Tab right-click context menu (6 items)             | SATISFIED | BuildTabContextMenu creates Rename, Pin/Unpin, Clone, Save, Delete, Delete all below |
| CTXM-02     | Plan 01     | Delete all below skips pinned                      | SATISFIED | `_tabs.Skip(tabIndex + 1)` list passed to DeleteMultipleAsync (which skips pinned) |
| ORPH-01     | Plan 03     | Orphaned sessions stay in DB after failed match    | SATISFIED | MatchSessionsAsync populates OrphanedSessionGuids; no auto-deletion             |
| ORPH-02     | Plan 03     | Recovery panel with name, tab count, date          | SATISFIED | CreateRecoveryCard shows desktopName ?? "Unknown desktop", tabCount, lastUpdated.ToString() |
| ORPH-03     | Plan 03     | Adopt/Open/Delete per session card                 | SATISFIED | Three buttons with full async handlers: MigrateTabsAsync, OpenWindowForOrphanAsync, DeleteSessionAndNotesAsync |
| ORPH-04     | Plan 03     | Non-blocking badge on menu button at startup       | SATISFIED | OrphanBadge Ellipse (7px, c-accent), UpdateOrphanBadge() called in App startup step 9.1 |

**No orphaned requirements**: All 11 requirement IDs (MENU-01 through MENU-05, CTXM-01, CTXM-02, ORPH-01 through ORPH-04) claimed by Plans 01-03 map to REQUIREMENTS.md entries all marked Complete. No IDs assigned to Phase 8 in REQUIREMENTS.md are missing from the plans.

### Anti-Patterns Found

| File                    | Line | Pattern                                        | Severity | Impact                                                           |
|-------------------------|------|------------------------------------------------|----------|------------------------------------------------------------------|
| `MainWindow.xaml.cs`    | 1850 | Stale docstring: "stub until Plan 03" on MenuRecover_Click | Info | Method is fully wired; comment was written during Plan 01 and not updated in Plan 03. No functional impact. |
| `MainWindow.xaml.cs`    | 2083 | Preferences stub: `/* Phase 9: PREF-01 */`    | Info     | Expected — PREF-01 is Phase 9 scope. MenuPreferences_Click correctly closes menu and does nothing else. |

No blocker or warning anti-patterns found. Both info items are expected stubs (Preferences = Phase 9) or stale documentation.

### Build Verification

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.47
```

### Human Verification Required

The following items require runtime testing — they cannot be verified programmatically.

#### 1. Hamburger Menu Visual Rendering

**Test:** Launch the app. Click the hamburger button (leftmost icon in sidebar header).
**Expected:** Themed popup opens with all 7 items, correct Segoe Fluent Icons glyphs (E700 hamburger, E72C recover, E81C delete-older, E74D delete-except-pinned, E75C delete-all, E713 preferences, E7E8 exit), hover highlights using c-hover-bg, themed background matching sidebar.
**Why human:** Icon glyph rendering and visual styling cannot be verified from source.

#### 2. Delete Older Than Submenu Hover

**Test:** In the hamburger menu, hover over "Delete older than..."
**Expected:** Submenu flies out to the right immediately, showing 4 options (7/14/30/90 days). Moving mouse away from the parent item but into the submenu keeps it open.
**Why human:** Mouse event timing and submenu positioning are runtime behaviors.

#### 3. Bulk Delete Confirmation Dialog

**Test:** Click "Delete all" from the hamburger menu with at least one note present.
**Expected:** Modal overlay appears (not a system dialog) centered on window with semi-transparent backdrop, note count in message, Cancel and Delete buttons. Press Escape — overlay dismisses without deleting. Repeat, press Enter — deletion proceeds, toast appears.
**Why human:** Modal overlay appearance, keyboard intercept, and toast timing require runtime observation.

#### 4. Tab Context Menu Appearance and Behavior

**Test:** Right-click any tab.
**Expected:** Themed popup appears at cursor position with correct items and keyboard shortcut labels right-aligned in muted text. Pin text changes based on whether the tab is pinned.
**Why human:** Context menu visual styling and dynamic pin text require runtime data.

#### 5. Delete All Below Pinned-Tab Preservation

**Test:** Create 4 tabs, pin tabs 1 and 3. Right-click tab 2 and select "Delete all below."
**Expected:** Tab 4 (non-pinned, below tab 2) is deleted. Tab 3 (pinned, below tab 2) is preserved. Toast shows "1 note deleted."
**Why human:** Pinned-skip behavior with specific tab arrangements requires runtime data.

#### 6. Orphan Badge and Recovery Panel

**Test:** Requires a DB with an orphaned session (a session GUID that has no matching live virtual desktop). With orphans present:
- Open app — badge dot (7px, accent-colored) visible on hamburger button
- Hamburger menu shows "Recover sessions" in accent color
- Click "Recover sessions" — flyout slides in from left (150ms animation)
- Card shows desktop name, tab count, date, Adopt/Open/Delete buttons
- After Adopt — card removed, if last orphan badge disappears, tabs reloaded
**Why human:** Requires test DB with specific orphaned session data; animation requires visual observation.

#### 7. Exit Menu Multi-Window Behavior

**Test:** Open app on two virtual desktops (two windows). Use Exit from hamburger menu.
**Expected:** Both windows flush and close, app terminates completely.
**Why human:** Multi-window state requires runtime setup.

### Gaps Summary

No gaps found. All 11 must-haves are verified at all three levels (exists, substantive, wired). The build passes with 0 errors and 0 warnings. The only pending items are human verification of visual/interactive behaviors that cannot be confirmed from static analysis.

---

_Verified: 2026-03-03T11:30:00Z_
_Verifier: Claude (gsd-verifier)_
