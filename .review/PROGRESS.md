# MVVM Migration Progress Log

## How to Resume
When starting a new session, read this file and `MVVM-PLAN.md` to understand where we left off. The current phase and last completed step are always listed below.

**Current Phase:** Complete — all phases done
**Last Completed Phase:** 9
**Build status:** Clean — 302 tests passing (0 failures)
**Test count progression:** 126 → 155 → 173 → 199 → 226 → 237 → 253 → 280 → 298 → 302

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

### Phase 8: XAML Data Templates — DONE
- **NoteTab.cs:**
  - Added `CreatedTooltipText` and `UpdatedTooltipText` computed properties
  - Added `nameof(UpdatedTooltipText)` to `UpdatedAtDependents` for change notification
- **MainWindow.xaml:**
  - Added `xmlns:models="clr-namespace:JoJot.Models"` namespace
  - Added `DataTemplate x:Key="TabItemTemplate"` with:
    - Two-row Grid: Row 0 = title + pin + close, Row 1 = dates
    - Data bindings: `{Binding DisplayLabel}`, `{Binding CreatedDisplay}`, `{Binding UpdatedDisplay}`, `{Binding CreatedTooltipText}`, `{Binding UpdatedTooltipText}`
    - Named elements: OuterBorder, PinBtn, PinIcon, TitleBlock, RenameBox, CloseBtn, CloseIcon, Col0, Col1
    - `Loaded="TabItemBorder_Loaded"` on root Border
    - DataTrigger for `Pinned=True`: swaps columns, shows pin always, sets "Unpin" tooltip
    - DataTrigger for `IsPlaceholder=True`: italic muted text
- **MainWindow.xaml.cs:**
  - Added `FindNamedDescendant<T>(parent, name)` helper (visual tree walk by FrameworkElement.Name)
  - Removed 5 `UpdateTabItemDisplay` calls (autosave, undo, redo, rename, save)
- **MainWindow.Tabs.cs:**
  - `CreateTabListItem` shrunk from ~277 to ~37 lines (uses DataTemplate)
  - Added `TabItemBorder_Loaded` handler (~80 lines) for hover/click wiring
  - Added `FindAncestor<T>` helper for visual tree upward walk
  - Removed `UpdateTabItemDisplay` entirely — data bindings handle updates automatically
  - Simplified `ApplyActiveHighlight` to use `FindNamedDescendant`
  - Simplified deselection in `TabList_SelectionChanged` to use `FindNamedDescendant`
- **MainWindow.Rename.cs:**
  - `BeginRename` uses `FindNamedDescendant<TextBox>(item, "RenameBox")` and `FindNamedDescendant<TextBlock>(item, "TitleBlock")`
  - Removed `UpdateTabItemDisplay` call from `CommitRename` — binding handles it
- Tests: `NoteTabObservableTests.cs` — 4 new tests (UpdatedTooltipText notification, tooltip text values)

---

### Phase 9: Keyboard Shortcuts → InputBindings — DONE
- 9 commands created as `ICommand` properties on MainWindow code-behind:
  - `NewTabCommand` (Ctrl+T), `CloseTabCommand` (Ctrl+W), `TogglePinCommand` (Ctrl+P)
  - `CloneTabCommand` (Ctrl+K), `SaveAsCommand` (Ctrl+S), `ToggleHelpCommand` (Ctrl+Shift+?)
  - `IncreaseFontCommand` (Ctrl+=, Ctrl+Add), `DecreaseFontCommand` (Ctrl+-, Ctrl+Sub), `ResetFontCommand` (Ctrl+0)
- 12 InputBindings registered in `InitializeInputBindings()` (called from constructor after InitializeComponent)
- CanExecute guards: `CloseTabCommand`/`TogglePinCommand`/`CloneTabCommand`/`SaveAsCommand` require `_activeTab is not null`
- `Window_PreviewKeyDown` reduced from ~308 to ~220 lines, retains:
  - Guards: drag overlay (blocks all), confirmation dialog, hotkey recording
  - Escape chain: rename → help → find bar → cleanup → recovery → preferences
  - Ctrl+Z/Y: in PreviewKeyDown to prevent WPF native TextBox undo
  - Ctrl+C: context-dependent (no selection = copy all)
  - Ctrl+F: context-dependent (editor focused = find bar, else = tab search)
  - F2: needs SelectedItem cast for inline rename
  - Ctrl+Tab/Shift+Tab: tab cycling with separator skipping
- Uses `RelayCommand` from ViewModels namespace (hand-rolled, Phase 0)
- No new tests (commands wrap existing tested logic; InputBinding wiring is UI-only)

---

## Future Work: XAML UserControl Extraction

MainWindow.xaml is 905 lines. Extract self-contained UI regions into UserControls (one at a time, easiest first):

| Priority | Section | ~Lines | Target File |
|---|---|---|---|
| 1 | Help overlay | 25 | `Controls/HelpOverlay.xaml` |
| 2 | Confirmation dialog | 40 | `Controls/ConfirmationOverlay.xaml` |
| 3 | Cleanup panel | 65 | `Controls/CleanupPanel.xaml` |
| 4 | Recovery sidebar | 30 | `Controls/RecoveryPanel.xaml` |
| 5 | Drag overlay | 85 | `Controls/DragOverlay.xaml` |
| 6 | Hamburger menu | 85 | `Controls/HamburgerMenu.xaml` |
| 7 | Preferences panel | 110 | `Controls/PreferencesPanel.xaml` |

~440 lines extractable → MainWindow.xaml drops to ~460 (layout + toolbar + editor + tab panel).

**Risks:** Each panel's code-behind references MainWindow fields/methods directly. Extraction requires events, commands, or dependency properties for communication. Slide-in/out animations are driven from MainWindow code-behind. Start with help overlay (simplest, least wiring) to establish the pattern.

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
- `JoJot/Models/NoteTab.cs` — inherits ObservableObject, SetProperty on 5 props, tooltip computed props
- `JoJot/MainWindow.xaml` — TabItemTemplate DataTemplate with data bindings + DataTriggers
- `JoJot/MainWindow.xaml.cs` — ViewModel property, forwarding fields, delegated methods, FindNamedDescendant, removed UpdateTabItemDisplay calls
- `JoJot/MainWindow.Tabs.cs` — DataTemplate-based CreateTabListItem, TabItemBorder_Loaded, FindAncestor, removed UpdateTabItemDisplay, simplified ApplyActiveHighlight/deselection
- `JoJot/MainWindow.Rename.cs` — FindNamedDescendant for RenameBox/TitleBlock, removed UpdateTabItemDisplay call
- `JoJot/MainWindow.TabDeletion.cs` — DeleteTabAsync, DeleteMultipleAsync, UndoDeleteAsync, ApplyFocusCascadeAsync delegate to VM
- `JoJot/MainWindow.TabDrag.cs` — CompleteDrag uses ViewModel.MoveTab
- `JoJot/MainWindow.Search.cs` — MatchesSearch delegates to VM
- `JoJot/MainWindow.Cleanup.cs` — focus cascade uses ViewModel.GetFocusCascadeTarget; cleanup logic delegates to VM
- `JoJot/MainWindow.Help.cs` — Show/Hide set ViewModel.IsHelpOpen
- `JoJot/MainWindow.Keyboard.cs` — InputBinding commands, InitializeInputBindings, slimmed PreviewKeyDown
- `JoJot/MainWindow.DesktopDrag.cs` — uses EvaluateDrag, BeginDrag, UpdateDragTarget, ResetDragState, IsMisplacedOnDesktop

## Key Architectural Decisions
- Forwarding properties pattern: `_tabs => ViewModel.Tabs` etc. allows all 16 partial classes to work unchanged
- ViewModel is `internal` so tests can access it (InternalsVisibleTo already set)
- Pure logic extracted to ViewModel; DB calls + UI updates stay in code-behind
- No MVVM framework — hand-rolled ObservableObject + RelayCommand
- NoteTab inherits ObservableObject directly (not a separate wrapper)
