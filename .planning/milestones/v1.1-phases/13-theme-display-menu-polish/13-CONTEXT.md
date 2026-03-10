# Phase 13: Theme, Display & Menu Polish - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix four specific visual/behavioral issues identified in the v1.0 manual review: dark mode tab text legibility, font size display format (pt → %), font size scaling to tab names, window title desktop name verification, and hamburger menu dismiss when clicking outside. No new features — polish only.

</domain>

<decisions>
## Implementation Decisions

### Dark mode tab contrast
- Only tab title text has a contrast issue — other dark mode elements (dates, search, menus, icons) are fine
- The tab label `TextBlock` (MainWindow.xaml.cs ~line 292) does not set an explicit Foreground — it inherits a default that's dark/black, making it unreadable on the `#252526` sidebar background
- Fix must ensure tab text is readable against both `c-sidebar-bg` (#252526) and `c-selected-bg` (#1A3A4A) in dark mode

### Font size scaling scope
- Font size changes (Ctrl+=/Ctrl+-/Ctrl+0/Ctrl+Scroll) affect **both** the editor text and the tab name labels in the sidebar
- This honors the original review note: "Text resize should show percentages instead of pt and affect the tabs as well"
- Editor font size range stays 8–32pt. Tab names scale proportionally
- Sidebar date/time text, menu items, and other UI elements do NOT scale (editor + tab titles only)

### Percentage display format
- Font size indicator shows percentage instead of point size (e.g., "120%" instead of "16pt")
- 100% baseline = 13pt (the current default/reset font size)
- Ctrl+0 resets to 100% (13pt)
- Three locations need updating: toolbar `FontSizeDisplay`, tooltip overlay `FontSizeTooltipText`, and keyboard shortcut help text

### Menu dismiss behavior
- Clicking anywhere outside the hamburger menu and its submenus closes everything immediately
- Bug is specifically in the "Delete older than" submenu interaction: hovering sets `StaysOpen=true` on the main menu, and this state can get stuck when clicking away
- The `StaysOpen` toggling logic between the main `HamburgerMenu` popup and `DeleteOlderSubmenu` needs to reliably reset

### Window title
- `UpdateDesktopTitle()` already sets `Title = "JoJot — {desktopName}"` — verify this works correctly against Windows Task View naming
- If desktop name is empty or in fallback mode, current behavior falls back to "JoJot — Desktop {index+1}" or plain "JoJot"

### Claude's Discretion
- Dark mode: Whether to use existing `c-text-primary` token or create a dedicated `c-tab-text` token
- Dark mode: Whether selected tab title should be brighter than unselected tabs
- Dark mode: Whether `c-selected-bg` needs adjustment for better contrast with text
- Font scaling: Whether date/time text under tab titles also scales
- Percentage display: Rounding approach (whole percent vs round increments)
- Percentage display: Tooltip format (percentage only vs both "120% (16pt)")
- Percentage display: Whether keyboard shortcut help says "100%" or "13pt"
- Menu: Whether Escape key closes the hamburger menu

</decisions>

<specifics>
## Specific Ideas

- The review note verbatim: "Text resize should show percentages instead of pt and affect the tabs as well"
- The menu bug is specifically triggered by hovering over "Delete older than" (which opens a submenu flyout) and then clicking outside — the `StaysOpen=true` state gets stuck

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ThemeService.cs`: Static theme manager with Light/Dark/System support. `ApplyTheme()` swaps ResourceDictionary at index 0
- Theme tokens: `c-text-primary` (#D4D4D4 dark / #1A1A1A light), `c-text-muted` (#808080), `c-selected-bg` (#1A3A4A dark), `c-hover-bg` (#2D2D2D dark), `c-sidebar-bg` (#252526 dark)
- `SetResourceReference()` pattern used throughout for dynamic theme binding

### Established Patterns
- Tab items are created in code-behind via `CreateTabListItem()` (~line 244) — no XAML templates
- Font size managed by `_currentFontSize` field, `ChangeFontSizeAsync()`, `SetFontSizeAsync()`, with persistence via `DatabaseService.GetPreferenceAsync("font_size")`
- Display text set imperatively: `FontSizeDisplay.Text = $"{size}pt"` and `FontSizeTooltipText.Text = $"{size}pt"`
- Hamburger menu is a WPF `Popup` with `StaysOpen="False"`, temporarily toggled to `StaysOpen=true` during submenu interaction
- Submenu close managed by `ScheduleSubmenuClose()` / `CancelSubmenuClose()` / `CloseSubmenu()` pattern with timer-based delay

### Integration Points
- `MainWindow.xaml.cs` line 292: `labelBlock` needs explicit Foreground for dark mode fix
- `MainWindow.xaml.cs` lines 2817, 2851, 2938, 2945: All `"{size}pt"` format strings need → percentage
- `MainWindow.xaml.cs` line 3176: Keyboard shortcut help text "Reset font size (13pt)"
- `MainWindow.xaml.cs` lines 134-139, 2040-2071: `StaysOpen` toggling logic for hamburger menu/submenu
- `MainWindow.xaml.cs` line 1682: `UpdateDesktopTitle()` for window title verification
- Tab label `FontSize = 13` (line 295) needs to become dynamic based on `_currentFontSize`

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 13-theme-display-menu-polish*
*Context gathered: 2026-03-04*
