---
phase: quick-12
plan: 01
subsystem: hotkey
tags: [win32, keyboard-hook, WH_KEYBOARD_LL, RegisterHotKey, P/Invoke]

requires: []
provides:
  - "Low-level keyboard hook to suppress Win key Start Menu during hotkey recording"
  - "Same-combo re-registration detection in UpdateHotkeyAsync"
  - "Escape-to-cancel hotkey recording"
affects: []

tech-stack:
  added: []
  patterns:
    - "WH_KEYBOARD_LL hook for suppressing OS-level key behavior during recording"
    - "Same-combo detection pattern: check modifiers+vk match before unregister/register cycle"

key-files:
  created: []
  modified:
    - JoJot/Services/HotkeyService.cs
    - JoJot/Controls/PreferencesPanel.xaml.cs
    - JoJot/Views/MainWindow.Keyboard.cs
    - JoJot.Tests/Services/HotkeyServiceTests.cs

key-decisions:
  - "Used WH_KEYBOARD_LL hook to suppress Win key rather than other approaches (subclassing, input simulation) because it is the standard Win32 mechanism for intercepting keys before the shell processes them"
  - "Stop LL hook before calling UpdateHotkeyAsync so RegisterHotKey sees normal key state"
  - "StopRecordingMode is idempotent and called from multiple cleanup paths for safety"

patterns-established:
  - "Recording mode lifecycle: StartRecordingMode on enter, StopRecordingMode on every exit path (capture, cancel, escape, panel close)"

requirements-completed: [HOTKEY-FIX]

duration: 4min
completed: 2026-03-13
---

# Quick Task 12: Fix Global Hotkey Recording Summary

**WH_KEYBOARD_LL hook suppresses Win key Start Menu during recording; same-combo detection prevents false "already in use" errors; Escape cancels recording**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-13T13:19:27Z
- **Completed:** 2026-03-13T13:23:48Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Win key no longer opens Start Menu during hotkey recording -- low-level keyboard hook intercepts and suppresses VK_LWIN/VK_RWIN
- Re-recording the same hotkey combo (e.g., Win+Shift+N when Win+Shift+N is already set) succeeds instead of falsely showing "already in use"
- Escape during recording cancels and restores the previous hotkey via ResumeHotkey
- All cleanup paths (capture, cancel click, escape, panel close, app shutdown) properly remove the LL hook

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix HotkeyService -- add low-level keyboard hook and same-combo detection** - `545066a` (feat)
2. **Task 2: Wire recording mode hook into PreferencesPanel and keyboard handler** - `3fbaf75` (feat)

## Files Created/Modified
- `JoJot/Services/HotkeyService.cs` - Added WH_KEYBOARD_LL hook (StartRecordingMode/StopRecordingMode/LowLevelKeyboardHookProc), same-combo detection in UpdateHotkeyAsync, hook cleanup in Shutdown
- `JoJot/Controls/PreferencesPanel.xaml.cs` - Wire StartRecordingMode on Record click, StopRecordingMode on cancel/close/capture
- `JoJot/Views/MainWindow.Keyboard.cs` - Add Escape-to-cancel recording, stop LL hook before UpdateHotkeyAsync call
- `JoJot.Tests/Services/HotkeyServiceTests.cs` - Added GetCurrentHotkey test

## Decisions Made
- Used WH_KEYBOARD_LL hook to suppress Win key rather than alternatives (subclassing, input simulation) because it is the standard Win32 mechanism for intercepting keys before the shell processes them
- Stop LL hook before calling UpdateHotkeyAsync so RegisterHotKey sees normal key state -- the hook must not be active when the OS processes the registration
- Made StopRecordingMode idempotent (safe to call multiple times) since it is invoked from multiple cleanup paths

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- JoJot.exe running in debug mode locked the output binary, preventing `dotnet build` from copying the exe. Verified compilation success using alternate output path (`-p:OutputPath=obj/test-verify/`). No C# compilation errors were present.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Hotkey recording feature is fully functional
- All three original bugs (Win key stealing focus, same-combo false positive, default hotkey broken after re-record) are fixed

## Self-Check: PASSED

- All 4 modified files exist on disk
- Commit 545066a (Task 1) verified in git log
- Commit 3fbaf75 (Task 2) verified in git log
- Build succeeds with 0 CS errors, 0 warnings

---
*Quick Task: 12-fix-global-hotkey-record-shortcut-loses-*
*Completed: 2026-03-13*
