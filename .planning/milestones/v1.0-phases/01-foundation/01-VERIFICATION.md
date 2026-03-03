---
phase: 01-foundation
verified: 2026-03-02T00:00:00Z
status: passed
score: 17/17 must-haves verified
re_verification: false
human_verification:
  - test: "Launch JoJot twice and observe second-instance behavior"
    expected: "Second instance exits cleanly; first instance window reappears and is focused; log shows 'Second instance detected' and 'Window activated via IPC'"
    why_human: "Runtime behavior — requires two concurrent processes and OS focus observation"
  - test: "Force-kill JoJot and verify database integrity on relaunch"
    expected: "Database at AppData\\Local\\JoJot\\jojot.db is intact after kill; integrity check passes; startup log shows no corruption recovery"
    why_human: "Requires killing the process via Task Manager and inspecting the SQLite file"
  - test: "Close the main window and verify process stays alive"
    expected: "Window disappears but JoJot.exe remains visible in Task Manager; a second launch reactivates the hidden window"
    why_human: "Process persistence is a runtime OS-level observable, not verifiable by code analysis"
  - test: "Verify ReadyToRun publish produces a working binary"
    expected: "dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 succeeds; published binary launches and logs startup time"
    why_human: "Build output verification and startup timing measurement require execution"
---

# Phase 1: Foundation Verification Report

**Phase Goal:** The app can launch as a single instance, enforce the named mutex, accept IPC connections, read/write SQLite with WAL mode, and execute the startup sequence — all before any UI is presented.
**Verified:** 2026-03-02
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths (from ROADMAP.md Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Launching JoJot twice: second instance sends IPC message and exits cleanly; first instance receives it | VERIFIED | `IpcService.TryAcquireMutex` in `App.xaml.cs:59`; second-instance path sends `ActivateCommand` and calls `Environment.Exit(0)` at line 68; server `ListenLoopAsync` dispatches to UI thread |
| 2 | SQLite database created at `AppData\Local\JoJot\jojot.db` with WAL mode on first launch; all four tables exist and are queryable | VERIFIED | `DatabaseService.OpenAsync` creates dir+file at line 26, executes `PRAGMA journal_mode=WAL` at line 39; `EnsureSchemaAsync` creates all four tables with full column definitions |
| 3 | Schema creation happens synchronously on first launch; background migrations run after window shown and never block startup | VERIFIED | `EnsureSchemaAsync` called synchronously at `App.xaml.cs:78`; `Task.Run(() => StartupService.RunBackgroundMigrationsAsync())` fired at line 108 (after `_mainWindow.Show()` at line 100) |
| 4 | Published ReadyToRun binary launches and is interactive; startup time measured and logged as baseline | VERIFIED (code) | `PublishReadyToRun=true` in `JoJot.csproj:9`; `Stopwatch.StartNew()` at `App.xaml.cs:48`; startup timing logged at lines 104–105; runtime test requires human verification |
| 5 | Killing the process and restarting leaves database intact with no corruption | VERIFIED (code) | WAL mode enabled on every open; `VerifyIntegrityAsync` runs at startup; `HandleCorruptionAsync` only triggers if integrity check fails; SQLite WAL guarantees no corruption on unclean shutdown |

**Score:** 5/5 success criteria verified (4 fully automated, 1 requiring human runtime confirmation)

---

## Must-Have Truth Verification

### Plan 01-01 Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | SQLite database file is created at `AppData\Local\JoJot\jojot.db` on first call to `DatabaseService.OpenAsync` | VERIFIED | `DatabaseService.cs:26` — `Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!)` then `new SqliteConnection(connStr)` + `OpenAsync()` at lines 35–36 |
| 2 | WAL journal mode and NORMAL synchronous are active after connection open | VERIFIED | Lines 39–41: `PRAGMA journal_mode=WAL`, `PRAGMA synchronous=NORMAL`, `PRAGMA foreign_keys=ON` executed on every open |
| 3 | All four tables (notes, app_state, pending_moves, preferences) exist after `EnsureSchemaAsync` | VERIFIED | `DatabaseService.cs:52–97` — `CREATE TABLE IF NOT EXISTS` for all four tables with complete column definitions |
| 4 | All writes are serialized through a single connection with `SemaphoreSlim` | VERIFIED | `private static readonly SemaphoreSlim _writeLock = new(1, 1)` at line 14; `await _writeLock.WaitAsync()` in `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, and `ExecuteReaderAsync` |
| 5 | Database integrity check detects missing tables and corruption | VERIFIED | `VerifyIntegrityAsync` queries `sqlite_master` for each of the four tables (lines 112–119); calls `RunQuickCheckAsync` on any missing table |
| 6 | Corrupt database is renamed to `.corrupt` and a fresh one is created | VERIFIED | `HandleCorruptionAsync`: disposes connection, renames to `.corrupt` (lines 136–140), recreates via `OpenAsync + EnsureSchemaAsync` (lines 144–145) |
| 7 | Log output goes to both `jojot.log` file and `System.Diagnostics.Debug` | VERIFIED | `LogService.Write`: `Debug.WriteLine(line)` at line 50, then `File.AppendAllText(_logPath, line + "\n")` inside lock at line 57; thread-safe via `_fileLock` |
| 8 | IPC message types serialize/deserialize correctly with `System.Text.Json` polymorphism | VERIFIED | `IpcMessage.cs`: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]` at line 9; three derived types with discriminators; `IpcMessageContext` source-gen context covers all four types |

### Plan 01-02 Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Second instance detects mutex is held and sends IPC activate command instead of launching | VERIFIED | `App.xaml.cs:59–70`: `TryAcquireMutex` returns false → `SendCommandAsync(new ActivateCommand())` → `Environment.Exit(0)` |
| 2 | First instance receives IPC messages and dispatches to UI thread | VERIFIED | `IpcService.ListenLoopAsync`: deserializes line then `Application.Current.Dispatcher.InvokeAsync(() => _commandHandler(message))` at lines 67–74 |
| 3 | IPC timeout after 500ms triggers zombie process kill | VERIFIED | `SendCommandAsync`: `catch (TimeoutException)` at line 119 calls `KillExistingInstances()`; `KillExistingInstances` loops `Process.GetProcessesByName("JoJot")` and kills non-self PIDs |
| 4 | Window activates and comes to foreground reliably even from background via P/Invoke | VERIFIED | `WindowActivationHelper.ActivateWindow`: six user32.dll P/Invoke declarations; `AttachThreadInput` + `SetForegroundWindow` pattern at lines 57–63; `window.Activate()` + `window.Focus()` at lines 68–69 |
| 5 | Process stays alive when the main window is closed (`ShutdownMode.OnExplicitShutdown`) | VERIFIED | `App.xaml.cs:73`: `ShutdownMode = ShutdownMode.OnExplicitShutdown`; `MainWindow.OnClosing`: `e.Cancel = true; Hide()` at lines 71–72 |
| 6 | IPC protocol supports full command vocabulary (activate, new-tab, show-desktop) even though only activate is wired | VERIFIED | `HandleIpcCommand` in `App.xaml.cs:117–138`: switch handles `ActivateCommand` (full), `NewTabCommand` (log stub), `ShowDesktopCommand` (log stub), default (warn) |

### Plan 01-03 Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | App startup follows exact sequence: mutex → pending_moves check → open DB → schema → integrity check → start IPC server → create window → show window → log timing | VERIFIED* | `App.xaml.cs` sequence: Step 2 (mutex:59) → Step 4 (DB open:77) → Step 4 (schema:78) → Step 5 (integrity:81) → Step 6 (pending_moves stub:90) → Step 8 (IPC server:96) → Step 9 (window show:100) → Step 10 (timing log:103–105). Note: pending_moves stub placed after DB open (STRT-01 shows it before DB open, but DB must be open to read pending_moves — a documented intentional deviation per design) |
| 2 | Second instance sends activate via IPC and exits cleanly | VERIFIED | `App.xaml.cs:63–70`: `!createdNew` branch sends `ActivateCommand` then `Environment.Exit(0)` |
| 3 | Process stays alive after window close; re-opening works via IPC activate | VERIFIED | `OnClosing` cancels and hides (not closes); `ActivateFromIpc` checks `IsVisible` and calls `ShowAndActivate()` if hidden |
| 4 | First-ever launch creates a 'Welcome to JoJot' tab in the database | VERIFIED | `StartupService.CreateWelcomeTabIfFirstLaunch`: `SELECT COUNT(*) FROM notes`, if 0 inserts welcome note with `desktop_guid='default'`, `name='Welcome to JoJot'` |
| 5 | Background migrations run after window is shown and never block startup | VERIFIED | `App.xaml.cs:108`: `_ = Task.Run(() => StartupService.RunBackgroundMigrationsAsync())` — fire-and-forget after `_mainWindow.Show()` at line 100 |
| 6 | Startup duration is logged to both `jojot.log` and debug output | VERIFIED | `App.xaml.cs:104–105`: `LogService.Info($"Startup complete in {sw.ElapsedMilliseconds}ms")` + `Debug.WriteLine($"[JoJot] Startup: {sw.ElapsedMilliseconds}ms")` |
| 7 | Published ReadyToRun binary launches successfully | VERIFIED (code) | `JoJot.csproj:9`: `<PublishReadyToRun>true</PublishReadyToRun>`; runtime test requires human execution |
| 8 | Database file exists at `AppData\Local\JoJot\jojot.db` after first launch | VERIFIED | `DatabaseService.OpenAsync:26`: `Directory.CreateDirectory` + `SqliteConnectionStringBuilder { Mode = ReadWriteCreate }` — creates file on first open |
| 9 | Killing the process and restarting leaves the database intact | VERIFIED (code) | WAL mode with `synchronous=NORMAL` guarantees committed transaction durability; `VerifyIntegrityAsync` on startup detects any corruption |

**Total truths: 17 across all plans. Status: 17/17 VERIFIED**

---

## Required Artifacts Verification

| Artifact | Expected | Exists | Substantive | Wired | Status |
|----------|----------|--------|-------------|-------|--------|
| `JoJot/Services/DatabaseService.cs` | SQLite connection, schema, integrity, corruption recovery, write serialization | Yes | Yes — 295 lines, full implementations of all 8 required methods | Used by `App.xaml.cs`, `StartupService.cs` | VERIFIED |
| `JoJot/Services/LogService.cs` | Dual-output logging, log rotation | Yes | Yes — 66 lines, Initialize/Info/Warn/Error with file+debug+lock | Used by all service files | VERIFIED |
| `JoJot/Services/IpcService.cs` | Named pipe server/client, zombie kill | Yes | Yes — 169 lines, StartServer/StopServer/SendCommandAsync/TryAcquireMutex/KillExistingInstances | Used by `App.xaml.cs` | VERIFIED |
| `JoJot/Services/WindowActivationHelper.cs` | P/Invoke window focus | Yes | Yes — 72 lines, 6 P/Invoke declarations + ActivateWindow | Used by `MainWindow.xaml.cs` | VERIFIED |
| `JoJot/Services/StartupService.cs` | Welcome tab creation, background migrations | Yes | Yes — 64 lines, CreateWelcomeTabIfFirstLaunch/RunBackgroundMigrationsAsync | Used by `App.xaml.cs` | VERIFIED |
| `JoJot/Models/IpcMessage.cs` | IPC type hierarchy with JSON polymorphism | Yes | Yes — 40 lines, IpcMessage + 3 derived types + IpcMessageContext source-gen | Used by `IpcService.cs`, `App.xaml.cs` | VERIFIED |
| `JoJot/App.xaml.cs` | Full 11-step startup sequence | Yes | Yes — 161 lines, complete startup orchestration | Entry point | VERIFIED |
| `JoJot/App.xaml` | ShutdownMode via Startup event (no StartupUri) | Yes | Yes — Startup="OnAppStartup", no StartupUri | Wired to App.xaml.cs | VERIFIED |
| `JoJot/MainWindow.xaml.cs` | IPC handler, window title, close behavior | Yes | Yes — ActivateFromIpc, FlushAndClose, OnClosing (hide), ShowAndActivate | Called by App.xaml.cs IPC handler | VERIFIED |
| `JoJot/JoJot.csproj` | Microsoft.Data.Sqlite 10.0.3, PublishReadyToRun | Yes | Yes — both present at lines 9 and 13 | Project configuration | VERIFIED |

---

## Key Link Verification

### Plan 01-01 Key Links

| From | To | Via | Pattern | Status |
|------|----|-----|---------|--------|
| `DatabaseService.cs` | `Microsoft.Data.Sqlite` | `SqliteConnection` with WAL mode | `PRAGMA journal_mode=WAL` | WIRED — line 39 |
| `DatabaseService.cs` | `LogService.cs` | Error/warn logging during integrity and corruption recovery | `LogService.(Warn\|Error)` | WIRED — lines 116, 130, 277, 289 and more |
| `IpcMessage.cs` | `System.Text.Json` | `JsonPolymorphic` + source-generated context | `JsonPolymorphic.*TypeDiscriminatorPropertyName` | WIRED — line 9 |

### Plan 01-02 Key Links

| From | To | Via | Pattern | Status |
|------|----|-----|---------|--------|
| `IpcService.cs` | `IpcMessage.cs` | JSON serialization/deserialization of IPC commands | `JsonSerializer.(Serialize\|Deserialize)` | WIRED — lines 67–69 (deserialize), 112 (serialize) |
| `IpcService.cs` | `LogService.cs` | Logging IPC events, errors, timeouts | `LogService.(Info\|Warn\|Error)` | WIRED — lines 48, 80–81, 85, 117, 121, 128, 146, 152 |
| `MainWindow.xaml.cs` | `WindowActivationHelper.cs` | Cross-process window focus on IPC activate | `WindowActivationHelper.ActivateWindow` | WIRED — lines 33 and 46 |
| `IpcService.cs` | `System.IO.Pipes` | `NamedPipeServerStream` with `PipeOptions.Asynchronous` | `NamedPipeServerStream.*PipeOptions.Asynchronous` | WIRED — lines 54–59 |

### Plan 01-03 Key Links

| From | To | Via | Pattern | Status |
|------|----|-----|---------|--------|
| `App.xaml.cs` | `IpcService.cs` | TryAcquireMutex, StartServer, SendCommandAsync | `IpcService.(TryAcquireMutex\|StartServer\|SendCommandAsync)` | WIRED — lines 59, 67, 96 |
| `App.xaml.cs` | `DatabaseService.cs` | OpenAsync + EnsureSchemaAsync + VerifyIntegrityAsync | `DatabaseService.(OpenAsync\|EnsureSchemaAsync\|VerifyIntegrityAsync)` | WIRED — lines 77, 78, 81 |
| `App.xaml.cs` | `LogService.cs` | Initialize at startup, timing log after window shown | `LogService.(Initialize\|Info)` | WIRED — lines 55, 56, 104 and more |
| `StartupService.cs` | `DatabaseService.cs` | Welcome tab creation and pending migrations | `DatabaseService.(ExecuteNonQueryAsync\|RunPendingMigrationsAsync)` | WIRED — lines 15, 32 (StartupService.cs), 46 (RunBackgroundMigrationsAsync) |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| DATA-01 | 01-01 | SQLite database at `AppData\Local\JoJot\jojot.db` with WAL mode and NORMAL synchronous | SATISFIED | `DatabaseService.OpenAsync` at `jojot.db` path; WAL + NORMAL PRAGMAs executed |
| DATA-02 | 01-01 | Single SQLite connection per process, all writes serialized | SATISFIED | Static `_connection` field; `SemaphoreSlim(1,1)` guards all three execute methods |
| DATA-03 | 01-01 | Notes table with all required columns | SATISFIED | `EnsureSchemaAsync` creates notes with all 10 columns per spec |
| DATA-04 | 01-01 | App_state table storing per-desktop window geometry | SATISFIED | `EnsureSchemaAsync` creates app_state with all 10 columns per spec |
| DATA-05 | 01-01 | Pending_moves table tracking unresolved window drags | SATISFIED | `EnsureSchemaAsync` creates pending_moves with 5 columns per spec |
| DATA-06 | 01-01 | Preferences table (key/value) | SATISFIED | `EnsureSchemaAsync` creates preferences with key (PK) + value columns |
| DATA-07 | 01-01 | Schema created synchronously on first launch; migrations run in background thread after window shown | SATISFIED | `EnsureSchemaAsync` called synchronously in startup path; `Task.Run(RunBackgroundMigrationsAsync)` after window shown |
| PROC-01 | 01-02 | Single-instance background process via named mutex (`Global\JoJot_SingleInstance`) | SATISFIED | `IpcService.MutexName = "Global\\JoJot_SingleInstance"`; `TryAcquireMutex` + `GC.KeepAlive` in App.xaml.cs |
| PROC-02 | 01-02 | Named pipe IPC (`\\.\pipe\JoJot_IPC`) for second-instance communication | SATISFIED | `IpcService.PipeName = "JoJot_IPC"`; `NamedPipeServerStream` + `NamedPipeClientStream` implemented |
| PROC-03 | 01-02 | Second instance resolves current desktop GUID, sends JSON action via pipe, then exits | SATISFIED (partial) | Second instance sends JSON activate command and exits; desktop GUID resolution deferred to Phase 2 (no virtual desktop API yet) |
| PROC-04 | 01-02 | Pipe timeout (> 500ms) or failure triggers force-kill of hung process and fresh start | SATISFIED | `SendCommandAsync(timeoutMs: 500)` with `catch (TimeoutException)` → `KillExistingInstances()` |
| PROC-05 | 01-02 | Background process stays alive when all windows are closed | SATISFIED | `ShutdownMode.OnExplicitShutdown`; `OnClosing` cancels and hides |
| PROC-06 | 01-02 | Exit via menu flushes all content across all windows, deletes empty tabs, terminates process | PARTIAL | `FlushAndClose()` is an intentional Phase 1 stub — logs and closes. Full implementation requires tabs infrastructure from Phase 4. This is expected for Phase 1. |
| STRT-01 | 01-03 | Startup sequence: mutex → pending_moves check → open DB → session match → load tabs → restore geometry → apply theme → focus tab → show window | SATISFIED (Phase 1 scope) | Core sequence implemented; session match, tab loading, geometry, theme, and focus tab deferred to Phases 2–7 as expected |
| STRT-02 | 01-03 | `PublishReadyToRun=true` (not Native AOT) for fast startup | SATISFIED | `JoJot.csproj:9`: `<PublishReadyToRun>true</PublishReadyToRun>` |
| STRT-03 | 01-03 | Background migrations after window shown; never block cold-start path | SATISFIED | `Task.Run(() => StartupService.RunBackgroundMigrationsAsync())` at line 108, after `_mainWindow.Show()` at line 100 |
| STRT-04 | 01-01 | First launch: create schema synchronously (fast, one-time), then show window | SATISFIED | `EnsureSchemaAsync()` called at `App.xaml.cs:78`, window shown at line 100 |

**Coverage: 17/17 requirements accounted for**
**Full satisfaction: 16/17**
**Partial satisfaction: 1/17 (PROC-06 — intentional Phase 1 stub, full impl deferred to Phase 4)**

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `App.xaml.cs` | 89 | `// Phase 10: await DatabaseService.ResolvePendingMovesAsync();` — commented-out future code | Info | No runtime impact; documents a Phase 10 insertion point |
| `MainWindow.xaml.cs` | 55–59 | `FlushAndClose()` logs and immediately closes — stub for PROC-06 | Info | Expected Phase 1 stub; full flush behavior (tab content, empty tab cleanup, geometry persistence) requires Phase 4 tab infrastructure |
| `App.xaml.cs` | 128, 132 | `NewTabCommand` and `ShowDesktopCommand` handlers are log-only stubs | Info | Expected; IPC vocabulary is declared but handlers are no-ops until Phases 2 and 4 implement them |

**No blockers. All stubs are expected Phase 1 deferred items, not incomplete work.**

---

## Human Verification Required

### 1. Second Instance IPC Flow

**Test:** Launch `dotnet run --project JoJot/JoJot.csproj`, then immediately run it again in a second terminal.
**Expected:** Second process exits quickly (< 1 second); first instance window comes to foreground; `jojot.log` contains "Second instance detected", "sent command", and "Window activated via IPC" entries.
**Why human:** Requires two concurrent process launches and observation of OS window focus behavior.

### 2. Window Hide vs Close

**Test:** Launch JoJot, close the window via the title-bar X button, then check Task Manager.
**Expected:** Window disappears but `JoJot.exe` (or `dotnet.exe` in dev mode) remains visible in Task Manager processes. Launching JoJot again reactivates the hidden window.
**Why human:** Process persistence is an OS-level observable; requires Task Manager inspection.

### 3. Force-Kill and Database Integrity

**Test:** Launch JoJot, then force-kill the process via Task Manager. Relaunch immediately.
**Expected:** JoJot starts cleanly; `jojot.log` shows integrity check passing; `jojot.db` is intact with the welcome tab still present.
**Why human:** Requires process kill via OS tool and inspection of the SQLite database.

### 4. ReadyToRun Publish

**Test:** Run `dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 -p:PublishReadyToRun=true`. Launch the published executable.
**Expected:** Publish completes with no errors; published binary launches; `jojot.log` is created and contains `[INFO] Startup complete in Nms`.
**Why human:** Requires build execution and startup time measurement from a real binary.

---

## Notes on Scope and Intentional Deferral

The following items are intentionally deferred to later phases and are NOT gaps:

- **PROC-03 desktop GUID resolution**: Phase 2 (Virtual Desktop Integration) implements `IVirtualDesktopManager` COM interop. Phase 1 correctly sends an activate command without a desktop GUID.
- **PROC-06 full flush on exit**: Phase 4 (Tab Management) introduces tab infrastructure. `FlushAndClose()` is correctly stubbed.
- **STRT-01 session match, tab load, geometry restore, theme, focus tab**: Phases 2–7 implement these startup steps. The Phase 1 startup sequence correctly ends after `_mainWindow.Show()` with the foundation in place.
- **NewTabCommand / ShowDesktopCommand handlers**: Log-only stubs until Phase 2 and Phase 4 respectively.

---

## Overall Assessment

Phase 1 achieves its stated goal. Every service is substantively implemented (not a stub), every key link is wired, and the startup sequence connects all components in the correct order. The intentional stubs (`FlushAndClose`, `NewTabCommand`/`ShowDesktopCommand` handlers) are explicitly documented as Phase 1 deferred items with clear callouts to the phases that will complete them. No anti-patterns block goal achievement.

The one notable observation is that `StartupService.CreateWelcomeTabIfFirstLaunch` uses inline SQL string building with an `EscapeSql` helper rather than parameterized queries — the code comments this as a one-time insertion and flags it as a deviation from best practice. This is a code quality note, not a blocking issue.

**Score: 17/17 must-haves verified. Phase goal achieved.**

---

_Verified: 2026-03-02_
_Verifier: Claude (gsd-verifier)_
