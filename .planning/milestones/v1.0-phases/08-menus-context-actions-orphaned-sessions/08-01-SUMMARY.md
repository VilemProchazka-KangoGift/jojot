---
phase: 08-menus-context-actions-orphaned-sessions
plan: 01
subsystem: ui
tags: [wpf, xaml, popup, context-menu, hamburger-menu, segoe-fluent-icons, csharp]

# Dependency graph
requires:
  - phase: 07-theming-toolbar
    provides: ToolbarButtonStyle, DynamicResource color tokens, GetBrush helper, theme-aware patterns
provides:
  - Hamburger button (glyph E700) in sidebar header left of search box
  - Hamburger menu popup with 7 items: Recover, Delete older than (submenu), Delete except pinned, Delete all, Preferences, Exit
  - Tab right-click context menu popup with 7 items: Rename, Pin/Unpin, Clone, Save as TXT, Delete, Delete all below
  - App.GetAllWindows() for multi-window Exit handler
  - Orphan badge Ellipse on hamburger button (wired in Plan 03)
  - Bulk delete confirmation overlay XAML (wired in Plan 02)
  - Recovery flyout panel XAML (wired in Plan 03)
  - VirtualDesktopService.OrphanedSessionGuids exposed property (Plan 03)
  - DatabaseService.GetOrphanedSessionInfoAsync and MigrateTabsAsync (Plan 03)
affects:
  - 08-02 (bulk delete confirmation — wires ConfirmAndDeleteOlderThanAsync, ConfirmAndDeleteExceptPinnedAsync, ConfirmAndDeleteAllAsync)
  - 08-03 (orphan recovery — wires ShowRecoveryPanel, RecoveryPanel, RecoverySessionList)

# Tech tracking
tech-stack:
  added:
    - System.Windows.Controls.Primitives (Popup namespace — now imported)
  patterns:
    - Custom Popup for menus instead of WPF ContextMenu — enables consistent theming with DynamicResource
    - Popup.StaysOpen=false for auto-dismiss on outside click
    - SetResourceReference on dynamically created elements for live theme updates
    - MenuItem_MouseEnter/Leave pattern for hover highlight on Border elements
    - PlacementMode.MousePoint for right-click context menus
    - Dispatcher.BeginInvoke(DispatcherPriority.Background) before shutdown for menu close animation

key-files:
  created: []
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs
    - JoJot/Services/DatabaseService.cs
    - JoJot/Services/VirtualDesktopService.cs

key-decisions:
  - "Custom Popup used for both hamburger menu and context menu — WPF ContextMenu cannot use DynamicResource for background without complex template override"
  - "Hamburger submenu (Delete older than) shows on MouseEnter rather than click — more discoverable, matches Windows 11 patterns"
  - "HamburgerMenu.Closed event wired to close DeleteOlderSubmenu — prevents dangling submenu when parent closes via StaysOpen=false"
  - "Context menu delegates to existing ToolbarPin_Click/ToolbarClone_Click/ToolbarSave_Click by setting _activeTab and calling SelectTabByNote first — reuses all existing behavior without duplication"
  - "ExitApplication uses App.GetAllWindows() public method — avoids reflection/internal access while keeping _windows field private"
  - "ConfirmAndDelete* methods are stubs returning Task.CompletedTask — Plan 02 replaces them with real confirmation overlay logic"

patterns-established:
  - "Menu popup pattern: Border(c-sidebar-bg, c-border, CornerRadius=6, DropShadow) wrapping StackPanel of Border items"
  - "Menu item hover: MouseEnter sets Background=GetBrush(c-hover-bg), MouseLeave resets to Brushes.Transparent"
  - "Dynamic popup items: SetResourceReference for theme colors, not GetBrush (ensures live theme update)"

requirements-completed: [MENU-01, CTXM-01, CTXM-02]

# Metrics
duration: 4min
completed: 2026-03-03
---

# Phase 8 Plan 01: Hamburger Menu and Tab Context Menu Summary

**Custom-themed Popup menus for hamburger (7 items with submenu) and tab right-click (7 items) using DynamicResource colors and Segoe Fluent Icons**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-03T09:46:54Z
- **Completed:** 2026-03-03T09:50:58Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- Hamburger button (E700 glyph) added to sidebar header left of search box with orphan badge Ellipse
- Full hamburger popup menu with themed styling: Recover sessions, Delete older than (submenu with 7/14/30/90 days), Delete all except pinned, Delete all, Preferences, Exit
- Exit handler flushes all open windows via App.GetAllWindows() then calls Environment.Exit(0)
- Tab right-click context menu with dynamically-built Popup matching hamburger menu styling
- Pin/Unpin context item text dynamically shows "Pin" or "Unpin" based on tab.Pinned state
- "Delete all below" uses DeleteMultipleAsync with tabs below index, skipping pinned (TDEL-06)
- Pre-wired XAML for bulk delete confirmation overlay and recovery flyout (Plans 02 and 03)

## Task Commits

1. **Task 1: Hamburger button and popup menu** - `9577acc` (feat)
2. **Task 2: Tab right-click context menu** - `3ae7026` (feat)
3. **Fix: RecoveryClose_Click stub** - `d9b44c6` (fix - Rule 3 blocking build)

**Plan metadata:** (docs commit — see state updates)

## Files Created/Modified
- `JoJot/MainWindow.xaml` - Added 3-column sidebar header, hamburger button+badge, hamburger popup, bulk confirmation overlay, recovery flyout panel
- `JoJot/MainWindow.xaml.cs` - Added Popup namespace, _activeContextMenu/_confirmAction/_recoveryPanelOpen fields, all hamburger menu handlers, BuildTabContextMenu(), right-click wiring, confirmation overlay stubs
- `JoJot/App.xaml.cs` - Added GetAllWindows() public method for Exit handler
- `JoJot/Services/DatabaseService.cs` - Added GetOrphanedSessionInfoAsync, MigrateTabsAsync for Plan 03
- `JoJot/Services/VirtualDesktopService.cs` - Added OrphanedSessionGuids property and SetOrphanedSessionGuids for Plan 03

## Decisions Made
- Used custom Popup (not WPF ContextMenu) for both menus — WPF ContextMenu cannot use DynamicResource for background without complex template override; custom Border+StackPanel approach gives full control
- "Delete older than" submenu shown on MouseEnter (not click) — more discoverable, matches Windows 11 flyout patterns
- HamburgerMenu.Closed event wired to close DeleteOlderSubmenu — prevents dangling submenu
- Context menu delegates to existing ToolbarPin/Clone/Save handlers by setting _activeTab first
- ExitApplication uses App.GetAllWindows() — keeps _windows field private while enabling cross-window access
- Confirmation and recovery XAML pre-wired (stubs in CS) to keep Plans 02/03 incremental

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added RecoveryClose_Click stub handler**
- **Found during:** Task 2 verification build
- **Issue:** Linter added Recovery flyout XAML referencing RecoveryClose_Click handler; build failed with CS1061
- **Fix:** Added stub `private void RecoveryClose_Click` that hides RecoveryPanel — full implementation in Plan 03
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** dotnet build succeeds with 0 errors
- **Committed in:** d9b44c6

**2. [Rule 3 - Blocking] Added missing Popup namespace using directive**
- **Found during:** Task 1 verification build
- **Issue:** `Popup` type in CS file required System.Windows.Controls.Primitives import
- **Fix:** Added `using System.Windows.Controls.Primitives;` to using directives
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** Build succeeded after adding namespace
- **Committed in:** 9577acc (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes required for compilation. No scope creep — linter-added XAML stubs are pre-wiring for Plans 02 and 03 as intended by the phase plan.

## Issues Encountered
- Linter added `_confirmAction`, `_recoveryPanelOpen` fields and recovery flyout XAML panel proactively during execution — these are valid Phase 8 infrastructure, accepted and incorporated with minimal stubs

## Next Phase Readiness
- Plan 02 (bulk delete confirmation) can wire ConfirmAndDeleteOlderThanAsync/ExceptPinned/All — overlay XAML and buttons already in MainWindow.xaml
- Plan 03 (orphan recovery) can wire ShowRecoveryPanel, RecoveryPanel, RecoverySessionList, RecoveryClose_Click — all XAML pre-built, OrphanedSessionGuids exposed, DB methods ready
- DatabaseService.GetOrphanedSessionInfoAsync and MigrateTabsAsync ready for Plan 03 consumption

---
*Phase: 08-menus-context-actions-orphaned-sessions*
*Completed: 2026-03-03*
