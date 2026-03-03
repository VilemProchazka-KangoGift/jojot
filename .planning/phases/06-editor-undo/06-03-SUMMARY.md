# Plan 06-03: Enhanced Ctrl+C copy, Save As TXT — Summary

**Status:** Complete
**Duration:** ~2 min (implemented alongside Plan 02)
**Commit:** 9c99dc3

## What was built

Two editor behaviors added to MainWindow.xaml.cs:

1. **Enhanced Ctrl+C** (EDIT-06) — When no text is selected, copies entire note content to clipboard silently via Clipboard.SetText. When text IS selected, falls through to WPF's built-in copy (e.Handled not set). Clipboard access wrapped in try/catch for ExternalException resilience.

2. **Save As TXT** (EDIT-07) — Ctrl+S opens Microsoft.Win32.SaveFileDialog with:
   - UTF-8 with BOM encoding via `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`
   - Smart default filename: tab name > first 30 chars content > "JoJot note YYYY-MM-DD.txt"
   - Filename sanitization (illegal chars replaced with underscore, trailing dots/spaces trimmed)
   - Last save directory remembered in `_lastSaveDirectory` (session-only, resets on launch)
   - No feedback after save — dialog closing is sufficient confirmation

## Key decisions

- Combined with Plan 02 implementation since both modify Window_PreviewKeyDown in MainWindow.xaml.cs
- Ctrl+C handler placed after undo/redo but before Ctrl+W in PreviewKeyDown for consistent key priority
- SanitizeFilename handles all Path.GetInvalidFileNameChars() plus trailing dot/space trimming

## Self-Check: PASSED

- [x] Ctrl+C with no selection copies entire note silently
- [x] Ctrl+C with selection lets WPF handle normally
- [x] Ctrl+S opens save dialog with correct defaults
- [x] File saved as UTF-8 with BOM
- [x] Filename sanitization handles illegal characters
- [x] Compiles without C# errors

## Key files

### Modified
- `JoJot/MainWindow.xaml.cs`
