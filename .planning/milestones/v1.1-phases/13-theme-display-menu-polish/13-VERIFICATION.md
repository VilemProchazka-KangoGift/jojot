---
phase: 13-theme-display-menu-polish
verified: 2026-03-04T00:00:00Z
status: human_needed
score: 7/8 must-haves verified
human_verification:
  - test: "WIN-01 — Window title shows virtual desktop name"
    expected: "Title bar reads 'JoJot — Desktop 1' (or named desktop) matching Windows Task View"
    why_human: "Requires running app against live Windows virtual desktop COM APIs; cannot grep for runtime title value"
  - test: "WIN-02 — Hamburger menu closes on outside click after submenu hover"
    expected: "After hovering over 'Delete older than...' and clicking outside the menu area, both the submenu and main hamburger popup close immediately"
    why_human: "WPF popup light-dismiss with timer race conditions cannot be verified statically; requires live interaction"
---

# Phase 13: Theme, Display & Menu Polish Verification Report

**Phase Goal:** Dark mode is legible, font size feedback is clear, and menus dismiss predictably
**Verified:** 2026-03-04
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Dark mode tab names use c-text-primary (#D4D4D4) against dark sidebar/selected backgrounds | VERIFIED | `MainWindow.xaml.cs:318` — `labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary")` before placeholder override at line 323 |
| 2 | Font size indicator shows percentage (e.g. "120%"), not point size | VERIFIED | `FontSizeToPercent` helper at line 2968; used in `SetFontSizeAsync` (line 2974), `ShowFontSizeTooltip` (line 2982), `InitializePreferencesAsync` (line 2851), `ShowPreferencesPanel` (line 2885) — no remaining `{size}pt` format strings in those paths |
| 3 | Tab labels in sidebar scale when user changes font size | VERIFIED | `labelBlock.FontSize = _currentFontSize` (line 314); `renameBox.FontSize = _currentFontSize` (line 332); `SetFontSizeAsync` calls `RebuildTabList()` at line 2977 |
| 4 | Ctrl+0 resets to 100% — keyboard help says "100%" not "13pt" | VERIFIED | `MainWindow.xaml.cs:3213` — `("Ctrl+0", "Reset font size (100%)")` |
| 5 | StaysOpen cannot get stuck true after submenu interaction | VERIFIED | `DeleteOlderSubmenu.Closed` handler (lines 143-146) unconditionally resets `HamburgerMenu.StaysOpen = false`; this fires regardless of how the submenu closes |
| 6 | PreviewMouseDown force-closes both popups on outside click | VERIFIED | Lines 149-158: handler checks `HamburgerMenu.IsOpen && !IsMouseOverPopup(HamburgerMenu) && !IsMouseOverPopup(DeleteOlderSubmenu) && !IsMouseOverElement(HamburgerButton)` then sets both `IsOpen = false` |
| 7 | Window title uses UpdateDesktopTitle to show desktop name | VERIFIED | `UpdateDesktopTitle` method at line 1702 formats title as `"JoJot — {desktopName}"` or `"JoJot — Desktop {n+1}"` or `"JoJot"`; called from `App.xaml.cs:202,208` on window creation and `App.xaml.cs:156` on desktop rename events |
| 8 | Window title and menu dismiss work correctly at runtime | NEEDS HUMAN | Cannot verify live WPF popup behavior or Windows virtual desktop COM integration statically |

**Score:** 7/8 truths verified (automated); 2 items require human confirmation

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `JoJot/MainWindow.xaml.cs` | Tab label foreground binding, percentage display, tab font scaling, menu dismiss fix | VERIFIED | All implementations confirmed at lines 314, 318, 332, 2968, 2974, 2977, 2982, 143-146, 149-158, 2073-2081 |
| `JoJot/Themes/DarkTheme.xaml` | `c-text-primary` token (#D4D4D4) — no new token needed | VERIFIED | Line 12: `<SolidColorBrush x:Key="c-text-primary" Color="#D4D4D4"/>` |
| `JoJot/Themes/LightTheme.xaml` | `c-text-primary` token (#1A1A1A) — no new token needed | VERIFIED | Line 12: `<SolidColorBrush x:Key="c-text-primary" Color="#1A1A1A"/>` |

---

## Key Link Verification

### Plan 01 Key Links (THEME-01, THEME-02)

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `CreateTabListItem()` | `c-text-primary` resource | `SetResourceReference` on `labelBlock.ForegroundProperty` | WIRED | Line 318: `labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary")` — correct position before placeholder override |
| `SetFontSizeAsync()` | `RebuildTabList()` | Direct method call at end of `SetFontSizeAsync` | WIRED | Line 2977: `RebuildTabList();  // Propagate font size to tab labels` |
| `CreateTabListItem()` | `_currentFontSize` | `FontSize` assignment on `labelBlock` | WIRED | Line 314: `FontSize = _currentFontSize` (also `renameBox` at line 332) |

### Plan 02 Key Links (WIN-01, WIN-02)

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MenuDeleteOlder_MouseEnter` | `HamburgerMenu.StaysOpen` | Sets `StaysOpen=true` to hold menu during submenu hover | WIRED | Line 2104: `HamburgerMenu.StaysOpen = true;` |
| `CloseSubmenu()` | `HamburgerMenu.StaysOpen` | Resets `StaysOpen=false` when submenu timer fires | WIRED | Line 2065: `HamburgerMenu.StaysOpen = false;` |
| `DeleteOlderSubmenu.Closed` event | `HamburgerMenu.StaysOpen = false` | New safety-net handler (Phase 13) | WIRED | Lines 143-146: unconditional `Closed` handler resets `StaysOpen=false` |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| THEME-01 | 13-01-PLAN.md | Dark mode tab names are legible (proper contrast) | SATISFIED | `labelBlock.SetResourceReference(ForegroundProperty, "c-text-primary")` at line 318; dark theme has `#D4D4D4` giving ~10:1 ratio against `#252526` sidebar |
| THEME-02 | 13-01-PLAN.md | Text resize shows percentages instead of pt and affects tab labels too | SATISFIED | `FontSizeToPercent` helper at line 2968; all 4 display locations use it; `RebuildTabList()` called in `SetFontSizeAsync` at line 2977 |
| WIN-01 | 13-02-PLAN.md | Virtual desktop name appears in window title | SATISFIED (code) / NEEDS HUMAN (runtime) | `UpdateDesktopTitle` at line 1702 wired in `App.xaml.cs` on window create and desktop rename; runtime behavior needs human confirmation |
| WIN-02 | 13-02-PLAN.md | Hamburger menu closes when user clicks anywhere outside of it | SATISFIED (code) / NEEDS HUMAN (runtime) | `DeleteOlderSubmenu.Closed` safety net + `PreviewMouseDown` force-close; runtime behavior after submenu hover needs human confirmation |

**Orphaned requirements check:** No requirements mapped to Phase 13 in REQUIREMENTS.md traceability table beyond the four above. No orphans detected.

**REQUIREMENTS.md documentation note:** THEME-01 and THEME-02 are still marked `[ ]` (pending) and "Pending" in the traceability table at lines 26-27 and 65-66. The code is implemented and committed. This is a documentation gap — the checkboxes and status should be updated to match WIN-01/WIN-02 (marked complete). This does not affect goal achievement but should be corrected.

---

## Build Verification

Build result: **succeeded** with 1 pre-existing warning (unrelated to phase 13 changes):

```
warning CS4014: Because this call is not awaited, execution of the current method continues
before the call is completed (MainWindow.xaml.cs:534)
```

This warning is at line 534, predates phase 13, and does not affect phase 13 functionality.

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `MainWindow.xaml.cs` | 906 | Comment reads `// Ctrl+0 or Ctrl+Numpad0: Reset font size to 13pt` — stale after THEME-02 | Info | Comment-only stale reference; does not affect behavior |

No stub implementations, no empty handlers, no TODO markers in phase-13 modified code paths.

---

## Human Verification Required

### 1. WIN-01: Window Title Shows Desktop Name

**Test:** Launch the app (`dotnet run --project JoJot/JoJot.csproj`). Look at the window title bar.
**Expected:** Title reads "JoJot — Desktop 1" (or the name of your virtual desktop if named in Windows Task View). If you have multiple virtual desktops, switch desktops and open JoJot there — the title on each window should reflect that desktop's name.
**Why human:** Requires live Windows virtual desktop COM APIs to be available and returning desktop names. Cannot be verified statically.

### 2. WIN-02: Hamburger Menu Dismiss After Submenu Hover

**Test:**
1. Click the hamburger menu button — menu opens
2. Hover over "Delete older than..." — submenu flyout appears
3. Move the mouse away from both the menu and submenu into the editor area
4. Click in the editor area

**Expected:** Both the submenu and the main hamburger popup close immediately on the click. Menu should not remain open. Repeat by clicking on the sidebar (tab list) area and title bar area — same result.

**Why human:** WPF popup light-dismiss with `StaysOpen` state, timer-based submenu close, and `PreviewMouseDown` routing involves race conditions that can only be confirmed through live interaction. The code is correctly structured but the timing interaction between the 250ms close timer and click events cannot be statically verified.

---

## Summary

Phase 13 goal is **achieved in code** across all four requirements:

- **THEME-01**: Dark mode tab legibility is fixed. `labelBlock` receives an explicit `c-text-primary` foreground binding (line 318) before the placeholder override, ensuring all non-placeholder tabs display `#D4D4D4` against the dark sidebar. Token wiring is correct.

- **THEME-02**: Font size display is fully percentage-based. `FontSizeToPercent` helper (line 2968) is called in all four display locations. Tab labels use `_currentFontSize` (not hard-coded 13) and `SetFontSizeAsync` calls `RebuildTabList()` to propagate changes to existing tabs. The keyboard shortcut help correctly reads "Reset font size (100%)".

- **WIN-01**: `UpdateDesktopTitle` exists, formats the title correctly, and is called at window creation (`App.xaml.cs:202,208`) and on desktop rename events (`App.xaml.cs:156`). Runtime verification needs human confirmation.

- **WIN-02**: Two-layer fix is in place: `DeleteOlderSubmenu.Closed` handler as unconditional safety net, and `PreviewMouseDown` on the window as active force-close. `IsMouseOverPopup` uses `popup.Child.IsMouseOver` correctly (WPF popup is not in the visual tree). Runtime behavior after submenu hover needs human confirmation.

One documentation gap: REQUIREMENTS.md traceability table should mark THEME-01 and THEME-02 as "Complete" to match the committed implementation.

---

_Verified: 2026-03-04_
_Verifier: Claude (gsd-verifier)_
