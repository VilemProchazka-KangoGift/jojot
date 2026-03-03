---
phase: 09-file-drop-preferences-hotkeys-keyboard
plan: 02
subsystem: ui
tags: [wpf, preferences, hotkey, registerhotkey, pinvoke, win32, theme, font-size]

# Dependency graph
requires:
  - phase: 07-theming
    provides: ThemeService with Light/Dark/System modes
  - phase: 05-rich-tab-features
    provides: AutosaveService with DebounceMs property
  - phase: 02-database
    provides: DatabaseService preferences table (GetPreferenceAsync, SetPreferenceAsync)
provides:
  - HotkeyService with Win32 RegisterHotKey P/Invoke
  - Slide-in preferences panel (theme, font size, debounce, hotkey picker)
  - Global hotkey toggle focus/minimize behavior
  - Font size persistence and live application
affects: [10-virtual-desktop-tab-moves]

# Tech tracking
tech-stack:
  added:
    - Win32 RegisterHotKey/UnregisterHotKey P/Invoke
    - HwndSource message hook for WM_HOTKEY
  patterns:
    - Slide-in panel with TranslateTransform animation
    - Hotkey recording mode with keyboard capture

key-files:
  created:
    - JoJot/Services/HotkeyService.cs
  modified:
    - JoJot/MainWindow.xaml
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs

key-decisions:
  - "Default global hotkey is Win+Shift+N with MOD_NOREPEAT to prevent auto-repeat"
  - "Hotkey toggle: if active and not minimized -> minimize; otherwise -> restore + activate"
  - "Preferences panel width 300px, slides in from right with 250ms CubicEase animation"
  - "Font size range 8-32pt, default 13pt, persisted to preferences table"
  - "Debounce input range 200-2000ms with 500ms typing delay before persistence"

patterns-established:
  - "Win32 P/Invoke service pattern for system-wide functionality"
  - "Slide-in panel pattern: Border with TranslateTransform, show/hide with DoubleAnimation"
  - "Hotkey recording mode pattern: toggle recording state, capture in Window_PreviewKeyDown"

requirements-completed: [PREF-01, PREF-02, PREF-03, PREF-04, PREF-05, KEYS-01]

# Metrics
duration: 25min
completed: 2026-03-03
---

# Phase 9 Plan 02: Preferences & Global Hotkey Summary

**Slide-in preferences panel with live theme/font/debounce controls and Win32 RegisterHotKey global hotkey with toggle focus/minimize**

## Performance

- **Duration:** 25 min
- **Started:** 2026-03-03
- **Completed:** 2026-03-03
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- HotkeyService with Win32 RegisterHotKey P/Invoke, WM_HOTKEY message hook, persistence, and conflict detection
- Preferences slide-in panel (300px, animated) with theme toggle, font size +/-, debounce input, hotkey picker
- Global hotkey (default Win+Shift+N) toggles JoJot focus/minimize from any application
- Hotkey recording mode with visual feedback, conflict detection, and fallback to previous hotkey
- All preference changes persist to database and apply live without restart

## Task Commits

All tasks committed together due to shared file modifications:

1. **Task 1: Create HotkeyService with Win32 RegisterHotKey** - `1f2c496` (feat)
2. **Task 2: Add preferences slide-in panel and wire global hotkey** - `1f2c496` + `c485c33` (feat)

## Files Created/Modified
- `JoJot/Services/HotkeyService.cs` - Static service: RegisterHotKey P/Invoke, WM_HOTKEY hook, hotkey persistence
- `JoJot/MainWindow.xaml` - PreferencesPanel Border with theme/font/debounce/hotkey sections
- `JoJot/MainWindow.xaml.cs` - Preferences methods, theme handlers, font size handlers, hotkey recording
- `JoJot/App.xaml.cs` - HotkeyService.InitializeAsync with toggle callback, HotkeyService.Shutdown

## Decisions Made
- HotkeyService is static (matches ThemeService, DatabaseService pattern) since only one hotkey registration per process
- Hotkey initialization happens in CreateWindowForDesktop (only for first window) since HWND required
- Failed hotkey registration shows toast on startup, attempts to register can be retried via preferences
- UpdateHotkeyAsync restores previous hotkey if new registration fails

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed duplicate MenuPreferences_Click**
- **Found during:** Build verification
- **Issue:** Old Phase 8 stub and new Phase 9 implementation both defined MenuPreferences_Click
- **Fix:** Removed the old stub method
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** `1f2c496`

**2. [Rule 3 - Blocking] Fixed FontFamily ambiguity**
- **Found during:** Build verification
- **Issue:** `FontFamily` ambiguous between System.Drawing and System.Windows.Media
- **Fix:** Fully qualified to `System.Windows.Media.FontFamily`
- **Files modified:** JoJot/MainWindow.xaml.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** `1f2c496`

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes necessary for compilation. No scope creep.

## Issues Encountered
None beyond the auto-fixed build issues.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Preferences infrastructure complete, Plan 03 can use SetFontSizeAsync and ChangeFontSizeAsync
- HotkeyService initialized and ready for hotkey display in help overlay

---
*Phase: 09-file-drop-preferences-hotkeys-keyboard*
*Completed: 2026-03-03*
