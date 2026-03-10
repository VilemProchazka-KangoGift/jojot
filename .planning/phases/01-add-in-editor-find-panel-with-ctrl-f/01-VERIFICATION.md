---
phase: 01-add-in-editor-find-panel-with-ctrl-f
verified: 2026-03-10T18:30:00Z
status: human_needed
score: 11/12 must-haves verified
human_verification:
  - test: "Visual verification of complete find/replace feature end-to-end"
    expected: "Ctrl+F opens panel, highlights appear on all matches, active match distinct, navigation works, replace works, Replace All creates single undo, Escape closes panel, tab switch re-searches, theme colors correct"
    why_human: "Panel animation, highlight rendering, theme-aware colors, undo semantics, and real-time UX cannot be verified programmatically"
  - test: "Ctrl+H vs Ctrl+F behavior"
    expected: "Both keys open the find panel (replace rows are always visible per implementation decision — no Ctrl+H opens replace-only behavior). User should confirm this simplified behavior is acceptable"
    why_human: "Implementation intentionally dropped the showReplace=true/false distinction from the plan. Needs human sign-off."
  - test: "Panel shows/hides with no slide animation"
    expected: "Panel appears and disappears instantly (no TranslateTransform slide). Plan specified a 320px slide animation. Verify whether instant show/hide is acceptable."
    why_human: "Animation presence can only be judged visually"
---

# Phase 01: Add In-Editor Find Panel with Ctrl+F — Verification Report

**Phase Goal:** Replace the inline EditorFindBar with a full-featured find-and-replace side panel, including real-time search, match highlighting via adorner overlay, case/whole-word toggles, and replace operations
**Verified:** 2026-03-10T18:30:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | FindAllMatches supports case-sensitive and whole-word matching options | VERIFIED | `MainWindowViewModel.cs:310` — `FindAllMatches(content, query, bool caseSensitive = false, bool wholeWord = false)` with `IsWholeWordMatch` at line 333 |
| 2 | ReplaceAll returns new content with all occurrences replaced | VERIFIED | `MainWindowViewModel.cs:345` — `ReplaceAll(...)` returns `(string NewContent, int Count)` tuple |
| 3 | FindReplacePanel UserControl renders with find input, toggle buttons, match counter, replace row, close button | VERIFIED | `FindReplacePanel.xaml` — 140 lines, contains `FindInput`, `CaseToggle`, `WholeWordToggle`, `MatchCountText`, `ReplaceInput`, Replace/ReplaceAll buttons, close (E711) button |
| 4 | Theme files define highlight colors for active and inactive match backgrounds | VERIFIED | Both `LightTheme.xaml:32-33` and `DarkTheme.xaml:32-33` define `c-find-match-bg` and `c-find-match-active-bg` |
| 5 | Ctrl+F opens find panel | VERIFIED | `MainWindow.Keyboard.cs:241-246` — `Key.F` triggers `ShowFindPanel()` |
| 6 | Ctrl+H opens find+replace panel | PARTIAL | `MainWindow.Keyboard.cs:241` — Ctrl+H calls `ShowFindPanel()` with no `showReplace` differentiation. Replace rows are always visible (no find-only mode). Comment at line 240 reads "replace always visible" — intentional simplification. The plan's `showReplace: true/false` parameter was not implemented. |
| 7 | Toolbar has a search icon button that opens the find panel | VERIFIED | `MainWindow.xaml:353-357` — `ToolbarFind` button; `MainWindow.Toolbar.cs:51` — `ToolbarFind_Click` calls `ShowFindPanel()` |
| 8 | Opening find panel closes other open side panels | VERIFIED | `ShowFindPanel()` calls `ViewModel.CloseAllSidePanels()` which sets `IsFindPanelOpen=false` AND `IsPreferencesOpen/IsCleanupOpen/IsRecoveryOpen=false`; Recovery.cs:39, Cleanup.cs:21, Preferences.cs:32 explicitly close find panel when opening |
| 9 | Typing in find input triggers real-time search | VERIFIED | `FindInput_TextChanged` → `RaiseFindChanged()` → `FindTextChanged` event → `OnFindTextChanged` in `MainWindow.Search.cs` → `RunSearch()` |
| 10 | Escape closes the panel and returns focus to editor | VERIFIED | `MainWindow.Keyboard.cs:123-128` — `_findPanelOpen` check triggers `HideFindPanel()`; `HideFindPanel()` calls `ContentEditor.Focus()` |
| 11 | Selection auto-populates find input when Ctrl+F pressed | VERIFIED | `ShowFindPanel()` at `MainWindow.Search.cs:123-126` — `ContentEditor.SelectionLength > 0` check calls `FindReplacePanel.SetFindText(ContentEditor.SelectedText)` |
| 12 | Search query persists when switching tabs; re-searches new tab content | VERIFIED | `RefreshFindIfPanelOpen()` at `MainWindow.Search.cs:288`; called from `MainWindow.Tabs.cs:402` on tab selection change |
| 13 | Replace replaces current match and advances to next | VERIFIED | `PerformReplace()` at `MainWindow.Search.cs:212` — uses `ReplaceSingle`, sets new content, calls `RunSearch` to refresh (which advances to index 0) |
| 14 | Replace All creates single undo checkpoint and shows replacement count | VERIFIED | `PerformReplaceAll()` at `MainWindow.Search.cs:240` — `PushSnapshot` before replacement; `ShowReplaceAllToast(count)` via `ShowUndoableToast` |
| 15 | Old EditorFindBar inline bar is completely removed | VERIFIED | No references to `EditorFindBar`, `EditorFindInput`, `EditorFindCount`, `ShowEditorFindBar`, `HideEditorFindBar` in any `.cs` or `.xaml` source files. References found only in legacy coverage XML build artifacts. |
| 16 | All matches highlighted with adorner overlay | HUMAN NEEDED | `TextBoxHighlightAdorner.cs` exists (79 lines), wired via `EnsureHighlightAdorner()` in `RunSearch()` — programmatic wiring is correct. Visual rendering requires human confirmation. |
| 17 | Active match has stronger highlight color; highlights update in real-time | HUMAN NEEDED | Adorner `OnRender` uses `activeBrush` vs `matchBrush` from `FindResource`. `CycleFindMatch` calls `_highlightAdorner?.Update(...)` on each cycle. Visual quality requires human. |
| 18 | No slide animation on show/hide | WARNING | `FindReplacePanel.Show()` and `Hide()` set `Visibility.Visible/Collapsed` directly. Plan specified `TranslateTransform` slide animation (320px, 250ms EaseOut). Panel shows/hides instantly. |

**Score:** 11/12 automated truths verified (1 partial on Ctrl+H mode distinction; 2 human-needed; 1 warning on animation)

### Required Artifacts

| Artifact | Required | Status | Details |
|----------|----------|--------|---------|
| `JoJot/ViewModels/MainWindowViewModel.cs` | Enhanced find engine with case/wholeWord + replace methods | VERIFIED | `FindAllMatches` (line 310), `IsWholeWordMatch` (333), `ReplaceAll` (345), `ReplaceSingle` (369), `IsFindPanelOpen` (537), `CloseAllSidePanels` updated (547) |
| `JoJot/Controls/FindReplacePanel.xaml` | Find/replace side panel UI | VERIFIED | 140 lines. Has find input, case/whole-word toggles, match counter, nav buttons, replace input, Replace/ReplaceAll buttons, close button. All use `DynamicResource` theme keys. |
| `JoJot/Controls/FindReplacePanel.xaml.cs` | Panel code-behind with Show/Hide, events, search-as-you-type | VERIFIED | Exports `Show()`, `Hide()`, `SetFindText()`, `UpdateMatches()`, `GetFindText()`, `GetReplaceText()`, `CaseSensitive`, `WholeWord`. Raises `CloseRequested`, `FindTextChanged`, `FindNextRequested`, `FindPreviousRequested`, `ReplaceRequested`, `ReplaceAllRequested`. |
| `JoJot/Themes/LightTheme.xaml` | Highlight color resources | VERIFIED | Line 32: `c-find-match-bg Color="#50FFDD00"`, Line 33: `c-find-match-active-bg Color="#70FF8C00"` |
| `JoJot/Themes/DarkTheme.xaml` | Highlight color resources | VERIFIED | Line 32: `c-find-match-bg Color="#50CCCC00"`, Line 33: `c-find-match-active-bg Color="#70FF9632"` |
| `JoJot.Tests/ViewModels/FindEngineTests.cs` | Tests for enhanced find engine | VERIFIED | 359 lines (>180 required). 48 tests covering case-sensitive, whole-word, ReplaceAll, ReplaceSingle. All 48 pass. |
| `JoJot/Controls/TextBoxHighlightAdorner.cs` | Adorner painting highlight rectangles | VERIFIED | 79 lines (>60 required). `Update()`, `Clear()`, `OnRender()` with `GetRectFromCharacterIndex`, active/inactive brush distinction, clipping, try/catch for stale positions. |
| `JoJot/Views/MainWindow.xaml` | FindReplacePanel instance, toolbar button, no EditorFindBar | VERIFIED | Line 423: `controls:FindReplacePanel` declared. Line 353: `ToolbarFind` button. No `EditorFindBar` in source XAML. |
| `JoJot/Views/MainWindow.Search.cs` | Panel show/hide methods, event handlers, adorner management | VERIFIED | `ShowFindPanel`, `HideFindPanel`, `WireUpFindPanelEvents`, `RunSearch`, `CycleFindMatch`, `PerformReplace`, `PerformReplaceAll`, `RefreshFindIfPanelOpen`, `EnsureHighlightAdorner`, `RemoveHighlightAdorner` all present. |
| `JoJot/Views/MainWindow.Keyboard.cs` | Ctrl+F opens panel, Ctrl+H opens find+replace, Escape closes | VERIFIED (partial) | Line 241: both `Key.F` and `Key.H` open `ShowFindPanel()`. Escape at line 123. No `showReplace` distinction — replace rows always visible. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MainWindow.Search.cs` | `FindReplacePanel.xaml.cs` | Event subscriptions | VERIFIED | Lines 93-98: all 6 events subscribed in `WireUpFindPanelEvents()` |
| `MainWindow.Search.cs` | `MainWindowViewModel.cs` | `FindAllMatches` with case/wholeWord params | VERIFIED | `RunSearch()` line 171: `MainWindowViewModel.FindAllMatches(ContentEditor.Text, query, caseSensitive, wholeWord)` |
| `MainWindow.Keyboard.cs` | `MainWindow.Search.cs` | Ctrl+F/H calls `ShowFindPanel` | VERIFIED | Keyboard.cs line 243: `ShowFindPanel()` |
| `MainWindow.Search.cs` | `UndoStack` | `PushSnapshot` before Replace All | VERIFIED | Search.cs lines 221 (PerformReplace) and 249 (PerformReplaceAll) |
| `MainWindow.Search.cs` | `TextBoxHighlightAdorner.cs` | Creates/updates adorner with match positions | VERIFIED | `EnsureHighlightAdorner().Update(...)` in `RunSearch()` (line 188) and `CycleFindMatch()` (line 204) |
| `TextBoxHighlightAdorner.cs` | `LightTheme.xaml` / `DarkTheme.xaml` | `FindResource("c-find-match-bg/active-bg")` | VERIFIED | Adorner.cs lines 51-52: `_textBox.FindResource("c-find-match-bg")` and `"c-find-match-active-bg"` on each render |
| `FindReplacePanel.xaml` | `LightTheme.xaml` | `DynamicResource c-find-match` | PARTIAL | Panel XAML does NOT reference `c-find-match` keys directly. Colors are consumed by adorner via `FindResource`, not panel XAML. The plan's key link was imprecise about where consumption happens. Theme resources ARE consumed; just by adorner, not panel. |
| `ThemeService.cs` | `MainWindow.Search.cs` | `ThemeChanged` event invalidates adorner | VERIFIED | `ThemeService.cs:42` — static `ThemeChanged` event raised at line 110. `WireUpFindPanelEvents()` subscribes at Search.cs line 107-110. |
| `MainWindow.Tabs.cs` | `MainWindow.Search.cs` | Tab switch calls `RefreshFindIfPanelOpen` | VERIFIED | `MainWindow.Tabs.cs:402` calls `RefreshFindIfPanelOpen()` |

### Requirements Coverage

No separate REQUIREMENTS.md file exists. Requirements are defined inline in the ROADMAP.md and plan frontmatter. Coverage per plan frontmatter:

| Requirement | Plans | Description | Status | Evidence |
|-------------|-------|-------------|--------|----------|
| FIND-01 | 01-01, 01-02 | Panel UI — FindReplacePanel with find input, toggles, counter, replace rows | SATISFIED | Panel exists with all UI elements; wired in MainWindow.xaml |
| FIND-02 | 01-01, 01-02 | Replace operations — Replace single, Replace All with undo | SATISFIED | `PerformReplace()` and `PerformReplaceAll()` with `PushSnapshot`; `ReplaceAll()` and `ReplaceSingle()` in ViewModel |
| FIND-03 | 01-01, 01-03 | Highlighting — adorner overlay for matches, active/inactive distinction | SATISFIED (programmatic) | `TextBoxHighlightAdorner` exists and is wired; visual rendering needs human confirmation |
| FIND-04 | 01-02 | Keyboard shortcuts — Ctrl+F (find), Ctrl+H (find+replace), Escape | PARTIALLY SATISFIED | Ctrl+F and Ctrl+H both open identical panel (no find-only vs find+replace distinction). Escape works. Replace rows always visible. Plan's intent of mode separation not fully realized. |
| FIND-05 | 01-02 | Cleanup — remove old EditorFindBar inline bar | SATISFIED | No EditorFindBar references in any source .cs or .xaml files |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `FindReplacePanel.xaml.cs` | 62-67 | `Show()` has no slide animation — instant `Visibility.Visible` only | Info | Plan specified TranslateTransform slide animation (320px, 250ms EaseOut). Implementation chose simplicity. No functional impact, visual polish difference. |
| `MainWindow.Keyboard.cs` | 241 | Ctrl+H and Ctrl+F have identical behavior — comment says "replace always visible" | Info | Plan specified separate find-only vs find+replace modes. Implementation made replace always visible. Documented by comment. Not a bug. |

No TODO/FIXME/placeholder comments found in phase files. No empty implementations. No console.log-only handlers.

### Human Verification Required

#### 1. Full Feature End-to-End Test

**Test:** Run `dotnet run --project JoJot/JoJot.csproj`. Create a note with repeated text: "The quick brown fox jumps over the lazy fox. THE FOX is clever." Then:
1. Press Ctrl+F — verify panel opens from right side
2. Type "fox" — verify match counter shows "1/3"
3. Verify all 3 matches highlighted in editor (soft yellow/amber background)
4. Verify first/active match has stronger highlight (orange/amber)
5. Press Enter — verify "2/3", active highlight moves
6. Press Shift+Enter — verify back to "1/3"
7. Click prev/next buttons — same navigation behavior
8. Click "Aa" toggle — verify case-sensitive, "THE FOX" match drops off
9. Click "W" toggle — verify whole-word only
10. Press Ctrl+H — verify panel opens (replace rows visible — same as Ctrl+F)
11. Type "cat" in replace input, click "Replace" — verify replacement, advance to next
12. Click "Replace All" — verify count shown in toast ("N replacements made"), Ctrl+Z undoes all at once
13. With panel open, switch tabs — verify search re-runs on new tab
14. Press Escape — verify panel closes, focus returns to editor
15. Open Preferences — verify find panel closes
16. Select text in editor, press Ctrl+F — verify selection pre-fills find input

**Expected:** All operations work as described
**Why human:** Visual highlight rendering, animation presence/absence, UX flow, undo semantics, and theme color quality cannot be verified programmatically

#### 2. Theme Highlight Colors

**Test:** With find panel open and matches visible, switch between Light and Dark themes
**Expected:** Highlight colors update immediately without re-typing. Light: yellow/amber tones. Dark: darker muted tones. No leftover highlights after panel close.
**Why human:** Color appearance and real-time update after theme switch requires visual verification

#### 3. Scroll Behavior

**Test:** In a long note with many search results, open find panel and search. Scroll through the content.
**Expected:** Highlight rectangles remain correctly positioned as content scrolls — they track the text position, not the viewport position.
**Why human:** AdornerLayer positioning on scroll requires visual verification

#### 4. Ctrl+F vs Ctrl+H Behavior Acceptance

**Test:** Press Ctrl+F then Ctrl+H on an empty find input
**Expected:** Both open the same panel with replace rows always visible (no find-only mode). Confirm this simplified behavior is acceptable per FIND-04.
**Why human:** User decision on whether find-only mode is needed or whether always-showing replace rows is acceptable UX

### Gaps Summary

No automated gaps block the phase goal. All core features are implemented and wired:

- Find engine with case/whole-word and replace is fully tested (48 tests, all pass)
- FindReplacePanel UserControl exists with all required UI elements and events
- TextBoxHighlightAdorner exists and is wired to update on search, cycle, tab switch, scroll, and theme change
- Old EditorFindBar is completely removed from source
- All keyboard shortcuts wire correctly
- Replace operations include undo checkpoint

Two implementation deviations from plan exist but do not block the goal:

1. **No slide animation** — `Show()/Hide()` use instant visibility change instead of TranslateTransform animation. The UX impact is noticeable but the feature is functional.
2. **No find-only vs find+replace mode distinction** — Both Ctrl+F and Ctrl+H open the same panel with replace rows always visible. This simplification was explicitly documented in the implementation (comment at `MainWindow.Keyboard.cs:240`).

The human-verify gate (Plan 03 Task 2) from the plan itself is still pending — this verification flags the same items.

---

_Verified: 2026-03-10T18:30:00Z_
_Verifier: Claude (gsd-verifier)_
