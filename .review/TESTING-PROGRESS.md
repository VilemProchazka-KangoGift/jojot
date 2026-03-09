# Testing Progress Tracker

**Baseline**: 28.5% line coverage (1987/6979), 302 tests
**Target**: ~580 tests, ~35.5% line coverage

## Status Legend
- [ ] Not started
- [~] In progress
- [x] Complete
- [-] Skipped

---

## Tier 1: Extract Logic from MainWindow

| # | Item | Est. Lines | Est. Tests | Status | Session |
|---|------|-----------|-----------|--------|---------|
| T1.1 | PreviewKeyDown → KeyAction router | ~100 | ~40 | [ ] | |
| T1.2 | Find engine (FindAllMatches, CycleIndex) | ~30 | 23 | [x] | 6 |
| T1.3 | Tab deletion orchestration / state machine | ~35 | ~12 | [ ] | |
| T1.4 | Font size parse & clamp | ~10 | 17 | [x] | 5 |
| T1.5 | Toast content formatting | ~8 | ~5 | [ ] | |
| T1.6 | Context menu pin state | ~5 | ~2 | [ ] | |

## Tier 2: Service & Pure Logic Testing

| # | Item | Est. Lines | Est. Tests | Status | Session |
|---|------|-----------|-----------|--------|---------|
| T2.1 | ComGuids.Resolve (build→GuidSet) | ~13 | 14 | [x] | 1 |
| T2.2 | ThemeService (preference ↔ enum) | ~12 | 13 | [x] | 6 |
| T2.3 | HotkeyService FormatHotkey catch path | ~4 | 0 | [-] | 1 |
| T2.4 | DatabaseCore corruption & integrity | ~30 | 8 | [x] | 2 |
| T2.5 | LogService template overloads | ~15 | 0 | [-] | 1 |
| T2.6 | IpcService (mutex, stop server) | ~10 | ~4 | [ ] | |
| T2.7 | Store async cancellation paths (38 methods) | ~100 | 7 | [x] | 6 |
| T2.8 | StartupService.EscapeSql | ~1 | 6 | [x] | 1 |
| T2.9 | FileDropService edge cases | ~8 | 21 | [x] | 1 |
| T2.10 | WindowPlacementHelper (zero-hwnd, neg coords) | ~5 | ~5 | [ ] | |

## Tier 3: Refactor & Test Complex Logic

| # | Item | Est. Lines | Est. Tests | Status | Session |
|---|------|-----------|-----------|--------|---------|
| T3.1 | VirtualDesktopService.MatchSessionsAsync | ~80 | ~20 | [ ] | |
| T3.2 | App.xaml.cs extractable logic | ~20 | 16 | [x] | 6 |
| T3.3 | PreferencesPanel.FontSizeToPercent | ~1 | 6 | [x] | 1 |

## Tier 5: Edge Case & Boundary Tests (on 100%-covered code)

| # | Item | Est. Tests | Status | Session |
|---|------|-----------|--------|---------|
| T5.1 | NoteTab boundaries (midnight, JustNow 59/60/61s, 31-char, whitespace, null Content, year boundary, AM/PM) | 29 | [x] | 3 |
| T5.2 | ViewModel logical gaps (FilteredTabs empty, MoveTab 1-item, pin boundary, RemoveMultiple non-contiguous, RestoreTabs scattered, cleanup cutoff exact, null/negative SaveEditorState, SanitizeFilename Unicode/illegal, ReorderAfterPinToggle alternating) | 21 | [x] | 4 |
| T5.3 | Store edge cases (preview truncation 59/60/61, migration pin order, MaxSortOrder gaps, GUID case, delete non-existent session, duplicate PendingMove, orphan ordering) | 22 | [x] | 2 |
| T5.4 | UndoManager/Stack behavioral (two-phase collapse, SetActive+Remove, tabId=0, dedup empty, MaxTier1/2 exact, 5min boundary, emoji bytes, empty tier collapse, EvictOldest(0), 100+ cycle, tier-boundary undo, tier-boundary truncate) | 23 | [x] | 5 |
| T5.5 | IPC & Model edge cases (unknown discriminator, case sensitivity, DesktopInfo record, AppState negative/zero, WindowGeometry negative, PendingMove same from/to) | 24 | [x] | 1 |
| T5.6 | HotkeyService & FileDropService gaps (all modifier combos, invalid VK, ModifierKeys.None, byte boundaries 0x08/0x0E-0x1A/0x20, empty file, exact 8KB) | 15 | [x] | 1 |

## Tier 4: Skip (Not Unit-Testable)

| # | Item | Lines | Reason | Status |
|---|------|-------|--------|--------|
| T4.1 | VirtualDesktopInterop (COM) | 179 | COM server required | [-] |
| T4.2 | VirtualDesktopNotificationListener | 80 | COM callbacks | [-] |
| T4.3 | Controls code-behind (6 controls) | 573 | Pure WPF UI | [-] |
| T4.4 | MainWindow UI-only methods | ~800 | Animations, visual tree | [-] |
| T4.5 | IpcMessageContext (auto-gen JSON) | 62 | Source-generated | [-] |
| T4.6 | App.OnAppStartup (full sequence) | 136 | Integration-only | [-] |
| T4.7 | DispatcherDebounceTimer | 5 | WPF timer wrapper | [-] |
| T4.8 | WindowActivationHelper | 23 | Win32 P/Invoke | [-] |

---

## Recommended Work Order (by session)

### Session 1: Quick Wins — Zero Refactoring (~50 tests)
- **T2.1** ComGuids.Resolve — 7 tests
- **T2.3** HotkeyService FormatHotkey catch — 2 tests
- **T2.8** StartupService.EscapeSql — 4 tests
- **T3.3** FontSizeToPercent — 5 tests
- **T2.5** LogService overloads — 10 tests
- **T5.6** HotkeyService all modifier combos + FileDropService byte boundaries — 15 tests
- **T5.5** DesktopInfo record + IPC edge cases — 7 tests

### Session 2: Database Safety + Store Gaps (~33 tests)
- **T2.4** DatabaseCore corruption & integrity — 8 tests
- **T5.3** Store edge cases (preview truncation, GUID case, migration pins, etc.) — 25 tests

### Session 3: NoteTab + Model Boundaries (~32 tests)
- **T5.1** NoteTab boundaries (midnight, JustNow, 31-char, whitespace, etc.) — 20 tests
- **T5.5** remaining model edge cases (AppState, WindowGeometry, PendingMove) — 5 tests
- **T2.1** ComGuids.Resolve if not done — 7 tests

### Session 4: Keyboard Router Extraction (~40 tests)
- **T1.1** PreviewKeyDown → KeyAction router — 40 tests

### Session 5: ViewModel + Undo Behavioral (~45 tests)
- **T5.2** ViewModel logical gaps — 25 tests
- **T5.4** UndoManager/Stack behavioral gaps — 20 tests

### Session 6: Search, Deletion, Theme (~35 tests)
- **T1.2** Find engine extraction — 15 tests
- **T1.3** Tab deletion orchestration — 12 tests
- **T2.2** ThemeService resolution — 8 tests

### Session 7: Store Async + VirtualDesktop (~40 tests)
- **T2.7** Store async cancellation paths — 20 tests
- **T3.1** VirtualDesktopService.MatchSessionsAsync — 20 tests

### Session 8: Remaining Items (~30 tests)
- **T1.4** Font size parse & clamp — 8 tests
- **T3.2** App.xaml.cs extractions — 12 tests
- **T2.6** IpcService — 4 tests
- **T2.9** FileDropService edge cases — 3 remaining
- **T2.10** WindowPlacementHelper — 5 tests
- **T1.5** Toast formatting — 5 tests
- **T1.6** Context menu pin state — 2 tests

---

## Session Log

| Session | Date | Items Completed | Tests Added | Total Tests | Coverage |
|---------|------|-----------------|-------------|-------------|----------|
| 0 | 2026-03-09 | baseline | 0 | 302 | 28.5% |
| 1 | 2026-03-09 | T2.1, T2.8, T2.9, T3.3, T5.5, T5.6 | 86 | 849 | ~30% |
| 2 | 2026-03-09 | T2.4, T5.3 | 30 | 879 | ~30% |
| 3 | 2026-03-09 | T5.1 | 29 | 908 | ~30% |
| 4 | 2026-03-09 | T5.2 | 21 | 929 | ~30% |
| 5 | 2026-03-09 | T5.4, T1.4 | 41 | 970 | ~31% |
| 6 | 2026-03-09 | T1.2, T2.2, T2.7, T3.2 | 59 | 1029 | ~32% |
