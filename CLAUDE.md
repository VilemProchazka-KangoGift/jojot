# CLAUDE.md

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

The solution file uses the `.slnx` format. No test project exists.

## Architecture

- **Framework**: WPF (.NET 10), with WinForms enabled only for `SystemEvents` access (ambiguous global usings are removed in `.csproj`)
- **Single-project solution**: All source under `JoJot/`. The `.slnx` file is also inside `JoJot/` (not repo root)
- **Nullable reference types**: Enabled
- **Implicit usings**: Enabled
- **Namespace**: `JoJot` (flat — `JoJot.Models`, `JoJot.Services`, `JoJot.Interop`)
- **Dependencies**: `Microsoft.Data.Sqlite` (SQLite via Microsoft.Data.Sqlite)

### Data Storage

SQLite database at `%LocalAppData%\JoJot\jojot.db`. WAL mode, NORMAL synchronous, foreign keys ON. All writes serialized through a `SemaphoreSlim(1,1)` in `DatabaseService`. Key tables: `notes`, `app_state`, `preferences`, `pending_moves`.

### Application Lifecycle

`App.xaml` uses `Startup="OnAppStartup"` (not `StartupUri`). `ShutdownMode` is `OnExplicitShutdown` — the process stays alive when all windows are closed. The startup sequence in `App.xaml.cs` is ordered: logging → mutex → database → theme → virtual desktop detection → session matching → IPC server → window creation.

### Single-Instance & IPC

`IpcService` uses a global mutex (`Global\JoJot_SingleInstance`) and named pipe (`JoJot_IPC`). Second instances send JSON commands (activate, new-tab) via the pipe then `Environment.Exit(0)`. IPC messages use `System.Text.Json` polymorphic serialization with source-generated `IpcMessageContext`.

### Virtual Desktop Integration

`VirtualDesktopService` wraps undocumented Windows COM APIs (in `Interop/`) to detect virtual desktops. Each desktop gets its own `MainWindow` instance and separate note session. Falls back to a single "default" desktop GUID when COM is unavailable. `VirtualDesktopNotificationListener` receives live desktop change/rename events via COM callback.

### Window & Tab Model

`App._windows` is a `Dictionary<string, MainWindow>` keyed by desktop GUID. Each `MainWindow` owns an `ObservableCollection<NoteTab>` and manages its own tab panel, text editor, search, drag-reorder, and context menus. `NoteTab` maps 1:1 to the `notes` SQLite table. Tabs support pinning, soft-delete with undo toast, and drag-to-reorder with sort_order persistence.

### Services (all static except `AutosaveService` and `UndoManager`)

| Service | Role |
|---|---|
| `DatabaseService` | SQLite connection lifecycle, schema, CRUD, migrations |
| `AutosaveService` | Per-window debounced save (500ms default, reset-on-keystroke) |
| `UndoManager` / `UndoStack` | Per-tab undo/redo with checkpoint snapshots |
| `ThemeService` | Light/Dark/System theme via ResourceDictionary swap |
| `HotkeyService` | Global Win+Shift+N hotkey via Win32 `RegisterHotKey` P/Invoke |
| `IpcService` | Single-instance mutex + named pipe IPC |
| `VirtualDesktopService` | Desktop detection, session matching, notifications |
| `LogService` | File logging to `%LocalAppData%\JoJot\` |
| `StartupService` | First-launch welcome tab, background migrations |
| `FileDropService` | File drag-and-drop import |
| `WindowPlacementHelper` | Win32 `SetWindowPlacement` for geometry save/restore |
| `WindowActivationHelper` | Win32 `SetForegroundWindow` with `AllowSetForegroundWindow` |

### Theming

Two ResourceDictionaries (`Themes/LightTheme.xaml`, `Themes/DarkTheme.xaml`) swapped at runtime by `ThemeService`. UI code uses `FindResource(key)` or `SetResourceReference` — never hardcoded colors. System mode follows Windows dark/light setting via `SystemEvents.UserPreferenceChanged`.

### Key Patterns

- **Code-behind, not MVVM**: UI logic lives in `MainWindow.xaml.cs` — no ViewModels or data binding framework
- **Static services**: Most services are static classes initialized in `App.OnAppStartup`
- **Async throughout**: Database calls, startup sequence, and IPC are all async
- **Phase comments**: Code comments reference design phases (e.g., "Phase 4: TABS-02") — these are historical build phases, not runtime phases
