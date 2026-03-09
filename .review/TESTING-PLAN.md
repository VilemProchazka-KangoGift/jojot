# JoJot Comprehensive Testing Plan

**Created**: 2026-03-09
**Baseline**: 28.5% line coverage (1987/6979 lines covered), 302 tests
**Target**: ~50-60% line coverage
**Report**: `JoJot/coverage260309b.xml`

## Coverage Summary

| Category | Covered | Uncovered | Total | Coverage |
|----------|---------|-----------|-------|----------|
| ViewModels | 306 | 0 | 306 | 100% |
| Models (NoteTab, records) | 141 | 0 | 141 | 100% |
| Data (JoJotDbContext) | 140 | 0 | 140 | 100% |
| Services (fully tested) | 470 | 0 | 470 | 100% |
| Services (partial coverage) | 137 | 188 | 325 | 42% |
| Store async overhead | 793 | 200 | 993 | 80% |
| MainWindow code-behind | 0 | 1777 | 1777 | 0% |
| Controls code-behind | 0 | 573 | 573 | 0% |
| App.xaml.cs | 0 | 678 | 678 | 0% |
| COM Interop | 0 | 259 | 259 | 0% |
| Auto-generated (IPC JSON) | 228 | 62 | 290 | 79% |

## Strategy

Three layers of testing opportunity:
1. **New line coverage** — Extract/test uncovered code (~490 lines)
2. **Edge case / boundary tests** — Harden already-covered code (~120 new tests)
3. **Behavioral integration tests** — Test complex state machines end-to-end (~30 new tests)

---

## Tier 1: Extract & Test Logic from MainWindow (HIGH IMPACT)

### T1.1 — PreviewKeyDown Keyboard Router (~153 lines)
**File**: `Views/MainWindow.Keyboard.cs`
**What**: Complex decision tree with modal guards, escape priority chain (rename → confirmation → help → find bar → cleanup → recovery → preferences), hotkey recording (filters lone modifiers, requires ≥1 modifier), undo/redo, enhanced copy (Ctrl+C with no selection copies entire note), context-dependent Ctrl+F, tab cycling with separator skipping, F2 rename.
**Approach**: Extract `KeyAction ResolveKeyAction(KeyContext ctx)` to ViewModel returning an enum of ~20 actions.
**Est. testable lines**: ~100 | **Est. tests**: ~35-45
**Priority**: HIGH

### T1.2 — Find/Search Engine (~50 lines)
**File**: `Views/MainWindow.Search.cs`
**What**: Case-insensitive substring search with match indexing and wrap-around cycling.
**Approach**: Extract `List<int> FindAllMatches(string content, string query)` and `int CycleIndex(int current, int count, bool forward)`.
**Est. testable lines**: ~30 | **Est. tests**: ~15
**Priority**: MEDIUM

### T1.3 — Tab Deletion Orchestration (~60 lines)
**File**: `Views/MainWindow.TabDeletion.cs`
**What**: Soft-delete with undo toast — `CommitPendingDeletionAsync`, `DeleteTabAsync`, `DeleteMultipleAsync`, `ApplyFocusCascadeAsync`, `UndoDeleteAsync`, `StartDismissTimerAsync`.
**Approach**: Extract deletion state machine to ViewModel or `DeletionManager`.
**Est. testable lines**: ~35 | **Est. tests**: ~12
**Priority**: MEDIUM

### T1.4 — Font Size Clamping & Persistence (~15 lines)
**File**: `Views/MainWindow.Preferences.cs`
**What**: `int.TryParse` + `Math.Clamp(fs, 8, 32)`, default 13. Delta clamping.
**Approach**: Extract `int ParseFontSize(string? saved)` and `int ClampFontSize(int current, int delta)`.
**Est. testable lines**: ~10 | **Est. tests**: ~8
**Priority**: LOW

### T1.5 — Toast Content Formatting (~10 lines)
**File**: `Views/MainWindow.TabDeletion.cs`
**What**: Truncate label to 30 chars + curly quotes; bulk "N notes deleted".
**Est. testable lines**: ~8 | **Est. tests**: ~5
**Priority**: LOW

### T1.6 — Context Menu Pin State (~10 lines extractable)
**File**: `Views/MainWindow.ContextMenu.cs`
**What**: Pin/Unpin text and icon toggle based on `tab.Pinned`.
**Est. testable lines**: ~5 | **Est. tests**: ~2
**Priority**: LOW

---

## Tier 2: Service & Pure Logic Testing (MEDIUM IMPACT)

### T2.1 — ComGuids.Resolve() (~13 lines, pure logic)
**File**: `Interop/ComGuids.cs`
**What**: Build-number dispatch with `SortedDictionary` iteration. Two entries: 22621, 26100.
**Est. testable lines**: ~13 | **Est. tests**: ~7
**Priority**: HIGH — zero refactoring needed

### T2.2 — ThemeService Theme Resolution (~15 testable lines)
**File**: `Services/ThemeService.cs`
**What**: Preference string ↔ AppTheme enum mapping. `DetectSystemTheme` registry read with exception fallback.
**Est. testable lines**: ~12 | **Est. tests**: ~8
**Priority**: MEDIUM

### T2.3 — HotkeyService FormatHotkey Catch Path (~4 lines)
**File**: `Services/HotkeyService.cs`
**What**: `KeyInterop.KeyFromVirtualKey` catch block → hex fallback `0x{vk:X2}`.
**Est. testable lines**: ~4 | **Est. tests**: ~2
**Priority**: HIGH — quick win

### T2.4 — DatabaseCore Corruption & Integrity (~38 lines)
**File**: `Services/DatabaseCore.cs`
**What**: `RunQuickCheckAsync` (PRAGMA quick_check parsing), `HandleCorruptionAsync` (backup + recreate), `OpenAsync` (directory creation, PRAGMA execution).
**Est. testable lines**: ~30 | **Est. tests**: ~8
**Priority**: HIGH — data safety critical

### T2.5 — LogService Remaining (~15 lines)
**File**: `Services/LogService.cs`
**What**: Untested overloads: `Debug<T0,T1>`, `Debug<T0,T1,T2>`, `Warn<T0,T1>`, `Error<T0,T1,T2>`, `ForContext(string, object?)`, `Shutdown`.
**Est. testable lines**: ~15 | **Est. tests**: ~10
**Priority**: LOW

### T2.6 — IpcService (~15 testable lines)
**File**: `Services/IpcService.cs`
**What**: `TryAcquireMutex`, `StopServer` idempotency.
**Est. testable lines**: ~10 | **Est. tests**: ~4
**Priority**: LOW

### T2.7 — Store Async Exception Paths (~156 lines across 38 methods)
**What**: Compiler-generated async state machine catch/finally paths.
**Approach**: Cancellation token tests per store method group.
**Est. testable lines**: ~100 | **Est. tests**: ~20
**Priority**: MEDIUM

### T2.8 — StartupService.EscapeSql (~1 line)
**Est. tests**: ~4 (no quotes, single, multiple, empty)
**Priority**: LOW

### T2.9 — FileDropService Edge Cases (~8 lines)
**What**: Uncovered error paths, byte range boundaries (0x08, 0x0E-0x1A).
**Est. testable lines**: ~8 | **Est. tests**: ~8
**Priority**: LOW

### T2.10 — WindowPlacementHelper (~5 lines)
**What**: `CaptureGeometry` hwnd==IntPtr.Zero fallback, zero-dimension geometry, negative screen coordinates.
**Est. testable lines**: ~5 | **Est. tests**: ~5
**Priority**: LOW

---

## Tier 3: Refactor & Test Complex Logic

### T3.1 — VirtualDesktopService.MatchSessionsAsync (~118 lines)
Three-tier matching algorithm (GUID → Name → Index), orphan detection, fallback mode.
**Approach**: Refactor to accept inputs as parameters.
**Est. testable lines**: ~80 | **Est. tests**: ~20
**Priority**: MEDIUM

### T3.2 — App.xaml.cs Extractable Logic (~30 lines)
Desktop redirect cooldown, pending move resolution, log level parse, IPC routing.
**Est. testable lines**: ~20 | **Est. tests**: ~12
**Priority**: LOW

### T3.3 — PreferencesPanel.FontSizeToPercent (~1 line)
Already `internal static`. `Math.Round(size * 100.0 / 13)`.
**Est. tests**: ~5
**Priority**: LOW

---

## Tier 5: Edge Case & Boundary Tests (100%-COVERED CODE)

These add tests for **already-covered code** where logical gaps exist despite line coverage.

### T5.1 — NoteTab Boundary Cases (~20 tests)
- **FormatRelativeDate midnight**: `dt=23:59:59` vs `now=00:00:01` (across midnight)
- **FormatRelativeTime JustNow threshold**: 59s ("Just now") vs 60s (not) vs 61s (definitely not)
- **DisplayLabel 31 chars**: Off-by-one at truncation boundary (30 exact, 31 truncates)
- **DisplayLabel whitespace-only**: tabs `\t`, newlines `\n`, `\r\n`, mixed whitespace
- **Whitespace-only Name**: `Name = "   "` should fall through to content
- **Content set to null**: After having content, set `Content = null`
- **EditorScrollOffset/CursorPosition**: Property set/get (never tested)
- **SortOrder negative/max**: `SortOrder = -1`, `SortOrder = int.MaxValue`
- **AM/PM time formatting**: 12:00 AM, 12:00 PM, 11:59 PM
- **Year boundary**: `now = 2025-01-01 00:00:00, dt = 2024-12-31 23:59:59`

### T5.2 — ViewModel Logical Gaps (~25 tests)
- **FilteredTabs all filtered out**: Search matches nothing
- **FilteredTabs + tab removal during active search**: Remove a tab while search is active
- **MoveTab single-item list**: Should always return false
- **MoveTab pin-zone exact boundary**: Move pinned tab to `targetIndex == pinnedCount`
- **RemoveMultiple non-contiguous**: Remove tabs [1,3,5] with active=3
- **RemoveMultiple all unpinned**: Remove all unpinned, pinned remain
- **RestoreTabs scattered indexes**: Indexes [0,10,2,8,1] — verify ordering
- **RestoreTabs all beyond end**: All originalIndexes > Tabs.Count
- **GetCleanupCandidates exact boundary**: Tab.UpdatedAt == cutoff (should NOT match, `<` not `<=`)
- **SaveEditorStateToTab null content**: Pass null content parameter
- **SaveEditorStateToTab negative cursor**: CursorPosition = -5
- **SanitizeFilename Unicode**: `"日本語.txt"` — should preserve
- **SanitizeFilename all illegal**: `"<>:\"|?*"` → all replaced with `_`
- **GetNewTabPosition only pinned tabs**: No unpinned — insert at pinnedCount
- **ReorderAfterPinToggle alternating**: [P,U,P,U] → [P,P,U,U]
- **ActiveTab set to tab not in collection**

### T5.3 — Store Edge Cases (~25 tests)
- **NoteStore content truncation boundary**: Preview at 59, 60, 61 chars
- **NoteStore migration pin preservation**: 5+ tabs with interleaved pins
- **NoteStore GetMaxSortOrder with gaps**: Sort orders [0, 5, 10] → returns 10
- **NoteStore UpdateSortOrders negative**: Negative sort order values
- **SessionStore GUID case sensitivity**: `"ABC-123"` vs `"abc-123"` lookup
- **SessionStore delete non-existent**: Delete session that doesn't exist
- **SessionStore UpdateSession to existing GUID**: Potential duplicate
- **PreferenceStore null vs empty**: Distinguish `key not found` from `value=""`
- **PendingMoveStore duplicate pairs**: Same (from, to) inserted twice
- **PendingMoveStore ordering**: Verify insertion-order retrieval
- **GetOrphanedSessionInfo ordering**: Verify deterministic order

### T5.4 — UndoManager/Stack Behavioral Gaps (~20 tests)
- **CollapseOldest two-phase cascade**: Phase 1 alone insufficient, Phase 2 kicks in
- **SetActiveTab then RemoveStack on same ID**: Active becomes stale
- **GetOrCreateStack with tabId=0**: Boundary value
- **PushSnapshot dedup with empty string**: `""` matches current `""`
- **PushSnapshot at exact MaxTier1 (50)**: Boundary eviction
- **PushCheckpoint at exact MaxTier2 (20)**: Boundary overflow
- **ShouldCreateCheckpoint at exactly 5 minutes**: `>=` vs `>` timing boundary
- **EstimatedBytes with emoji/Unicode**: Surrogate pairs in UTF-16
- **CollapseTier1IntoTier2 with empty tier-1**: No-op behavior
- **EvictOldestTier2(0)**: No-op edge case
- **Full 100+ snapshot cycle**: Push 100, undo all, redo all, verify consistency
- **Undo at exact tier boundary**: `currentIndex == _tier2.Count - 1` vs `_tier2.Count`
- **TruncateAfterCurrent at tier boundary**: Truncate when currentIndex is last tier-2 entry

### T5.5 — IPC & Model Edge Cases (~12 tests)
- **IPC unknown discriminator**: `{"action":"unknown"}` → null or throw
- **IPC case sensitivity**: `{"action":"ACTIVATE"}` vs `"activate"`
- **NewTabCommand empty InitialContent**: `""` vs null
- **DesktopInfo record**: Basic constructor, equality, Guid.Empty, null name, negative index
- **AppState negative coordinates**: `WindowLeft = -100` (multi-monitor)
- **AppState zero dimensions**: `WindowWidth = 0`
- **WindowGeometry negative/zero**: Negative coords, zero width/height
- **PendingMove same From/To**: `FromDesktop == ToDesktop`

### T5.6 — HotkeyService & FileDropService Gaps (~15 tests)
- **FormatHotkey all modifier combos**: Ctrl, Shift, Ctrl+Alt, Ctrl+Shift, Alt+Shift, Win+Ctrl, Win+Alt, Win+Shift, all four
- **FormatHotkey invalid VK**: 0x00, 0xFF, 0xFFF → hex fallback
- **ModifierKeysToWin32 with None**: Returns 0
- **IsBinaryContent byte boundaries**: 0x08 exact, 0x0E-0x1A range, 0x20 (space)
- **ValidateFileAsync empty file**: FileInfo.Length == 0
- **ValidateFileAsync exact 8KB**: Boundary between small/large read

---

## Tier 4: Not Worth Unit Testing (SKIP)

| Item | Lines | Reason |
|------|-------|--------|
| VirtualDesktopInterop (COM) | 179 | COM server required |
| VirtualDesktopNotificationListener | 80 | COM callbacks |
| Controls code-behind (6 controls) | 573 | Pure WPF UI |
| MainWindow UI-only methods | ~800 | Animations, visual tree, focus |
| IpcMessageContext (auto-gen JSON) | 62 | Source-generated |
| App.OnAppStartup (full sequence) | 136 | Integration-only |
| DispatcherDebounceTimer | 5 | WPF timer wrapper |
| WindowActivationHelper | 23 | Win32 P/Invoke |

---

## Estimated Impact Summary

| Tier | Type | New Lines | New Tests |
|------|------|-----------|-----------|
| T1 (Extract from MainWindow) | Line coverage | ~190 | ~65-80 |
| T2 (Service tests) | Line coverage | ~200 | ~65-75 |
| T3 (Refactor complex logic) | Line coverage | ~100 | ~35-40 |
| T5 (Edge cases on covered code) | Behavioral | ~0 | ~115-120 |
| **Total** | | **~490 new lines** | **~280-315 new tests** |

**Projected coverage**: ~(1987+490)/6979 = **~35.5%** line coverage
**Projected test count**: 302 + ~280 = **~580 tests**

The line coverage ceiling is ~35-40% for unit tests due to WPF/COM architecture. The T5 edge case tests don't increase line coverage but significantly improve correctness confidence.

## Session Workflow

Each session should:
1. Pick next uncompleted item(s) from TESTING-PROGRESS.md
2. Extract logic (if T1/T3) or write tests directly (if T2/T5)
3. Run `dotnet test JoJot.Tests/JoJot.Tests.csproj`
4. Update TESTING-PROGRESS.md with test counts and status
