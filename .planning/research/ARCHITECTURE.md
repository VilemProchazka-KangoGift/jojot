# Architecture Research

**Domain:** WPF desktop notepad — single process, multi-window, COM interop, IPC, SQLite
**Researched:** 2026-03-02
**Confidence:** HIGH (derived from authoritative spec documents in resources/)

---

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Process Boundary                             │
│                                                                      │
│  ┌────────────────────────────┐  ┌────────────────────────────────┐  │
│  │     SingleInstanceGuard    │  │      PipeServer (IPC)          │  │
│  │  (Named Mutex acquisition) │  │  \\.\pipe\JoJot_IPC            │  │
│  └──────────────┬─────────────┘  └────────────────┬───────────────┘  │
│                 │                                  │                  │
│  ┌──────────────▼──────────────────────────────────▼───────────────┐  │
│  │                    App / Application Host                        │  │
│  │         (WPF Application, lifecycle owner, DI root)              │  │
│  └──────────────────────────────┬──────────────────────────────────┘  │
│                                 │                                      │
│  ┌──────────────────────────────▼──────────────────────────────────┐  │
│  │                 VirtualDesktopService                            │  │
│  │   (COM: IVirtualDesktopManager + IVirtualDesktopNotification)   │  │
│  │   Resolves desktop GUIDs, fires DesktopChanged / WindowMoved    │  │
│  └──────────────────────────────┬──────────────────────────────────┘  │
│                                 │                                      │
│  ┌──────────────────────────────▼──────────────────────────────────┐  │
│  │                    WindowManager                                 │  │
│  │   Maps desktop GUIDs → MainWindow instances                     │  │
│  │   Creates / focuses / destroys windows on IPC commands           │  │
│  └──────┬────────────────────┬────────────────────┬───────────────┘  │
│         │                    │                    │                   │
│  ┌──────▼──────┐   ┌─────────▼──────┐   ┌────────▼──────┐          │
│  │ MainWindow  │   │  MainWindow    │   │  MainWindow   │ (n per    │
│  │ Desktop A   │   │  Desktop B     │   │  Desktop C    │  desktop) │
│  │             │   │                │   │               │           │
│  │ TabPanel    │   │  TabPanel      │   │  TabPanel     │           │
│  │ EditorPane  │   │  EditorPane    │   │  EditorPane   │           │
│  │ Toolbar     │   │  Toolbar       │   │  Toolbar      │           │
│  │ LockOverlay │   │  LockOverlay   │   │  LockOverlay  │           │
│  └──────┬──────┘   └────────────────┘   └───────────────┘          │
│         │                                                            │
│  ┌──────▼──────────────────────────────────────────────────────────┐ │
│  │                     DataService (Repository)                     │ │
│  │    Single SQLite connection, WAL mode, all writes serialised      │ │
│  │    Tables: notes, app_state, pending_moves, preferences           │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │                    UndoManager (in-memory)                       │ │
│  │    Per-tab UndoStack (50 fine + 20 coarse), 50MB global budget   │ │
│  │    Lives in WindowManager scope — not persisted to SQLite        │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────────┘

External: Windows Virtual Desktop API (COM, undocumented)
External: Win32 API (RegisterHotKey, named mutex, named pipe)
Storage:  AppData\Local\JoJot\jojot.db (SQLite)
```

---

### Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| `SingleInstanceGuard` | Acquires `Global\JoJot_SingleInstance` mutex on startup; detects if process already running | `PipeClient` (send IPC and exit if mutex already held) |
| `PipeServer` | Listens on `\\.\pipe\JoJot_IPC`; receives `left-click` / `middle-click` messages with `desktop_guid` | `WindowManager` (dispatches commands) |
| `PipeClient` | Launched by second instance; sends IPC message then exits | `PipeServer` (in background process) |
| `App` (WPF Application) | Entry point, startup sequence, DI container root, global exception handling | All services |
| `VirtualDesktopService` | Wraps COM `IVirtualDesktopManager` + `IVirtualDesktopNotification`; resolves desktop GUIDs, names, indices; fires C# events on desktop change or window move | `WindowManager`, `MainWindow` (title updates) |
| `WindowManager` | Owns the dictionary of `desktop_guid → MainWindow`; creates/focuses/destroys windows; handles lock overlay resolution | `VirtualDesktopService`, `DataService`, all `MainWindow` instances |
| `MainWindow` | Per-desktop WPF window; owns `TabPanel`, `EditorPane`, `Toolbar`, `LockOverlay`, `DeletionToast`; handles keyboard shortcuts, file drop, theming | `WindowManager`, `DataService`, `UndoManager`, `VirtualDesktopService` |
| `TabPanel` | Left-panel tab list (180px); renders tabs, handles search/filter, drag-to-reorder within zones, rename, context menu, pin state | `MainWindow` (raises tab-switch / delete / rename events) |
| `EditorPane` | Plain-text `TextBox`-based editor; autosave debounce timer; cursor/scroll state; file drop detection | `MainWindow`, `DataService`, `UndoManager` |
| `Toolbar` | Undo/redo/pin/clone/copy/paste/save/delete buttons above editor | `MainWindow` |
| `LockOverlay` | Covers window during inter-desktop drag; shows Reparent/Merge/Cancel; polling fallback for missed COM events | `WindowManager`, `VirtualDesktopService` |
| `DeletionToast` | Bottom-edge slide-up toast; 4-second auto-dismiss; undo for last delete or bulk delete | `TabPanel`, `MainWindow` |
| `DataService` | Single SQLite connection (WAL), all CRUD for `notes`, `app_state`, `pending_moves`, `preferences`; schema creation on first launch; migrations on background thread | Everything that needs persistence |
| `UndoManager` | Manages all `UndoStack` instances; enforces 50MB global budget; triggers collapse on pressure | `EditorPane` (binds active stack on tab switch) |
| `UndoStack` | Per-tab; 50 fine-grained + 20 coarse checkpoints; pointer-based undo/redo | `UndoManager`, `EditorPane` |
| `PreferencesService` | Reads/writes `preferences` table; broadcasts live changes (theme, font size, debounce, hotkey) | `MainWindow`, `EditorPane`, `HotkeyService` |
| `ThemeService` | Swaps WPF `ResourceDictionary` instantly; listens to `SystemEvents.UserPreferenceChanged` for system-follow | `PreferencesService`, all windows |
| `HotkeyService` | Calls `RegisterHotKey` Win32 API; dispatches to `WindowManager` on activation | `WindowManager`, `PreferencesService` |
| `StartupOrchestrator` | Executes the 10-step startup sequence; ensures < 200ms window-show target; defers migrations | `DataService`, `VirtualDesktopService`, `WindowManager` |

---

## Recommended Project Structure

```
JoJot/
├── App.xaml                    # Application entry point, resources
├── App.xaml.cs                 # Startup orchestration, DI setup
│
├── Core/                       # Process-level services (no UI dependencies)
│   ├── SingleInstanceGuard.cs  # Named mutex acquisition
│   ├── PipeServer.cs           # Named pipe listener
│   ├── PipeClient.cs           # IPC sender (second-instance path)
│   ├── StartupOrchestrator.cs  # Sequenced startup (10 steps)
│   └── HotkeyService.cs        # Win32 RegisterHotKey
│
├── VirtualDesktop/             # COM interop layer (isolated)
│   ├── IVirtualDesktopManager.cs   # COM interface definition
│   ├── IVirtualDesktopNotification.cs
│   ├── VirtualDesktopService.cs    # Wraps COM, exposes C# events
│   └── DesktopInfo.cs          # GUID + name + index value object
│
├── Data/                       # SQLite persistence layer
│   ├── DataService.cs          # Single connection, all queries
│   ├── Models/
│   │   ├── Note.cs
│   │   ├── AppState.cs
│   │   ├── PendingMove.cs
│   │   └── Preferences.cs
│   └── Migrations/
│       └── MigrationRunner.cs  # Background migration runner
│
├── Windows/                    # WPF window layer
│   ├── WindowManager.cs        # GUID → MainWindow dictionary
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── Controls/
│   │   ├── TabPanel.xaml
│   │   ├── TabPanel.xaml.cs
│   │   ├── EditorPane.xaml
│   │   ├── EditorPane.xaml.cs
│   │   ├── Toolbar.xaml
│   │   ├── Toolbar.xaml.cs
│   │   ├── LockOverlay.xaml
│   │   ├── LockOverlay.xaml.cs
│   │   └── DeletionToast.xaml
│   │       DeletionToast.xaml.cs
│   └── Dialogs/
│       ├── PreferencesDialog.xaml
│       ├── PreferencesDialog.xaml.cs
│       └── OrphanedSessionPanel.xaml
│           OrphanedSessionPanel.xaml.cs
│
├── Editing/                    # Undo/redo system
│   ├── UndoManager.cs          # Global budget enforcer
│   └── UndoStack.cs            # Per-tab two-tier stack
│
├── Services/                   # App-level services
│   ├── PreferencesService.cs   # Read/write + broadcast changes
│   └── ThemeService.cs         # ResourceDictionary swap
│
└── Themes/                     # WPF resource dictionaries
    ├── Light.xaml
    ├── Dark.xaml
    └── Tokens.xaml             # Shared color token names
```

### Structure Rationale

- **Core/:** Services with no WPF dependency — testable without UI, must initialize before any window.
- **VirtualDesktop/:** Isolated COM interop with a clean C# event boundary. COM failures contained here; rest of app sees only C# events.
- **Data/:** Single-responsibility persistence. All SQL lives here; nothing else writes to SQLite directly.
- **Windows/:** All WPF views and code-behind. `WindowManager` is the only component that creates `MainWindow` instances.
- **Editing/:** Undo/redo isolated from both UI and persistence — it is pure in-memory logic.
- **Services/:** Stateful singletons that broadcast changes to multiple windows (theme, preferences).

---

## Architectural Patterns

### Pattern 1: Process-Level Single-Instance via Mutex + Named Pipe

**What:** Second process instance acquires named mutex, finds it already held, sends IPC via named pipe, and exits. Background process receives message and acts.

**When to use:** Anytime a desktop app must ensure one background process while accepting launch commands from OS (taskbar icon clicks, hotkeys, shortcuts).

**Trade-offs:** Simple and reliable. Pipe timeout (500ms) handles hung-process recovery by PID force-kill. No third-party library needed — Win32 primitives.

**Example:**
```csharp
// First instance: become the server
bool createdNew;
var mutex = new Mutex(true, "Global\\JoJot_SingleInstance", out createdNew);
if (!createdNew)
{
    // Second instance path: send IPC, exit
    PipeClient.Send(new { action = "left-click", desktop_guid = currentGuid });
    Application.Current.Shutdown();
    return;
}
// Start pipe server and continue as background process
PipeServer.Start(HandleIpcMessage);
```

---

### Pattern 2: COM Interop Isolation Behind a C# Service Boundary

**What:** All COM interface declarations and `Marshal.GetActiveObject` / `CoCreateInstance` calls live exclusively in `VirtualDesktopService`. The rest of the codebase receives clean C# events: `DesktopGuidResolved`, `WindowMovedToDesktop`, `DesktopNameChanged`.

**When to use:** When integrating with undocumented or fragile COM APIs (Windows virtual desktop API is undocumented and version-sensitive). Isolating COM means failures and Windows-version guards are contained.

**Trade-offs:** Extra indirection. Worth every bit — the virtual desktop COM API changes between Windows versions and can fail at runtime. Centralized failure handling is critical.

**Example:**
```csharp
public class VirtualDesktopService
{
    // COM objects are private — never leaked to callers
    private IVirtualDesktopManager _vdm;
    private IVirtualDesktopNotification _vdn;

    public event EventHandler<DesktopInfo>? WindowMovedToDesktop;

    public bool TryGetCurrentDesktopGuid(out Guid guid) { ... }
    // Returns false cleanly on COM failure — caller falls back to "default" GUID
}
```

---

### Pattern 3: Single SQLite Connection, All Writes Serialized

**What:** One `SqliteConnection` created at startup, held open for the process lifetime. All reads and writes go through `DataService`. No connection pooling, no multiple connections.

**When to use:** Local single-user desktop apps with SQLite. WAL mode allows reads to overlap with writes, so one connection is sufficient and avoids locking issues.

**Trade-offs:** Simplest possible SQLite pattern. No connection contention. Autosave debounce keeps write frequency low. The only risk is blocking the UI thread — use `async`/`await` for all DB calls.

**Example:**
```csharp
// DataService holds the one connection
public class DataService
{
    private readonly SqliteConnection _db;

    // WAL mode, NORMAL sync — set once at startup
    public DataService(string dbPath)
    {
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        _db.Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
    }

    public async Task SaveNoteAsync(Note note) { ... }
}
```

---

### Pattern 4: Per-Tab Undo Stack with Global Memory Budget

**What:** Each tab owns an `UndoStack` (not shared with the WPF `TextBox`). `UndoManager` tracks all stacks and enforces a 50MB global ceiling. On pressure (>40MB), it collapses the oldest stacks from fine-grained to coarse, protecting the active tab.

**When to use:** Multi-tab editors where WPF's native `TextBox.Undo` is insufficient (it clears on each tab switch and cannot be swapped).

**Trade-offs:** More code than using `TextBox` undo. Required here because WPF `TextBox` undo is per-control and loses history on tab switch.

---

### Pattern 5: Three-Tier Session Matching with Fallback

**What:** Desktop sessions are matched on startup using GUID (exact, same-boot), then name (across reboots), then index (last resort). First match wins; stored GUID is updated to the current live GUID.

**When to use:** When the primary key (GUID) is volatile across reboots (Windows reassigns virtual desktop GUIDs on every reboot).

**Trade-offs:** More matching logic than a simple key lookup. Required because the OS does not guarantee stable GUIDs. Ambiguity detection (two sessions with same name) is critical — surfaced to user, never silently merged.

---

### Pattern 6: Deferred Migrations (Window-First Startup)

**What:** DB schema creation happens synchronously on first launch (fast, one-time). Schema migrations on upgrades run in a background thread *after* the window is shown. Migrations never block the cold-start path.

**When to use:** Any desktop app with a startup latency target. User sees a responsive window immediately.

**Trade-offs:** Migrations run while app is usable. Migration failure must be non-fatal (log and continue). Ensure the app handles a partially migrated schema gracefully.

---

## Data Flow

### Startup Flow (Happy Path, < 200ms Target)

```
Executable launched (AOT native binary)
    |
    v
SingleInstanceGuard.Acquire()
    |- Mutex not held -> continue as background process
    `- Mutex held -> PipeClient.Send() -> exit
    |
    v
DataService.Open()  [sync, fast]
    |- DB new -> CreateSchema() [sync]
    `- DB exists -> skip migrations until after window shown
    |
    v
DataService.CheckPendingMoves()
    `- Row found -> restore window to origin desktop, delete row
    |
    v
VirtualDesktopService.TryGetCurrentDesktopGuid()
    `- Failure -> fallback GUID "default"
    |
    v
DataService.MatchSession(guid) [3-tier: GUID -> name -> index]
    |
    v
DataService.LoadTabs(desktopGuid) + DataService.LoadAppState(desktopGuid)
    |
    v
WindowManager.CreateWindow(desktopGuid, tabs, appState)
    |
    v
MainWindow.Show()  <-- target: < 200ms total
    |
    v [background thread]
DataService.RunPendingMigrations()
```

---

### User Types in Editor (Autosave Flow)

```
User keystroke
    |
    v
EditorPane.TextChanged
    |
    v
DebounceTimer.Reset(500ms)
    |
    v [500ms later, or on window close]
DataService.SaveNoteAsync(noteId, content, cursorPos, scrollOffset)
    |
    v
UndoStack.Push(contentSnapshot)  [if content differs from stack top]
```

---

### Left-Click on Taskbar (IPC Flow)

```
OS raises taskbar icon click
    |
    v
New process instance launched
    |
    v
VirtualDesktopService.TryGetCurrentDesktopGuid() [in new instance]
    |
    v
PipeClient.Send({ action: "left-click", desktop_guid: "..." })
    |
    v [in background process]
PipeServer receives message
    |
    v
WindowManager.HandleLeftClick(desktopGuid)
    |- Window exists for guid -> BringToForeground()
    `- No window -> DataService.MatchSession() -> CreateWindow()
```

---

### Window Dragged Between Desktops

```
User drags window in Task View
    |
    v
OS moves window to target desktop
    |
    v
VirtualDesktopService.OnWindowMovedToDesktop fires
    |
    v
DataService.InsertPendingMove(originGuid, targetGuid, hwnd)  [crash recovery row]
    |
    v
MainWindow.ShowLockOverlay()  [all input disabled]
    |
    v [user clicks Reparent / Merge / Cancel]
    |
    |- Reparent: DataService.UpdateNotesDesktop(originGuid, targetGuid)
    |             DataService.UpsertAppState(targetGuid)
    |             DataService.DeletePendingMove()
    |
    |- Merge:    DataService.ReassignNotes(originGuid, targetGuid, after: lastPinned/lastUnpinned)
    |            DataService.DeleteAppState(originGuid)
    |            DataService.DeletePendingMove()
    |            originWindow.Close()
    |
    `- Cancel:   VirtualDesktopService.MoveWindowToDesktop(hwnd, originGuid)
                 DataService.DeletePendingMove()
                 MainWindow.HideLockOverlay()
```

---

### Tab Switch

```
User clicks tab B (was on tab A)
    |
    v
TabPanel raises TabSwitchRequested(tabB.Id)
    |
    v
MainWindow saves tab A: cursor position, scroll offset (in-memory, not DB yet)
UndoManager.SetActiveStack(tabB.undoStack)
    |
    v
EditorPane.LoadTab(tabB): set Text, restore cursor, restore scroll
    |
    v
tabB.lastAccessed = now
DataService.UpdateAppState(activeTabId = tabB.Id)  [async, low priority]
```

---

## Scaling Considerations

This is a single-user local desktop app. Traditional "scaling" does not apply.

| Concern | Reality | Approach |
|---------|---------|----------|
| Many open tabs | In-memory undo stacks grow; 50MB budget is the only constraint | UndoManager collapse handles this automatically |
| Many virtual desktops | One window per desktop; WindowManager dictionary stays small (Windows supports ~unlimited but users have 2-10 typically) | No action needed |
| Large note content | 500KB file drop limit; SQLite TEXT can hold far more | Autosave cost is linear in content size; debounce cap prevents thrash |
| DB grows over time | Old orphaned sessions accumulate | Expose "delete orphaned sessions" in recovery panel; no auto-purge |
| COM API version sensitivity | Windows 11 builds changed virtual desktop COM internals | Guard with version checks; graceful fallback to "default" GUID mode |

---

## Anti-Patterns

### Anti-Pattern 1: Opening Multiple SQLite Connections

**What people do:** Open a new `SqliteConnection` per operation, or one per window (one per desktop).

**Why it's wrong:** SQLite WAL mode supports concurrent reads but serialized writes. Multiple connections from the same process increase locking complexity and risk `SQLITE_BUSY` errors. With AOT + serialized writes, one connection is sufficient and simpler.

**Do this instead:** Hold one `SqliteConnection` in `DataService`, opened at startup and kept open. Use `async`/`await` so writes never block the UI thread.

---

### Anti-Pattern 2: Using WPF TextBox Native Undo

**What people do:** Rely on the built-in `TextBox` undo stack (Ctrl+Z handled by WPF).

**Why it's wrong:** WPF `TextBox.UndoLimit` controls depth, but the stack is **cleared on every programmatic `Text` assignment** — which happens on every tab switch. Undo history is lost whenever the user switches tabs.

**Do this instead:** Disable native undo (`TextBox.IsUndoEnabled = false`), maintain `UndoStack` per tab in `UndoManager`, and manually handle `Ctrl+Z` / `Ctrl+Y`.

---

### Anti-Pattern 3: Running DB Migrations on the Cold-Start Path

**What people do:** Check schema version and run all migrations before showing the window.

**Why it's wrong:** Migrations can take seconds on large databases. This directly breaks the < 200ms startup target.

**Do this instead:** Schema creation (first launch only) is synchronous and fast. All subsequent migrations run in a background thread after `MainWindow.Show()`. Migration failure is non-fatal — log and continue.

---

### Anti-Pattern 4: Calling COM Virtual Desktop API Without a Fallback

**What people do:** Call `IVirtualDesktopManager::GetWindowDesktopId` and assume it always returns a valid GUID.

**Why it's wrong:** The Windows virtual desktop COM interfaces are undocumented and have changed between Windows 10 and 11 builds. Any COM call can fail (`COMException`, interface not found, wrong version).

**Do this instead:** Wrap every COM call in try/catch inside `VirtualDesktopService`. Define explicit fallback behaviour for each failure mode (see `resources/02-virtual-desktops.md` failure table). Expose only C# events to the rest of the app — COM exceptions never escape `VirtualDesktopService`.

---

### Anti-Pattern 5: Storing Desktop State in Window Objects

**What people do:** Attach tab state, session GUIDs, or geometry directly to the `Window` object's fields. Window close = state gone.

**Why it's wrong:** JoJot windows are created and destroyed per-desktop but the process stays alive. State must survive window destruction and be re-loadable when the same desktop is re-opened.

**Do this instead:** All durable state lives in `DataService` (SQLite). `MainWindow` holds only transient UI state (current `UndoStack` binding, pending overlay state). On `Window.Closing`, flush to DB before the window is destroyed.

---

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Windows Virtual Desktop COM | `VirtualDesktopService` wraps `IVirtualDesktopManager` + `IVirtualDesktopNotification`; exposes C# events | Undocumented; version-sensitive; isolate completely |
| Win32 Named Mutex | P/Invoke `CreateMutex` or `System.Threading.Mutex` | `Global\JoJot_SingleInstance` acquired at startup |
| Win32 Named Pipe | `System.IO.Pipes.NamedPipeServerStream` / `NamedPipeClientStream` | `\\.\pipe\JoJot_IPC`; JSON messages; 500ms timeout |
| Win32 RegisterHotKey | P/Invoke `RegisterHotKey` / `UnregisterHotKey` | `HotkeyService`; re-registers when preference changes |
| SQLite | `Microsoft.Data.Sqlite` (AOT-safe) | Single connection; WAL mode; AppData path |
| Windows Theme | `SystemEvents.UserPreferenceChanged` | `ThemeService` listens for system appearance changes |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| `VirtualDesktopService` ↔ rest of app | C# events only (`EventHandler<T>`) | COM never leaks past this boundary |
| `DataService` ↔ all callers | Async methods returning model objects | No raw SQL outside `DataService` |
| `WindowManager` ↔ `MainWindow` | Direct method calls + C# events | `WindowManager` owns window lifecycle; `MainWindow` raises close/flush events |
| `UndoManager` ↔ `EditorPane` | `BindStack(UndoStack)` on tab switch | `EditorPane` calls `Push`/`Undo`/`Redo` on bound stack |
| `PipeServer` ↔ `WindowManager` | Direct method call after deserialization | `PipeServer` parses JSON, calls `WindowManager.HandleCommand()` |
| `PreferencesService` ↔ consumers | `event Action<Preferences>` broadcast | Consumers (editor, hotkey, theme) subscribe; no polling |

---

## Suggested Build Order

Dependencies flow from bottom to top. Build in this order to avoid circular dependencies and enable integration testing at each layer:

1. **Data layer** (`DataService`, schema, models) — no dependencies; everything else depends on it
2. **VirtualDesktopService** (COM interop, C# event wrappers) — depends on nothing in the app; isolates the riskiest external dependency
3. **Core services** (`SingleInstanceGuard`, `PipeServer`/`PipeClient`, `HotkeyService`) — depend on nothing except Win32
4. **Editing layer** (`UndoStack`, `UndoManager`) — pure in-memory; no UI or DB dependency
5. **Services** (`PreferencesService`, `ThemeService`) — depend on `DataService`
6. **Window controls** (`TabPanel`, `EditorPane`, `Toolbar`, `LockOverlay`, `DeletionToast`) — depend on Editing layer and DataService
7. **MainWindow** — assembles controls; depends on all above
8. **WindowManager** — depends on `MainWindow`, `VirtualDesktopService`, `DataService`
9. **StartupOrchestrator** and `App.xaml.cs` — wires everything together; last to build

This order means each layer can be tested (or at least wired up) before the next layer that depends on it is built.

---

## Sources

- `resources/01-data-model.md` — Schema, WAL configuration, migrations pattern (HIGH confidence — project spec)
- `resources/02-virtual-desktops.md` — IPC, process lifecycle, COM API, session matching, drag handling (HIGH confidence — project spec)
- `resources/03-layout-and-ui.md` — Component structure, theming, controls (HIGH confidence — project spec)
- `resources/05-editing.md` — Undo/redo model, autosave flow, file drop (HIGH confidence — project spec)
- `resources/07-preferences.md` — PreferencesService concerns, hotkey, theme (HIGH confidence — project spec)
- `resources/08-startup.md` — Startup sequence, AOT constraints, migration deferral (HIGH confidence — project spec)
- `.planning/PROJECT.md` — Architectural decisions, constraints, out-of-scope (HIGH confidence — project spec)

---
*Architecture research for: WPF desktop notepad with virtual desktop integration*
*Researched: 2026-03-02*
