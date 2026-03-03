# Plan 07-01: Theme Infrastructure — Summary

**Status:** Complete
**Duration:** ~5 min
**Commit:** feat(07-01): theme infrastructure, ThemeService, replace hardcoded colors

## What Was Built

Created the full theming infrastructure for JoJot:
- **LightTheme.xaml** and **DarkTheme.xaml**: 12 SolidColorBrush entries each (10 core tokens + 2 supplementary for toast)
- **ThemeService.cs**: Static service managing Light/Dark/System theme switching via ResourceDictionary swap
- **Database preference methods**: GetPreferenceAsync and SetPreferenceAsync added to DatabaseService
- **All hardcoded colors replaced**: MainWindow.xaml uses DynamicResource, MainWindow.xaml.cs uses SetResourceReference/GetBrush

## Key Decisions

- [07-01]: 12 color tokens (not just 10) — added c-toast-bg and c-toast-fg for toast overlay since toast colors differ from main UI in both themes
- [07-01]: SetResourceReference preferred over GetBrush for code-behind color assignments — ensures instant update on theme switch
- [07-01]: ApplyActiveHighlight changed from static to instance method — needed FindResource access for theme-aware accent brush
- [07-01]: CaretBrush added to ContentEditor — ensures cursor is visible in both light and dark themes
- [07-01]: SearchBox gets explicit Background/Foreground theme tokens — wasn't themed before

## Requirements Covered

- THME-01: Three themes (Light, Dark, System) via ThemeService.AppTheme enum
- THME-02: Instant switching via ResourceDictionary swap in MergedDictionaries
- THME-03: System theme auto-follows via SystemEvents.UserPreferenceChanged + Registry
- THME-04: 12 color tokens defined consistently in both theme files

## Self-Check: PASSED

- [x] Build succeeds with 0 errors, 0 warnings
- [x] LightTheme.xaml has 12 entries
- [x] DarkTheme.xaml has 12 matching entries
- [x] ThemeService has InitializeAsync, ApplyTheme, SetThemeAsync, Shutdown
- [x] No hardcoded color values remain in MainWindow.xaml (except Transparent)
- [x] No static brush fields remain in MainWindow.xaml.cs
- [x] GetPreferenceAsync/SetPreferenceAsync added to DatabaseService

## Key Files

### Created
- `JoJot/Themes/LightTheme.xaml`
- `JoJot/Themes/DarkTheme.xaml`
- `JoJot/Services/ThemeService.cs`

### Modified
- `JoJot/Services/DatabaseService.cs` (preference methods)
- `JoJot/App.xaml` (MergedDictionaries)
- `JoJot/App.xaml.cs` (ThemeService init/shutdown)
- `JoJot/MainWindow.xaml` (DynamicResource tokens)
- `JoJot/MainWindow.xaml.cs` (SetResourceReference/GetBrush)

---
*Plan: 07-01 | Phase: 07-theming-toolbar*
*Completed: 2026-03-03*
