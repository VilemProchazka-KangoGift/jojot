# MainWindow MVVM Migration Plan

## Status: COMPLETE
**All Phases (0-9):** COMPLETE (302 tests passing)
**Last Updated:** 2026-03-09

---

## Overview

Migrate MainWindow's 16 partial classes (~4,578 lines of code-behind) to MVVM pattern incrementally. Each phase produces a compiling, working app + new tests. No MVVM framework — hand-rolled ObservableObject + RelayCommand.

### Principles
1. Incremental — each phase compiles and runs
2. Extract, don't rewrite — move logic to ViewModels, leave thin code-behind stubs
3. Test the ViewModel — unit tests target ViewModel logic
4. No MVVM framework — minimal hand-rolled infrastructure
5. Code-behind survives for animations/visual tree/drag visuals (genuine View concerns)

---

## Phase 0: MVVM Foundation
**Status:** NOT STARTED
**Files to create:**
- `JoJot/ViewModels/ObservableObject.cs` — INotifyPropertyChanged base with SetProperty<T>
- `JoJot/ViewModels/RelayCommand.cs` — ICommand (sync), AsyncRelayCommand (async)
- `JoJot/ViewModels/RelayCommand{T}.cs` — generic typed version

**Tests to create:**
- `JoJot.Tests/ViewModels/ObservableObjectTests.cs`
- `JoJot.Tests/ViewModels/RelayCommandTests.cs`

**Risk:** None. Pure additive.

---

## Phase 1: NoteTab → Observable Model
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/Models/NoteTab.cs` — implement INotifyPropertyChanged, SetProperty on UI-affecting props

**What changes:**
- Properties that affect UI (Name, Content, Pinned, UpdatedAt, SortOrder) use SetProperty
- DisplayLabel, IsPlaceholder, UpdatedDisplay raise dependent notifications
- Remove no-op RefreshDisplayProperties()
- All callers of RefreshDisplayProperties() cleaned up

**Tests to create:**
- `JoJot.Tests/Models/NoteTabObservableTests.cs` — property change fires, dependent notifications

**Regression:** Existing NoteTabTests + NoteTabModelTests must still pass.

---

## Phase 2: MainWindowViewModel — Core State
**Status:** NOT STARTED
**Files to create:**
- `JoJot/ViewModels/MainWindowViewModel.cs`

**Files to modify:**
- `JoJot/MainWindow.xaml.cs` — add ViewModel property, redirect _tabs/_activeTab reads

**ViewModel contains:**
- `ObservableCollection<NoteTab> Tabs` (from MainWindow._tabs)
- `NoteTab? ActiveTab` (from _activeTab)
- `string DesktopGuid` (from _desktopGuid)
- `string SearchText` (from _searchText)
- `IReadOnlyList<NoteTab> FilteredTabs` (computed)
- `string WindowTitle` (computed from desktop info)

**Tests to create:**
- `JoJot.Tests/ViewModels/MainWindowViewModelTests.cs`
  - ActiveTab change raises PropertyChanged
  - FilteredTabs recomputes on SearchText change
  - FilteredTabs excludes non-matching tabs
  - WindowTitle formats correctly (name / index / fallback)

**Migration note:** Code-behind still does everything else — this centralizes state only. Tab selection handler sets ViewModel.ActiveTab.

---

## Phase 3: Tab CRUD Commands
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/ViewModels/MainWindowViewModel.cs` — add commands
- `JoJot/MainWindow.Tabs.cs` — delegate to ViewModel
- `JoJot/MainWindow.TabDeletion.cs` — delegate to ViewModel

**ViewModel gets:**
- CreateNewTabCommand (async)
- DeleteTabCommand / DeleteActiveTabCommand
- TogglePinCommand
- CloneTabCommand
- Focus cascade logic (GetNextTabAfterDeletion)

**Tests:**
- CreateNewTab adds to collection, sets ActiveTab, correct DesktopGuid
- DeleteTab removes, focus cascades to correct neighbor
- DeleteTab on pinned: cascades within pinned group first
- TogglePin flips + re-sorts
- CloneTab duplicates, inserts after original
- DeleteTab single remaining → creates new empty tab

**Migration note:** Code-behind handlers become one-liners. Toast/undo UI stays code-behind.

---

## Phase 4: Tab Reorder Logic
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/ViewModels/MainWindowViewModel.cs` — add MoveTab
- `JoJot/MainWindow.TabDrag.cs` — call ViewModel.MoveTab in CompleteDrag

**ViewModel gets:**
- `MoveTab(NoteTab tab, int newIndex)` — reorders + updates SortOrder
- Pin-zone enforcement

**Tests:**
- MoveTab updates SortOrder for all affected tabs
- MoveTab respects pin boundaries
- Same-position = no-op
- Cross-boundary move rejected

**Migration note:** Visual feedback (drop indicator, opacity, mouse capture) stays code-behind.

---

## Phase 5: Editor State & Undo/Redo Commands
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/ViewModels/MainWindowViewModel.cs`
- `JoJot/MainWindow.xaml.cs` — remove PerformUndo/PerformRedo, sync EditorContent

**ViewModel gets:**
- `string EditorContent` — synced with editor text
- `int CursorPosition`, `int ScrollOffset`
- `UndoCommand`, `RedoCommand`, `SaveAsCommand`
- `bool CanUndo`, `bool CanRedo`
- `bool IsRestoringContent` (replaces _suppressTextChanged)
- SaveCurrentTabContent() and RestoreTabContent() logic

**Tests:**
- Undo/Redo delegate to UndoManager
- CanUndo/CanRedo reflect stack state
- Tab switch saves content + cursor, restores new tab
- EditorContent change marks dirty

---

## Phase 6: Panel State
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/ViewModels/MainWindowViewModel.cs`
- `JoJot/MainWindow.Preferences.cs`, `MainWindow.Cleanup.cs`, `MainWindow.Recovery.cs`, `MainWindow.Help.cs`

**ViewModel gets:**
- `IsPreferencesOpen`, `IsCleanupOpen`, `IsRecoveryOpen`, `IsHelpOpen` + toggle commands
- Cleanup logic: GetCleanupCandidates, GetCleanupCutoffDate, DeleteCleanupCandidatesAsync
- Recovery logic: GetOrphanedSessions, AdoptSessionAsync, DeleteSessionAsync

**Tests:**
- Cleanup cutoff dates correct per filter
- Cleanup candidates filter by age
- Recovery identifies orphaned GUIDs
- Panel toggles flip state

**Migration note:** Slide animations + row building stay code-behind.

---

## Phase 7: Desktop Drag Logic
**Status:** NOT STARTED
**Files to modify:**
- `JoJot/ViewModels/MainWindowViewModel.cs`
- `JoJot/MainWindow.DesktopDrag.cs`

**ViewModel gets:**
- DragFromDesktopGuid, DragToDesktopGuid, DragToDesktopName
- IsMisplaced (observable)
- KeepHereAsync, MergeToDesktopAsync, CancelDragAsync

**Tests:**
- KeepHere updates DesktopGuid in DB
- Merge transfers tabs to target
- Cancel restores original state
- IsMisplaced computed correctly

---

## Phase 8: XAML Data Templates
**Status:** COMPLETE
**Files modified:** NoteTab.cs, MainWindow.xaml, MainWindow.xaml.cs, MainWindow.Tabs.cs, MainWindow.Rename.cs
**Result:** ~240 C# lines removed, ~90 XAML lines added. UpdateTabItemDisplay eliminated. Data bindings handle all display updates automatically.
**Tests:** 4 new tests (302 total).

---

## Phase 9: Keyboard Shortcuts → InputBindings
**Status:** COMPLETE
**Files modified:** MainWindow.Keyboard.cs, MainWindow.xaml.cs
**Result:** 9 ICommand properties + 12 InputBindings. PreviewKeyDown reduced by ~90 lines. Guards and complex/context-dependent shortcuts remain in PreviewKeyDown.
**Tests:** Existing command tests cover logic.

---

## Dependency Graph

```
Phase 0 ──→ Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4
                            │
                            ├──→ Phase 5 (Editor)
                            ├──→ Phase 6 (Panels)
                            └──→ Phase 7 (Desktop)
                                    │
                        Phases 1-7 done
                                    │
                            Phase 8 (Templates)
                                    │
                            Phase 9 (Keybindings)
```

Phases 3-7 can be done in any order after Phase 2. Phases 8-9 are final cleanup.

## Expected Impact

| Metric | Before | After |
|--------|--------|-------|
| MainWindow code-behind | ~4,578 lines | ~1,500-2,000 lines |
| Testable ViewModel logic | 0 lines | ~1,500 lines |
| Test count | 126 | ~300+ |
| Code-behind concerns | Everything | Animations, visual tree, drag visuals, Dispatcher |

## What Stays in Code-Behind (by design)
- Toast slide-in/out animations
- Drop indicator visuals during tab drag
- Mouse capture management
- Visual tree helpers (FindDescendant)
- Dispatcher.Invoke for COM callbacks
- Panel slide animations
- File dialog (SaveFileDialog)
- Hamburger menu popup behavior
