# MVVM Migration Progress Log

## How to Resume
When starting a new session, read this file and `MVVM-PLAN.md` to understand where we left off. The current phase and last completed step are always listed below.

**Current Phase:** 8 — Not started
**Last Completed Phase:** 7
**Build status:** Clean — 298 tests passing (0 failures)
**Test count progression:** 126 → 155 → 173 → 199 → 226 → 237 → 253 → 280 → 298

---

## Completed Work Summary

### Phase 0: MVVM Foundation — DONE
- Created `JoJot/ViewModels/ObservableObject.cs` — INotifyPropertyChanged base with SetProperty<T> + dependent property overload
- Created `JoJot/ViewModels/RelayCommand.cs` — RelayCommand + RelayCommand<T>
- Created `JoJot/ViewModels/AsyncRelayCommand.cs` — AsyncRelayCommand + AsyncRelayCommand<T> (re-entrance guarded)
- Tests: `ObservableObjectTests.cs` (9 tests), `RelayCommandTests.cs` (20 tests)

### Phase 1: NoteTab → Observable Model — DONE
- `JoJot/Models/NoteTab.cs` — now inherits ObservableObject
- SetProperty on: Name, Content, Pinned, UpdatedAt, SortOrder
- Dependent notifications: Name→DisplayLabel+IsPlaceholder, Content→DisplayLabel+IsPlaceholder, UpdatedAt→UpdatedDisplay
- Removed RefreshDisplayProperties() (was no-op, zero callers in code)
- Tests: `NoteTabObservableTests.cs` (18 tests)
- All existing NoteTab tests still pass

### Phase 2: MainWindowViewModel — Core State — DONE
- Created `JoJot/ViewModels/MainWindowViewModel.cs` with:
  - `ObservableCollection<NoteTab> Tabs`, `NoteTab? ActiveTab`, `string DesktopGuid`, `string SearchText`
  - `IReadOnlyList<NoteTab> FilteredTabs` (recomputes on SearchText change or Tabs collection change)
  - `string WindowTitle` + `UpdateDesktopInfo()` + `FormatWindowTitle()` static
  - `MatchesSearch()` extracted from MainWindow.Search.cs
- `MainWindow.xaml.cs` updated:
  - Added `internal MainWindowViewModel ViewModel` property
  - Old fields (_tabs, _activeTab, _searchText, _desktopGuid) → forwarding properties to ViewModel
  - Constructor creates ViewModel first
  - `UpdateDesktopTitle()` delegates to ViewModel
  - `DesktopGuid` public property reads from ViewModel
- `MainWindow.Search.cs` — `MatchesSearch()` delegates to ViewModel
- Tests: `MainWindowViewModelTests.cs` (26 tests)

### Phase 3: Tab CRUD Commands — DONE
- ViewModel methods added:
  - `GetNewTabPosition()` — finds insert index + sort order, detects reusable placeholder
  - `InsertNewTab()` — adds tab at correct collection position
  - `RemoveTab()` — removes tab, returns focus cascade target
  - `RemoveMultiple()` — bulk remove (skips pinned), returns focus target
  - `RestoreTabs()` — undo-delete restoration at original indexes
  - `GetFocusCascadeTarget()` — pure logic for post-deletion focus selection
  - `ReorderAfterPinToggle()` — re-sorts collection, reassigns SortOrder
  - `GetClonePosition()` — calculates clone insert position + shifts sort orders
- Code-behind updated to delegate:
  - `MainWindow.Tabs.cs`: CreateNewTabAsync, TogglePinAsync, CloneTabAsync
  - `MainWindow.TabDeletion.cs`: DeleteTabAsync, DeleteMultipleAsync, UndoDeleteAsync, ApplyFocusCascadeAsync
  - `MainWindow.Cleanup.cs`: focus cascade call site
- Tests: `TabCrudTests.cs` (27 tests)

### Phase 4: Tab Reorder Logic — DONE
- ViewModel method: `MoveTab(int oldIndex, int newIndex)` — reorders collection with pin-zone enforcement, reassigns SortOrder
- `MainWindow.TabDrag.cs` CompleteDrag() calls ViewModel.MoveTab()
- Tests: `TabReorderTests.cs` (11 tests)

### Phase 5: Editor State — DONE
- ViewModel additions:
  - `IsRestoringContent` property (replaces _suppressTextChanged field)
  - `SaveEditorStateToTab(content, caretIndex, scrollOffset)` — saves editor state to active tab model
  - `GetDefaultFilename()` + `SanitizeFilename()` — static, moved from MainWindow.xaml.cs
- Code-behind updated:
  - `_suppressTextChanged` → forwarding property to ViewModel.IsRestoringContent
  - `SaveCurrentTabContent()` uses ViewModel.SaveEditorStateToTab()
  - `SaveAsTxt()` uses MainWindowViewModel.GetDefaultFilename()
  - Removed duplicate GetDefaultFilename/SanitizeFilename from MainWindow.xaml.cs
- Tests: `EditorStateTests.cs` (16 tests)

### Phase 6: Panel State — DONE
- ViewModel additions:
  - `IsPreferencesOpen`, `IsCleanupOpen`, `IsRecoveryOpen`, `IsHelpOpen` — observable panel state
  - `CloseAllSidePanels()` — mutual exclusion helper (excludes help overlay)
  - `GetCleanupCutoffDate(age, unitIndex, now)` — static, testable cutoff computation
  - `GetCleanupCandidates(cutoff, includePinned)` — filters Tabs by age + pin state
  - `GetCleanupExcerpt(tab)` — static, moved from code-behind
- Code-behind updated:
  - `MainWindow.xaml.cs`: `_preferencesOpen`, `_cleanupPanelOpen`, `_recoveryPanelOpen` → forwarding properties to ViewModel
  - `MainWindow.Cleanup.cs`: `GetCleanupCutoffDate()` parses UI then delegates to ViewModel static; `GetCleanupCandidates()` delegates to ViewModel; `GetCleanupExcerpt()` delegates to ViewModel
  - `MainWindow.Help.cs`: Show/HideHelpOverlay set `ViewModel.IsHelpOpen`
  - `MainWindow.Keyboard.cs`: help overlay checks use `ViewModel.IsHelpOpen` instead of `HelpOverlay.Visibility`
- Tests: `PanelStateTests.cs` (27 tests)
- Note: Recovery panel logic is heavily UI-bound (building FrameworkElements, DB calls). The panel open state is extracted; adopt/delete actions remain in code-behind as thin DB wrappers.

### Phase 7: Desktop Drag Logic — DONE
- ViewModel additions:
  - `IsDragOverlayActive`, `DragFromDesktopGuid`, `DragToDesktopGuid`, `DragToDesktopName`, `IsMisplaced` — observable state
  - `DragAction` enum (`Dismiss`, `NoOp`, `UpdateTarget`, `ShowNew`)
  - `EvaluateDrag(toGuid)` — pure state machine logic for re-entry handling
  - `BeginDrag(from, to, name)` — initial drag state setup
  - `UpdateDragTarget(to, name)` — update target during active drag
  - `ResetDragState()` — clear all drag state after resolution
  - `IsMisplacedOnDesktop(currentGuid)` — pure desktop mismatch check
- Code-behind updated:
  - `MainWindow.xaml.cs`: 5 drag fields → forwarding properties to ViewModel
  - `MainWindow.DesktopDrag.cs`: ShowDragOverlayAsync uses EvaluateDrag switch; HideDragOverlayAsync uses ResetDragState; merge click uses ResetDragState; misplaced check uses IsMisplacedOnDesktop
- Tests: `DesktopDragTests.cs` (18 tests)
- Note: KeepHere/Merge/Cancel actions remain in code-behind — they orchestrate DB, COM, App, and UI operations. State machine extracted to ViewModel for testability.

---

## Remaining Phases

### Phase 8: XAML Data Templates — NOT STARTED (detailed plan below)
Replace code-behind tab item visual tree (CreateTabListItem/UpdateTabItemDisplay) with XAML DataTemplates.

**Implementation plan (from code analysis):**

1. **NoteTab changes:** Add `CreatedTooltipText` and `UpdatedTooltipText` computed properties. Add `nameof(UpdatedTooltipText)` to `UpdatedAtDependents`.

2. **MainWindow.xaml changes:**
   - Add `xmlns:models="clr-namespace:JoJot.Models"` to Window
   - Add `DataTemplate x:Key="TabItemTemplate"` in Window.Resources with:
     - Two-row Grid: Row 0 = title + pin button + close button, Row 1 = dates
     - Bindings: `{Binding DisplayLabel}`, `{Binding CreatedDisplay}`, `{Binding UpdatedDisplay}`, `{Binding CreatedTooltipText}`, `{Binding UpdatedTooltipText}`
     - `Loaded="TabItemBorder_Loaded"` on root Border for hover/click wiring
     - `x:Name` on key elements: OuterBorder, PinBtn, PinIcon, TitleBlock, RenameBox, CloseBtn, CloseIcon, Col0, Col1
     - DataTriggers:
       - `Pinned=True`: swap Col0/Col1 widths (Auto↔Star), swap PinBtn/TitleBlock Grid.Column (0↔1), PinBtn Visibility=Visible+Opacity=1, ToolTip="Unpin", RenameBox column=1
       - `IsPlaceholder=True`: TitleBlock FontStyle=Italic, Foreground=c-text-muted
   - Default state: PinBtn in Col1 (Auto), TitleBlock in Col0 (Star), PinBtn hidden, ToolTip="Pin"

3. **MainWindow.xaml.cs changes:**
   - Add `FindNamedDescendant<T>(DependencyObject parent, string name)` helper (walks visual tree by FrameworkElement.Name)

4. **MainWindow.Tabs.cs changes:**
   - **CreateTabListItem** shrinks from ~277 to ~25 lines: creates ListBoxItem, sets Tag=tab, Content=tab, ContentTemplate=TabItemTemplate, wires drag/middle-click/right-click events
   - **Add TabItemBorder_Loaded** handler (~80 lines): finds PinBtn/CloseBtn/PinIcon/CloseIcon by name, wires MouseEnter/MouseLeave hover animations (with _isDragging guard), wires pin click (TogglePinAsync) and close click (DeleteTabAsync), wires pinned hover icon swap (pin→unpin glyph + red, on leave restore)
   - **Remove UpdateTabItemDisplay** entirely — data bindings handle DisplayLabel/UpdatedDisplay/IsPlaceholder changes automatically
   - **Simplify ApplyActiveHighlight**: use FindNamedDescendant to find PinBtn/CloseBtn by name (instead of fragile visual tree position checks)
   - **Simplify deselection code** in TabList_SelectionChanged: same approach, find buttons by name

5. **Remove UpdateTabItemDisplay call sites** (5 locations):
   - `MainWindow.xaml.cs:177` (timer), `:498` (undo), `:517` (redo) — remove calls
   - `MainWindow.Rename.cs:57` (rename commit) — remove call
   - `MainWindow.Tabs.cs:645` (save content) — remove call

6. **MainWindow.Rename.cs changes:**
   - `BeginRename`: use `FindNamedDescendant<TextBox>(item, "RenameBox")` and `FindNamedDescendant<TextBlock>(item, "TitleBlock")` instead of `FindDescendant<TextBox>` + Tag reference
   - Remove `Tag = labelBlock` dependency (no longer set in template)

**Key files:** MainWindow.xaml, MainWindow.Tabs.cs (~300 lines removed), MainWindow.xaml.cs, MainWindow.Rename.cs, NoteTab.cs
**Expected reduction:** ~250+ C# lines removed, ~90 XAML lines added
**Tests:** Existing ViewModel tests + existing NoteTab tests cover logic. Manual visual verification needed.

### Phase 9: Keyboard Shortcuts → InputBindings — NOT STARTED
Replace Window_PreviewKeyDown with XAML InputBindings bound to ViewModel commands.

---

## Files Modified (cumulative)

### New files created:
- `JoJot/ViewModels/ObservableObject.cs`
- `JoJot/ViewModels/RelayCommand.cs`
- `JoJot/ViewModels/AsyncRelayCommand.cs`
- `JoJot/ViewModels/MainWindowViewModel.cs`
- `JoJot.Tests/ViewModels/ObservableObjectTests.cs`
- `JoJot.Tests/ViewModels/RelayCommandTests.cs`
- `JoJot.Tests/ViewModels/MainWindowViewModelTests.cs`
- `JoJot.Tests/ViewModels/TabCrudTests.cs`
- `JoJot.Tests/ViewModels/TabReorderTests.cs`
- `JoJot.Tests/ViewModels/EditorStateTests.cs`
- `JoJot.Tests/Models/NoteTabObservableTests.cs`
- `JoJot.Tests/ViewModels/PanelStateTests.cs`
- `JoJot.Tests/ViewModels/DesktopDragTests.cs`

### Modified files:
- `JoJot/Models/NoteTab.cs` — inherits ObservableObject, SetProperty on 5 props
- `JoJot/MainWindow.xaml.cs` — ViewModel property, forwarding fields, delegated methods
- `JoJot/MainWindow.Tabs.cs` — CreateNewTabAsync, TogglePinAsync, CloneTabAsync, SaveCurrentTabContent delegate to VM
- `JoJot/MainWindow.TabDeletion.cs` — DeleteTabAsync, DeleteMultipleAsync, UndoDeleteAsync, ApplyFocusCascadeAsync delegate to VM
- `JoJot/MainWindow.TabDrag.cs` — CompleteDrag uses ViewModel.MoveTab
- `JoJot/MainWindow.Search.cs` — MatchesSearch delegates to VM
- `JoJot/MainWindow.Cleanup.cs` — focus cascade uses ViewModel.GetFocusCascadeTarget; cleanup logic delegates to VM
- `JoJot/MainWindow.Help.cs` — Show/Hide set ViewModel.IsHelpOpen
- `JoJot/MainWindow.Keyboard.cs` — help overlay checks use ViewModel.IsHelpOpen
- `JoJot/MainWindow.DesktopDrag.cs` — uses EvaluateDrag, BeginDrag, UpdateDragTarget, ResetDragState, IsMisplacedOnDesktop

## Key Architectural Decisions
- Forwarding properties pattern: `_tabs => ViewModel.Tabs` etc. allows all 16 partial classes to work unchanged
- ViewModel is `internal` so tests can access it (InternalsVisibleTo already set)
- Pure logic extracted to ViewModel; DB calls + UI updates stay in code-behind
- No MVVM framework — hand-rolled ObservableObject + RelayCommand
- NoteTab inherits ObservableObject directly (not a separate wrapper)
