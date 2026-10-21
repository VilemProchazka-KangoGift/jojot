# AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

JoJot is a WPF desktop notepad application targeting .NET 10 (net10.0-windows), written in C#. It provides per-virtual-desktop note sessions with tabbed editing, auto-save, undo/redo, theme switching, and single-instance enforcement.

## Build & Run Commands

```bash
# Build
dotnet build JoJot/JoJot.slnx

# Run
dotnet run --project JoJot/JoJot.csproj

# Clean
dotnet clean JoJot/JoJot.slnx

# Publish (single-file, self-contained)
dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 --self-contained
```

The solution file uses the `.slnx` format.

```bash
# Test
dotnet test JoJot.Tests/JoJot.Tests.csproj
```

Test project: `JoJot.Tests/` (xUnit + AwesomeAssertions + NSubstitute). 1266 tests. `InternalsVisibleTo("JoJot.Tests")` in JoJot.csproj.

### Versioning

CalVer format: **`YYYY.M.Count`** (e.g. `2026.3.7`). Count increments per release within the month, resets each new month. Update in both `JoJot/JoJot.csproj` (`Version`, `AssemblyVersion`, `FileVersion`) and `installer/jojot.iss` (`AppVersion`, `AppVerName`, `OutputBaseFilename`).

## Architecture

- **Framework**: WPF (.NET 10) only — WinForms is not referenced (`UseWindowsForms` is off). `SystemEvents` comes from `Microsoft.Win32.SystemEvents`, which ships with WPF; monitor work areas and double-click metrics are Win32 P/Invokes (`WindowPlacementHelper`, `MainWindow`). Don't reintroduce `System.Windows.Forms`/`System.Drawing`: it loads the WinForms assemblies at startup
- **Lazy panels**: the seven hidden side panels/overlays (Find, Confirmation, Drag, Preferences, Help, Recovery, Cleanup) are created on first use (`MainWindow.LazyPanels.cs`) into Collapsed `Border` hosts in `MainWindow.xaml`. Use the `...Lazy` accessors to act on one and the null-aware helpers (`IsConfirmationOpen`, `IsRecordingHotkey`, `_x?.Hide()`) in guards/hide paths so a keystroke never builds a panel. Tab rename uses one shared on-demand `TextBox` (`MainWindow.Rename.cs`), not a per-item template element
- **Single-project solution**: All source under `JoJot/`. The `.slnx` file is also inside `JoJot/` (not repo root)
- **Nullable reference types**: Enabled
- **Implicit usings**: Enabled
- **Namespace**: `JoJot` (flat — `JoJot.Models`, `JoJot.Services`, `JoJot.Interop`)
- **Dependencies**: `Microsoft.EntityFrameworkCore.Sqlite`, `Serilog` (+ Sinks.File, Sinks.Debug, Enrichers.Thread, Enrichers.Process)

### Data Storage

SQLite database at `%LocalAppData%\JoJot\jojot.db`. WAL mode, NORMAL synchronous, foreign keys ON. All writes serialized through a `SemaphoreSlim(1,1)` in `DatabaseCore`. **All DB work runs on one dedicated background thread ("JoJot-DB")**: Microsoft.Data.Sqlite's async APIs complete synchronously, so every public store/`DatabaseCore` entry point hops via `DatabaseCore.RunOnDbThreadAsync` (FIFO channel; nested calls from the DB thread run inline) with the body in a private `...CoreAsync`. New store methods must follow that pattern, and code after a store `await` with `ConfigureAwait(false)` runs off the UI thread — marshal with `Dispatcher.InvokeAsync` (never `Invoke`: close paths sync-block the UI thread on DB tasks). `BackgroundWriter.Run` operations never resume on the UI context. Key tables: `notes`, `app_state`, `preferences`, `pending_moves`. Domain stores: `NoteStore`, `SessionStore`, `PreferenceStore`, `PendingMoveStore`.

Schema is stamped in `PRAGMA user_version` (`DatabaseCore.CurrentSchemaVersion` = **2**; bump it and add a version-guarded step in `RunPendingMigrationsCoreAsync` for any schema change). Index `idx_notes_desktop` is `notes(desktop_guid, pinned DESC, sort_order)` — matching the tab-load `ORDER BY pinned DESC, sort_order` so SQLite walks it instead of sorting; it is declared in the EF model (`.IsDescending(false, true, false)`) and recreated by the migration (v<2: `DROP INDEX IF EXISTS` + `CREATE INDEX IF NOT EXISTS`, same name so they converge). `foreign_keys=ON` is set once on the raw connection only (the model has no FKs; there is no per-open EF interceptor). Startup `VerifyIntegrityAsync` checks the four tables with one `sqlite_master` query and runs `PRAGMA quick_check` only when `ShouldRunQuickCheck` says so: a `.clean-shutdown` marker next to the DB is deleted on open and written after a successful close (content = UTC date of the last quick_check); check runs when the marker was missing (crash/power loss/first run), its date is missing/unparseable/in the future, or ≥7 days old. In-memory DBs (tests) have no marker and always check. Only definite corruption (missing table / `quick_check != ok`) triggers `HandleCorruptionAsync`. `CloseAsync` runs `PRAGMA wal_checkpoint(TRUNCATE)` (best-effort, file DBs only) before closing so the next start does not replay a large WAL. Multi-row re-homing (`MigrateTabs*`) is set-based `ExecuteUpdateAsync` — never load `Content` just to change `desktop_guid`/`sort_order`.

### Application Lifecycle

`App.xaml` uses `Startup="OnAppStartup"` (not `StartupUri`). `ShutdownMode` is `OnExplicitShutdown` — the process stays alive when all windows are closed. The startup sequence in `App.xaml.cs` is window-first: logging + mutex happen in `Program.Main` (second instances never load WPF); `OnAppStartup` starts the IPC pipe server first, then the DB pipeline starts in the background while the snapshot (`startup-cache.json`) provides preferences and geometry so the window shows immediately; DB-dependent steps complete behind the visible window, ordered for time-to-type: prefs cache → (session matching only if this desktop's GUID has no session yet) → ensure session row → migrations (one PRAGMA read when stamped) → auto-delete → welcome check → tab load (editor enabled + focused) → hotkey → deferred tail (session matching, pending-move recovery with a merge-preserving `LoadTabsAsync` reload if anything was recovered, orphan badge). `LoadTabsAsync` is re-entrancy safe: a concurrent call makes the running load loop once more, and `CreateNewTab` requests arriving while tabs are not loaded are queued, not dropped. `VirtualDesktopService.GetAllDesktops()` is memoized while `CacheDesktopsDuringStartup` is set. `StartupSnapshot` is reconciled to DB truth after the preference cache loads.

### Single-Instance & IPC

`IpcService` uses a per-session mutex (`Local\JoJot_SingleInstance`) and a session-scoped named pipe (`JoJot_IPC_S{sessionId}`); both carry a `.Dev` suffix in Debug (`AppEnvironment`). Second instances send JSON commands (activate, new-tab) via the pipe with the synchronous `IpcService.SendCommand`, then exit without loading WPF. IPC messages use `System.Text.Json` polymorphic serialization with source-generated `IpcMessageContext`. The pipe server starts at the very top of `OnAppStartup`, before the DB opens, and commands whose target window does not exist yet are held in an `IpcCommandQueue` and replayed when startup completes (a command for an existing window is forwarded at once; the window queues the new tab until its tabs load). A primary is only killed as a zombie after ≥5 s of failed connects **and** when its process is ≥10 s old (`IpcService.ShouldKillCandidate`); a younger primary is still starting and the second instance keeps retrying (20 s ceiling, then exits without killing).

### Virtual Desktop Integration

`VirtualDesktopService` wraps undocumented Windows COM APIs (in `Interop/`) to detect virtual desktops. Each desktop gets its own `MainWindow` instance and separate note session. Falls back to a single "default" desktop GUID when COM is unavailable. `VirtualDesktopNotificationListener` receives live desktop change/rename events via COM callback.

### Window & Tab Model

`App._windows` is a `Dictionary<string, MainWindow>` keyed by desktop GUID. Each `MainWindow` owns an `ObservableCollection<NoteTab>` and manages its own tab panel, text editor, search, drag-reorder, and context menus. The tab list is **data-bound and UI-virtualized with container recycling** (`TabList.ItemsSource` = a `ListCollectionView` over the collection with a search `Filter`; `IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`, `IsSynchronizedWithCurrentItem=False`). Recycling is safe because `TabItemTemplate` holds no per-item state: the container style binds `Tag` to the item and carries the mouse handlers as `EventSetter`s, the template's hover/pin/close handlers resolve their `NoteTab` from `DataContext`/`Tag` at event time (no closures), and all per-state visuals (selection highlight, pin/close visibility, zone divider via `NoteTab.ShowUnpinnedSeparator`) are triggers/bindings; hover fades are animations only, never local values. A recycled container does not re-fire `Loaded`, so `TabItemBorder_Loaded` only counts realizations — never put per-item wiring there. Structural mutations that clear/repopulate the collection (pin re-sort, delete, reload) run inside `MutateTabsGuarded` to suppress the `SelectionChanged` that would otherwise blank the active tab. To act on another tab (e.g. from its context menu) call `SelectTabByNote(tab)` and never assign `_activeTab` first: `TabList_SelectionChanged` saves the outgoing editor text into `_activeTab`. `NoteTab` maps 1:1 to the `notes` SQLite table. Tabs support pinning, soft-delete with undo toast, drag-to-reorder with sort_order persistence, and file-backed mode (`FilePath` non-null). File-backed tabs remember their source file, save directly on Ctrl+S, show a file icon, disable rename, and auto-save to DB like regular notes (file save is explicit only).

### Services (all static except `AutosaveService`, `UndoManager`, and `UndoStack`)

| Service | Role |
|---|---|
| `DatabaseCore` | SQLite connection lifecycle, schema, write lock, migrations |
| `NoteStore` | Notes CRUD, sort orders, cross-desktop migration |
| `SessionStore` | Sessions, window geometry, orphaned session operations |
| `PreferenceStore` | Key-value preferences CRUD |
| `PendingMoveStore` | Desktop drag pending move tracking |
| `AutosaveService` | Per-window debounced save (500ms default, reset-on-keystroke) |
| `UndoManager` / `UndoStack` | Per-tab undo/redo with checkpoint snapshots |
| `ThemeService` | Light/Dark/System theme via ResourceDictionary swap |
| `HotkeyService` | Global Win+Shift+N hotkey via Win32 `RegisterHotKey` P/Invoke |
| `IpcService` | Single-instance mutex + named pipe IPC |
| `VirtualDesktopService` | Desktop detection, session matching, notifications |
| `LogService` | Serilog-backed structured logging to `%LocalAppData%\JoJot\jojot{Date}.log` (rolling daily, 5MB cap, 5 retained files, `shared: true` so a second instance appends to the primary's file — never combine with `buffered: true`). Debug sink only in Debug builds. Deletes legacy `jojot.log` / `jojot.log.old` on startup. Configurable level via `log_level` preference. ThreadId/ProcessId enriched. |
| `StartupService` | First-launch welcome tab, background migrations |
| `FileDropService` | File drag-and-drop import (preserves full path for file-backed tabs) |
| `WindowPlacementHelper` | Win32 `SetWindowPlacement` for geometry save/restore |
| `WindowActivationHelper` | Win32 `SetForegroundWindow` with `AllowSetForegroundWindow` |
| `AppEnvironment` | Centralized paths, config flags, and environment detection |
| `BackgroundWriter` | Tracks fire-and-forget persistence: failure logging/toast surfacing, shutdown drain |
| `LanguageService` | Language selection (English/Czech), culture init, app restart on change, plural helper |
| `IClock` / `IDebounceTimer` | Testability interfaces for time and debounce abstraction |

### Localization

Standard .NET `.resx` localization: `Resources/Strings.resx` (English default) + `Resources/Strings.cs.resx` (Czech). `Strings.Designer.cs` provides typed accessors (generated by `PublicResXFileCodeGenerator` — must be `public`, not `internal`, because VS's XAML diagnostic tools use external reflection to resolve `{x:Static}` references). `LanguageService` sets `Strings.Culture` and `CurrentUICulture` at startup from the `"language"` preference. Language change requires app restart (confirmation dialog shown first, mutex released before restart).

- **XAML**: Uses `{x:Static res:Strings.PropertyName}` with `xmlns:res="clr-namespace:JoJot.Resources"`.
- **C#**: Uses `Strings.PropertyName` directly (compile-time safe).
- **Plurals**: Czech has 3 forms (1 / 2-4 / 5+). Use `LanguageService.Plural(one, few, other, count)` to select the correct resource string, then `string.Format()`.
- **Adding a language**: Create `Resources/Strings.xx.resx` with all keys translated. Add the enum value to `LanguageService.AppLanguage` and handle it in `ParsePreference`/`ToPreferenceString`/`InitializeAsync`. Add a button to the language toggle in `PreferencesPanel.xaml`.

### Theming

Two ResourceDictionaries (`Themes/LightTheme.xaml`, `Themes/DarkTheme.xaml`) swapped at runtime by `ThemeService`. System mode follows Windows dark/light setting via `SystemEvents.UserPreferenceChanged`.

- **Theme keys**: All theme resource key strings live in `Themes/ThemeKeys.cs` as `const string` fields. Code-behind uses `ThemeKeys.TextPrimary`, `ThemeKeys.Accent`, etc. — never bare string literals.
- **XAML**: Uses `{DynamicResource c-...}` for theme-aware bindings. No hardcoded hex colors.
- **Code-behind**: Uses `SetResourceReference(property, ThemeKeys.X)` for live theme updates, or `GetBrush(ThemeKeys.X)` for one-time assignments (`TryFindResource` with fallback).

### MVVM Architecture

Hand-rolled MVVM (no framework). Base classes in `JoJot/ViewModels/`: `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`.

- **`MainWindowViewModel`** owns core state: `Tabs`, `ActiveTab`, `SearchText`, `DesktopGuid`, `FilteredTabs`, panel open/close state, drag state machine, tab CRUD logic
- **`NoteTab`** inherits `ObservableObject` — `SetProperty` on Name, Content, Pinned, UpdatedAt, SortOrder, FilePath with dependent property notifications (e.g., Name→DisplayLabel+IsPlaceholder, FilePath→IsFileBacked)
- **Forwarding properties**: Code-behind uses `_tabs => ViewModel.Tabs` etc. so all 18 partial classes work unchanged
- **DataTemplate**: Tab items use XAML `TabItemTemplate` with data bindings + DataTriggers. Hover/click handlers are declared in the template/container style and resolve the tab at event time (recycling-safe; see Window & Tab Model)
- **InputBindings**: 11 simple shortcuts (Ctrl+T/W/P/K/S, Ctrl+Shift+S, help, font size) as `ICommand` properties + `InputBindings`. Complex/context-dependent shortcuts remain in `PreviewKeyDown`
- **Code-behind retains**: Animations, visual tree manipulation, file dialogs, COM dispatching, modal keyboard guards

### Key Patterns

- **Static services**: Most services are static classes initialized in `App.OnAppStartup`
- **Async throughout**: Database calls, startup sequence, and IPC are all async
- **Structured logging**: All log calls use Serilog message templates — never string interpolation. Use `LogService.Info("Text {PropertyName}", value)` not `LogService.Info($"Text {value}")`. Warn/Error with exceptions: `LogService.Error("Failed {Id}", id, ex)`.
- **Sealed classes**: All non-inherited classes are `sealed` (models, services, view models, adorners). Enables devirtualization.
- **Parameterized SQL**: Raw SQL uses `@param` placeholders via `DatabaseCore.ExecuteNonQueryAsync(sql, ("@name", value))`. Never interpolate values into SQL strings.
- **Build strictness**: `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are enabled — warnings fail the build. `.editorconfig` enforces file-scoped namespaces, `_camelCase` fields, `var` preferences.
- **EF compiled model**: `JoJot/Data/CompiledModels/` is generated. After ANY change to entity
  classes or `OnModelCreating`, regenerate with:
  `dotnet ef dbcontext optimize --project JoJot/JoJot.csproj --output-dir Data/CompiledModels --namespace JoJot.Data.CompiledModels --context JoJotDbContext`
