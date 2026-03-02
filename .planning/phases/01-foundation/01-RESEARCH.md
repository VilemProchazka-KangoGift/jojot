# Phase 1: Foundation - Research

**Researched:** 2026-03-02
**Domain:** SQLite (WAL), Single-Instance Mutex, Named Pipe IPC, WPF Startup Sequence, ReadyToRun Publishing
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Second-instance behavior**
- Second instance triggers focus & bring to front of the existing JoJot window for the current desktop
- If the existing window is minimized, restore it to saved geometry and focus it
- Design the full IPC command vocabulary from day one (activate, new-tab, show-desktop-X, etc.) even though only "activate" is implemented in this phase — future-proofs the protocol
- IPC timeout/force-kill recovery is silent: kill zombie, start fresh, log the event, no user-facing dialog or message

**Failure & recovery**
- Database corruption: attempt SQLite repair first (integrity_check / recovery tools), then backup corrupt file to jojot.db.corrupt and recreate fresh if repair fails
- Background migration failure: log and continue, retry on next launch — user never sees anything, app functions with current schema
- Error philosophy is tiered: critical failures (can't write to DB at all) show a dialog and exit; minor failures (one migration, one IPC message) are logged silently
- Logging goes to both AppData\Local\JoJot\jojot.log file AND System.Diagnostics.Debug output

**Startup experience**
- No splash screen or loading indicator — window appears only when fully ready (startup steps are invisible)
- First-ever launch creates a single "Welcome to JoJot" tab with brief content explaining basics (virtual desktops, keyboard shortcuts) — user can delete it
- Startup timing (duration from launch to window shown) logged to both debug console and log file on every launch
- Quick database integrity check on every launch (verify all expected tables exist) — catches issues before user notices

### Claude's Discretion
- Log file rotation strategy and size limits
- Exact welcome tab content and formatting
- Database integrity check implementation details (pragma vs query approach)
- IPC protocol message format (JSON schema, versioning)
- Startup sequence error handling order and retry logic

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DATA-01 | SQLite database at AppData\Local\JoJot\jojot.db with WAL mode and NORMAL synchronous | Microsoft.Data.Sqlite 10.0.3; WAL + NORMAL PRAGMA pattern documented |
| DATA-02 | Single SQLite connection per process, all writes serialized | Single connection singleton pattern; lock-based serialization |
| DATA-03 | Notes table with id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, editor_scroll_offset, cursor_position | Standard CREATE TABLE + IF NOT EXISTS pattern |
| DATA-04 | App_state table storing per-desktop window geometry, active tab, scroll offset, desktop name/index | Standard CREATE TABLE + IF NOT EXISTS pattern |
| DATA-05 | Pending_moves table tracking unresolved window drags for crash recovery | Standard CREATE TABLE + IF NOT EXISTS pattern |
| DATA-06 | Preferences table (key/value) storing theme, font_size, autosave_debounce_ms, global_hotkey | Key/value table pattern; simple schema |
| DATA-07 | Schema created synchronously on first launch; migrations run in background thread after window shown | Task.Run() for background; synchronous open + CREATE TABLE in startup path |
| PROC-01 | Single-instance background process via named mutex (Global\JoJot_SingleInstance) | Mutex(true, "Global\\JoJot_SingleInstance", out bool createdNew) pattern |
| PROC-02 | Named pipe IPC (\\.\pipe\JoJot_IPC) for second-instance communication | NamedPipeServerStream + NamedPipeClientStream; PipeOptions.Asynchronous required |
| PROC-03 | Second instance resolves current desktop GUID, sends JSON action via pipe, then exits | StreamWriter + System.Text.Json; line-delimited JSON message |
| PROC-04 | Pipe timeout (> 500ms) or failure triggers force-kill of hung process and fresh start | NamedPipeClientStream.Connect(500); Process.GetProcessesByName + Kill() on timeout |
| PROC-05 | Background process stays alive when all windows are closed | Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown |
| PROC-06 | Exit via menu flushes all content across all windows, deletes empty tabs, terminates process | Application.Current.Shutdown() after flush |
| STRT-01 | Startup sequence: mutex → pending_moves check → open DB → session match → load tabs → restore geometry → apply theme → focus tab → show window | Implemented in App.xaml.cs OnStartup override |
| STRT-02 | PublishReadyToRun=true (not Native AOT) for fast startup; best-effort sub-200ms | `<PublishReadyToRun>true</PublishReadyToRun>` in .csproj; requires RID at publish |
| STRT-03 | Background migrations after window shown; never block cold-start path | Task.Run() post window.Show(); fire-and-forget with catch |
| STRT-04 | First launch: create schema synchronously (fast, one-time), then show window | CREATE TABLE IF NOT EXISTS; PRAGMA journal_mode=WAL on first open |
</phase_requirements>

---

## Summary

Phase 1 establishes the invisible backbone of JoJot: a single-process WPF app with a named mutex guard, a named pipe IPC channel for second-instance communication, a SQLite database with WAL mode, and a sequenced startup that never blocks the window from appearing. All four concerns are well-understood .NET patterns with stable, in-box or lightweight NuGet-based solutions.

The standard library for SQLite is `Microsoft.Data.Sqlite` version 10.0.3, which ships aligned with .NET 10 and is maintained by Microsoft. WAL mode is enabled via a PRAGMA immediately after connection open. The single-connection requirement (DATA-02) means no connection pool is needed — one static `SqliteConnection` held for the process lifetime, with all writes serialized by a `lock` or a `SemaphoreSlim(1,1)`.

The single-instance pattern uses a `Mutex` acquired in `App.xaml.cs OnStartup`. If a second instance finds the mutex taken, it connects to the named pipe, sends a JSON "activate" command, and calls `Environment.Exit(0)`. The first instance listens on the pipe with `PipeOptions.Asynchronous` and marshals the activation onto the Dispatcher thread. Cross-process window focus requires `SetForegroundWindow` P/Invoke because WPF's `window.Activate()` is not reliable when called from outside the foreground process; `AttachThreadInput` is the reliable workaround.

**Primary recommendation:** Use `Microsoft.Data.Sqlite` 10.0.3 directly (no EF Core), `System.IO.Pipes` for IPC, `System.Text.Json` for the IPC message format, and `ShutdownMode.OnExplicitShutdown` to keep the background process alive.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Data.Sqlite | 10.0.3 | SQLite ADO.NET provider | Microsoft-maintained, .NET 10 aligned, no extra dependencies beyond SQLitePCLRaw |
| System.IO.Pipes | In-box (.NET 10) | Named pipe IPC | Zero-dependency, built into the runtime, designed for same-machine IPC |
| System.Text.Json | In-box (.NET 10) | IPC message serialization | In-box, fast, no allocation overhead for small messages, source-gen compatible |
| System.Threading.Mutex | In-box (.NET 10) | Single-instance guard | OS-level primitive, survives process crash cleanup automatically |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Data.Sqlite.Core | 10.0.3 | Stripped-down version (no bundled native) | Only if providing own SQLite native; use full package instead |
| System.Diagnostics.Debug | In-box | Debug output logging | Always; supplement with file logging |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Microsoft.Data.Sqlite | SQLite-net-pcl / Dapper | Dapper is a great micro-ORM but adds a dependency; raw ADO.NET keeps this phase minimal |
| System.Text.Json (line-delimited) | Binary protocol / XML | JSON is human-readable, extensible, in-box, sufficient for this single-machine pipe |
| System.IO.Pipes | TCP loopback socket | Named pipes are simpler for same-machine IPC; no port conflicts |

**Installation:**
```bash
dotnet add package Microsoft.Data.Sqlite --version 10.0.3
```
(All other libraries are in-box with .NET 10.)

---

## Architecture Patterns

### Recommended Project Structure

```
JoJot/
├── App.xaml               # ShutdownMode.OnExplicitShutdown set here
├── App.xaml.cs            # OnStartup: full startup sequence + IPC server start
├── MainWindow.xaml
├── MainWindow.xaml.cs     # Handles "activate" IPC command from Dispatcher
├── Services/
│   ├── DatabaseService.cs  # Single SqliteConnection, all DB operations, schema creation
│   ├── IpcService.cs       # NamedPipeServerStream listener + client sender
│   ├── LogService.cs       # File + Debug log writer; rotation at Claude's discretion
│   └── StartupService.cs   # Orchestrates startup sequence steps
└── Models/
    ├── IpcMessage.cs        # sealed record hierarchy for all IPC commands
    └── AppDbContext.cs      # (optional) typed helpers over DatabaseService
```

### Pattern 1: Named Mutex Single-Instance Guard

**What:** Acquire a Global\ mutex on startup; if already held, send IPC message and exit.
**When to use:** Always — in `App.xaml.cs OnStartup`, before any other work.

```csharp
// Source: Microsoft Learn + established WPF community pattern
// App.xaml.cs
private static Mutex? _singleInstanceMutex;

protected override async void OnStartup(StartupEventArgs e)
{
    _singleInstanceMutex = new Mutex(
        initiallyOwned: true,
        name: "Global\\JoJot_SingleInstance",
        out bool createdNew);

    // Prevent GC from collecting the mutex before app exits
    GC.KeepAlive(_singleInstanceMutex);

    if (!createdNew)
    {
        // Second instance: send activate command and exit
        await IpcService.SendCommandAsync(new ActivateCommand(), timeoutMs: 500);
        Environment.Exit(0);
        return;
    }

    // First instance: continue startup
    ShutdownMode = ShutdownMode.OnExplicitShutdown;
    await StartupService.RunAsync();
}

protected override void OnExit(ExitEventArgs e)
{
    _singleInstanceMutex?.ReleaseMutex();
    _singleInstanceMutex?.Dispose();
    base.OnExit(e);
}
```

**Critical:** Hold `_singleInstanceMutex` as a field. If it goes out of scope and is GC'd, the mutex is released and the single-instance guard fails silently in release builds.

### Pattern 2: Named Pipe IPC Server (First Instance)

**What:** `NamedPipeServerStream` with `PipeOptions.Asynchronous` listening in a background loop; restart after each client.
**When to use:** Started during first-instance startup, before window shown.

```csharp
// Source: Microsoft Learn docs + dotnet/runtime issues #40674, #40289
// IpcService.cs (server side)
private static CancellationTokenSource? _cts;

public static void StartServer(CancellationToken appShutdownToken)
{
    _cts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
    Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
}

private static async Task ListenLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // MUST use PipeOptions.Asynchronous for CancellationToken to work
        using var server = new NamedPipeServerStream(
            "JoJot_IPC",
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            await server.WaitForConnectionAsync(ct);
            using var reader = new StreamReader(server, leaveOpen: true);
            string? line = await reader.ReadLineAsync(ct);
            if (line is not null)
                HandleCommand(line); // dispatches to UI thread
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { LogService.Warn("IPC server error", ex); }
        // Loop to create a fresh server instance for the next client
    }
}
```

**Critical:** `PipeOptions.Asynchronous` is REQUIRED for `CancellationToken` to cancel `WaitForConnectionAsync`. Without it, cancellation is ignored and the task hangs.

### Pattern 3: Named Pipe IPC Client (Second Instance)

**What:** `NamedPipeClientStream.Connect(timeoutMs)` — if it times out, kill the zombie process and exit fresh.
**When to use:** Second instance, after mutex check fails.

```csharp
// Source: NamedPipeClientStream.Connect docs + requirement PROC-04
// IpcService.cs (client side)
public static async Task SendCommandAsync(IpcMessage message, int timeoutMs = 500)
{
    try
    {
        using var client = new NamedPipeClientStream(
            ".", "JoJot_IPC", PipeDirection.Out, PipeOptions.None);

        // Connect with timeout per PROC-04 (>500ms = zombie)
        await Task.Run(() => client.Connect(timeoutMs));

        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        string json = JsonSerializer.Serialize(message, IpcMessageContext.Default.IpcMessage);
        await writer.WriteLineAsync(json);
    }
    catch (TimeoutException)
    {
        // PROC-04: Kill zombie, log, start fresh
        LogService.Warn("IPC timeout — killing zombie JoJot process");
        KillExistingInstances();
        // Don't exit — let this instance become the primary
    }
    catch (Exception ex)
    {
        LogService.Warn("IPC send failed", ex);
        // Silent per decision: log only, no dialog
    }
}

private static void KillExistingInstances()
{
    int myPid = Environment.ProcessId;
    foreach (var p in Process.GetProcessesByName("JoJot"))
    {
        if (p.Id != myPid)
        {
            try { p.Kill(entireProcessTree: true); }
            catch { /* already dead */ }
        }
    }
}
```

### Pattern 4: IPC Message Format (JSON, line-delimited)

**What:** Sealed record hierarchy with `System.Text.Json` source-generation for zero-allocation serialization.
**When to use:** For ALL IPC commands. Design full vocabulary now (PROC-03 decision).

```csharp
// Source: System.Text.Json polymorphism docs (.NET 7+), applicable to .NET 10
// Models/IpcMessage.cs

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ActivateCommand), "activate")]
[JsonDerivedType(typeof(NewTabCommand), "new-tab")]
[JsonDerivedType(typeof(ShowDesktopCommand), "show-desktop")]
public abstract record IpcMessage;

public sealed record ActivateCommand : IpcMessage;

public sealed record NewTabCommand(string? InitialContent = null) : IpcMessage;

public sealed record ShowDesktopCommand(string DesktopGuid) : IpcMessage;

// Source-generated context for AOT-friendly serialization
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(ActivateCommand))]
[JsonSerializable(typeof(NewTabCommand))]
[JsonSerializable(typeof(ShowDesktopCommand))]
public partial class IpcMessageContext : JsonSerializerContext { }
```

Wire format (line-delimited, one message per connection):
```json
{"action":"activate"}
{"action":"new-tab","InitialContent":null}
{"action":"show-desktop","DesktopGuid":"<guid>"}
```

### Pattern 5: SQLite Connection + WAL Mode

**What:** Single static connection opened at startup; WAL + NORMAL synchronous set via PRAGMA; never closed until process exit.
**When to use:** In `DatabaseService` as a process-lifetime singleton.

```csharp
// Source: Microsoft.Data.Sqlite docs (learn.microsoft.com) + SQLite WAL docs
// Services/DatabaseService.cs

public static class DatabaseService
{
    private static SqliteConnection? _connection;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    public static async Task OpenAsync(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Do NOT use Cache=Shared with WAL — mixing is discouraged per official docs
        }.ToString();

        _connection = new SqliteConnection(connStr);
        await _connection.OpenAsync();

        // WAL mode is persistent; set once on first open
        await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;");
        await ExecuteNonQueryAsync("PRAGMA synchronous=NORMAL;");
        await ExecuteNonQueryAsync("PRAGMA foreign_keys=ON;");
    }

    public static async Task<T> ExecuteScalarAsync<T>(string sql, ...)
    {
        // All writes go through the semaphore (DATA-02: serialized writes)
        await _writeLock.WaitAsync();
        try { /* execute */ }
        finally { _writeLock.Release(); }
    }
}
```

**Critical:** Do NOT use `Cache=Shared` with WAL mode. The official docs explicitly warn: "Mixing shared-cache mode and write-ahead logging is discouraged."

### Pattern 6: Schema Creation (Synchronous, First Launch)

**What:** `CREATE TABLE IF NOT EXISTS` for all four tables, executed synchronously during startup before window is shown.
**When to use:** Every launch; it's idempotent and fast (< 5ms on warm disk).

```csharp
// Services/DatabaseService.cs
public static async Task EnsureSchemaAsync()
{
    // Runs on startup path (before window shown) — synchronous in intent, async in impl
    await ExecuteNonQueryAsync("""
        CREATE TABLE IF NOT EXISTS notes (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            desktop_guid     TEXT    NOT NULL,
            name             TEXT,
            content          TEXT    NOT NULL DEFAULT '',
            pinned           INTEGER NOT NULL DEFAULT 0,
            created_at       TEXT    NOT NULL DEFAULT (datetime('now')),
            updated_at       TEXT    NOT NULL DEFAULT (datetime('now')),
            sort_order       INTEGER NOT NULL DEFAULT 0,
            editor_scroll_offset INTEGER NOT NULL DEFAULT 0,
            cursor_position  INTEGER NOT NULL DEFAULT 0
        );
        """);

    await ExecuteNonQueryAsync("""
        CREATE TABLE IF NOT EXISTS app_state (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            desktop_guid    TEXT    NOT NULL UNIQUE,
            desktop_name    TEXT,
            desktop_index   INTEGER,
            window_left     REAL,
            window_top      REAL,
            window_width    REAL,
            window_height   REAL,
            active_tab_id   INTEGER,
            scroll_offset   REAL
        );
        """);

    await ExecuteNonQueryAsync("""
        CREATE TABLE IF NOT EXISTS pending_moves (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            window_id       TEXT    NOT NULL,
            from_desktop    TEXT    NOT NULL,
            to_desktop      TEXT,
            detected_at     TEXT    NOT NULL DEFAULT (datetime('now'))
        );
        """);

    await ExecuteNonQueryAsync("""
        CREATE TABLE IF NOT EXISTS preferences (
            key             TEXT    PRIMARY KEY,
            value           TEXT    NOT NULL
        );
        """);
}
```

### Pattern 7: Window Activation from Pipe Thread (Cross-Process Focus)

**What:** Dispatch window restore + `SetForegroundWindow` P/Invoke to the UI thread; use `AttachThreadInput` for reliable cross-process focus.
**When to use:** In the IPC command handler when "activate" is received.

```csharp
// Source: Rick Strahl's WPF Window Activation article + MSDN AttachThreadInput docs
// MainWindow.xaml.cs

[DllImport("user32.dll")]
private static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

[DllImport("user32.dll")]
private static extern uint GetCurrentThreadId();

[DllImport("user32.dll")]
private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

public void ActivateFromIpc()
{
    // Must run on UI thread
    Application.Current.Dispatcher.InvokeAsync(() =>
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        var hwnd = new WindowInteropHelper(this).Handle;
        uint foregroundThread = GetWindowThreadProcessId(
            GetForegroundWindow(), out _);
        uint appThread = GetCurrentThreadId();

        if (foregroundThread != appThread)
        {
            AttachThreadInput(foregroundThread, appThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foregroundThread, appThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        Activate();
        Focus();
    }, DispatcherPriority.ApplicationIdle);
}
```

**Why P/Invoke:** WPF's `window.Activate()` silently fails when called from outside the foreground process. Windows prevents focus-stealing — `AttachThreadInput` is the documented workaround.

### Pattern 8: ReadyToRun Configuration

**What:** Add `<PublishReadyToRun>true</PublishReadyToRun>` to csproj; publish with an explicit RID.
**When to use:** At publish time for the release binary (STRT-02).

```xml
<!-- JoJot.csproj addition -->
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>
  <!-- SelfContained not required but typical for ReadyToRun on Windows -->
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishReadyToRun=true
```

**Note:** ReadyToRun requires a Runtime Identifier (RID). WPF + ReadyToRun is fully supported — Native AOT is not (WPF incompatible per dotnet/wpf#3811, confirmed in REQUIREMENTS.md).

### Pattern 9: Background Migrations (Post-Window-Show)

**What:** Fire-and-forget `Task.Run()` after `window.Show()` with full exception catch; never awaited on the startup path.
**When to use:** In `App.xaml.cs` after `mainWindow.Show()`.

```csharp
// App.xaml.cs — after mainWindow.Show()
_ = Task.Run(async () =>
{
    try
    {
        await DatabaseService.RunPendingMigrationsAsync();
    }
    catch (Exception ex)
    {
        // CONTEXT decision: log and continue, retry next launch
        LogService.Error("Background migration failed", ex);
    }
});
```

### Pattern 10: Database Integrity Check (Quick Table Verification)

**What:** At startup, query `sqlite_master` to verify all four expected tables exist; use `PRAGMA quick_check` if any are missing.
**When to use:** In `DatabaseService.VerifyIntegrityAsync()`, every launch.

```csharp
// Services/DatabaseService.cs
public static async Task<bool> VerifyIntegrityAsync()
{
    // Claude's discretion: table-existence check approach
    // Fast: query sqlite_master (O(1) per table) rather than full integrity_check
    var required = new[] { "notes", "app_state", "pending_moves", "preferences" };
    foreach (var table in required)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        long count = (long)(await cmd.ExecuteScalarAsync())!;
        if (count == 0)
        {
            LogService.Warn($"Expected table '{table}' missing — running integrity check");
            return await RunIntegrityCheckAsync();
        }
    }
    return true;
}

private static async Task<bool> RunIntegrityCheckAsync()
{
    var cmd = _connection!.CreateCommand();
    cmd.CommandText = "PRAGMA quick_check;";
    using var reader = await cmd.ExecuteReaderAsync();
    if (reader.Read())
    {
        string result = reader.GetString(0);
        return result == "ok";
    }
    return false;
}
```

**Rationale for `quick_check` over `integrity_check`:** `quick_check` is O(N) vs `integrity_check` O(NlogN); it skips UNIQUE constraint verification but still detects structural corruption, which is sufficient for a startup health check on a fresh or small database.

### Pattern 11: Corruption Recovery

**What:** If integrity check fails, rename the bad file to `.corrupt` and recreate.
**When to use:** Only if `VerifyIntegrityAsync()` returns false.

```csharp
// Services/DatabaseService.cs
public static async Task HandleCorruptionAsync(string dbPath)
{
    LogService.Error("Database corruption detected — attempting recovery");

    // Close current connection
    _connection?.Dispose();
    _connection = null;

    string corruptPath = dbPath + ".corrupt";
    // Remove previous corrupt file if it exists
    if (File.Exists(corruptPath)) File.Delete(corruptPath);
    File.Move(dbPath, corruptPath);

    LogService.Warn($"Corrupt DB backed up to {corruptPath}");

    // Recreate fresh
    await OpenAsync(dbPath);
    await EnsureSchemaAsync();
}
```

### Pattern 12: Process Stays Alive (PROC-05)

**What:** Set `ShutdownMode.OnExplicitShutdown` so closing all windows does not exit the process.
**When to use:** In `App.xaml.cs OnStartup`, before creating the main window.

```csharp
// App.xaml.cs
ShutdownMode = ShutdownMode.OnExplicitShutdown;
```

Termination only happens via:
- The Exit menu item calling `Application.Current.Shutdown()` (PROC-06)
- The process being killed externally

### Anti-Patterns to Avoid

- **Holding the mutex as a local variable:** If `_singleInstanceMutex` is a local in `OnStartup`, it gets GC'd in release builds and the single-instance guard silently stops working mid-session.
- **Using Cache=Shared with WAL mode:** The official Microsoft.Data.Sqlite docs explicitly warn against this combination.
- **Calling `window.Activate()` from a non-UI thread:** Will silently fail. Always dispatch to `Application.Current.Dispatcher`.
- **Creating `NamedPipeServerStream` without `PipeOptions.Asynchronous`:** `WaitForConnectionAsync` with a `CancellationToken` will NOT cancel without this flag.
- **Using `Application.Current.Shutdown()` on window close:** Defeats PROC-05 (background process requirement).
- **Running schema creation in a background thread:** DATA-07 requires synchronous schema creation before window shown. If you `Task.Run` it and the first-launch window appears before tables exist, tab creation will fail.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SQLite ADO.NET provider | Custom P/Invoke to sqlite3.dll | `Microsoft.Data.Sqlite` | Native library management, connection pooling, parameter binding |
| JSON serialization | String concatenation or custom parser | `System.Text.Json` (in-box) | Source-gen support, no allocation, handles escaping, versioning |
| Single-instance locking | File lock or registry flag | `System.Threading.Mutex` with `Global\\` prefix | OS-level, survives process crash, automatic cleanup on exit |
| IPC transport | TCP socket or file polling | `System.IO.Pipes.NamedPipeServerStream` | Designed for same-machine IPC, no port conflicts, OS security |
| Log file rolling | Custom file write loop | See pitfalls — roll by size check before write (simple is fine) | No logging framework needed at this scale |

**Key insight:** All five infrastructure problems have solved, battle-tested .NET in-box or first-party solutions. The only NuGet package this phase adds is `Microsoft.Data.Sqlite`.

---

## Common Pitfalls

### Pitfall 1: Mutex Garbage Collection in Release Builds
**What goes wrong:** Single-instance guard works in Debug but fails in Release; a second instance can start after 60-90 seconds.
**Why it happens:** In release builds, the JIT detects that the `Mutex` variable is never read after `TryWaitOne` and marks it eligible for GC. The mutex is released when collected.
**How to avoid:** Declare `_singleInstanceMutex` as a `static` field on `App` (not a local variable), AND call `GC.KeepAlive(_singleInstanceMutex)` at the end of `OnStartup`.
**Warning signs:** Integration test where second instance is launched 2+ minutes after first instance starts working.

### Pitfall 2: NamedPipeServerStream Cancellation Without PipeOptions.Asynchronous
**What goes wrong:** App shutdown hangs; the background pipe listener thread never cancels.
**Why it happens:** `WaitForConnectionAsync(CancellationToken)` only respects the token if the stream was created with `PipeOptions.Asynchronous`. Without it, the cancellation token is checked at call entry only.
**How to avoid:** Always pass `PipeOptions.Asynchronous` when constructing `NamedPipeServerStream` if you intend to cancel it.
**Warning signs:** App takes 30+ seconds to close; Task Manager shows process still running after closing.

### Pitfall 3: Cache=Shared + WAL Mode Combination
**What goes wrong:** Database write performance degrades; transaction behaviour changes unexpectedly.
**Why it happens:** Shared-cache mode and WAL mode interact in ways that defeat WAL's concurrency benefits.
**How to avoid:** Use the default cache mode (do not specify `Cache=Shared` in connection string when WAL is active).
**Warning signs:** Intermittent `SQLITE_BUSY` errors even with a single connection.

### Pitfall 4: Window Activate Failing from Pipe Thread
**What goes wrong:** Second instance sends "activate" but the first instance window does not come to front; it flashes in the taskbar instead.
**Why it happens:** Windows 10/11 prevent applications from stealing focus unless they are the current foreground process. `window.Activate()` alone is insufficient when called cross-process.
**How to avoid:** Use `AttachThreadInput` + `SetForegroundWindow` P/Invoke pattern. Always dispatch to `DispatcherPriority.ApplicationIdle` so the Dispatcher queue is drained before activation.
**Warning signs:** Window flashes in taskbar but does not gain focus; user has to manually click it.

### Pitfall 5: ShutdownMode Default Kills Process on Window Close
**What goes wrong:** User closes the JoJot window; the process exits; on re-launch a new instance starts with no IPC state.
**Why it happens:** WPF's default `ShutdownMode` is `OnLastWindowClose`, which terminates the process when all windows are closed.
**How to avoid:** Set `ShutdownMode = ShutdownMode.OnExplicitShutdown` in `OnStartup` before any windows are created.
**Warning signs:** Process is gone from Task Manager after closing window; mutex is released; next launch creates a fresh instance instead of re-using the background one.

### Pitfall 6: ReadyToRun Requires RID at Publish
**What goes wrong:** `dotnet publish` without `-r` produces a framework-dependent build that ignores `PublishReadyToRun=true`.
**Why it happens:** ReadyToRun generates architecture-specific native code; without a target RID, the compiler cannot produce it.
**How to avoid:** Always publish with `-r win-x64` (or `win-x86` / `win-arm64`).
**Warning signs:** Published DLL is the same size as a non-R2R build; no `.ni.dll` files in output.

### Pitfall 7: Blocking the Startup Thread with Database I/O
**What goes wrong:** Cold start hangs for 200-500ms before window appears; startup time logging shows schema creation dominating.
**Why it happens:** `await` in an `async void OnStartup` must still complete before the window can show if the code is linear. But if `await` is called on an async method that is itself synchronous internally, no yield occurs.
**How to avoid:** Use `ConfigureAwait(false)` on database awaits in the startup path so they can complete on thread pool; only dispatch UI operations back to Dispatcher. Measure each startup step with `Stopwatch`.
**Warning signs:** `Stopwatch` shows >50ms on `EnsureSchemaAsync()` call.

---

## Code Examples

Verified patterns from official sources:

### SQLite WAL + NORMAL Synchronous Setup

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings
// + https://sqlite.org/wal.html
var connStr = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    // Cache left as Default (not Shared) when using WAL
}.ToString();

var connection = new SqliteConnection(connStr);
connection.Open();

// WAL mode persists across connections once set — only needs to run once
using (var cmd = connection.CreateCommand())
{
    cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
    cmd.ExecuteNonQuery();
}
```

### PROC-04 Zombie Kill Logic

```csharp
// Source: Process.GetProcessesByName docs + System.Diagnostics.Process API
// Timeout is 500ms per PROC-04 requirement
try
{
    client.Connect(500); // throws TimeoutException if not connected in 500ms
}
catch (TimeoutException)
{
    LogService.Warn("IPC connect timeout — killing zombie");
    int myPid = Environment.ProcessId;
    foreach (var proc in Process.GetProcessesByName("JoJot"))
    {
        if (proc.Id != myPid)
            try { proc.Kill(entireProcessTree: true); } catch { }
    }
    // Do not exit — this second instance becomes the new primary
    // Re-enter startup flow (re-acquire mutex)
}
```

### STRT-01 Startup Sequence Skeleton

```csharp
// App.xaml.cs — full startup sequence per STRT-01
protected override async void OnStartup(StartupEventArgs e)
{
    var sw = Stopwatch.StartNew();

    // Step 1: Mutex (single instance guard)
    _singleInstanceMutex = new Mutex(true, "Global\\JoJot_SingleInstance", out bool createdNew);
    GC.KeepAlive(_singleInstanceMutex);
    if (!createdNew)
    {
        await IpcService.SendCommandAsync(new ActivateCommand());
        Environment.Exit(0);
        return;
    }

    ShutdownMode = ShutdownMode.OnExplicitShutdown;

    // Step 2: Open DB + schema + integrity check
    string dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JoJot", "jojot.db");
    await DatabaseService.OpenAsync(dbPath);
    await DatabaseService.EnsureSchemaAsync();
    bool healthy = await DatabaseService.VerifyIntegrityAsync();
    if (!healthy) await DatabaseService.HandleCorruptionAsync(dbPath);

    // Step 3: Check pending_moves (crash recovery for Phase 10)
    await DatabaseService.ResolvePendingMovesAsync();

    // Step 4: Start IPC server (before window shown so second instances don't time out)
    IpcService.StartServer(applicationShutdownCts.Token);

    // Step 5: Session match + load tabs + restore geometry + apply theme
    var mainWindow = new MainWindow();
    await mainWindow.InitializeSessionAsync(); // loads tabs, geometry etc.

    // Step 6: Show window
    mainWindow.Show();
    mainWindow.FocusActiveTab();

    sw.Stop();
    LogService.Info($"Startup complete in {sw.ElapsedMilliseconds}ms");
    System.Diagnostics.Debug.WriteLine($"[JoJot] Startup: {sw.ElapsedMilliseconds}ms");

    // Step 7: Background migrations (STRT-03 — never block startup)
    _ = Task.Run(async () =>
    {
        try { await DatabaseService.RunPendingMigrationsAsync(); }
        catch (Exception ex) { LogService.Error("Migration failed", ex); }
    });
}
```

### Log Service (File + Debug)

```csharp
// Services/LogService.cs — Claude's discretion: simple rolling log
public static class LogService
{
    private static string _logPath = string.Empty;
    private static readonly object _fileLock = new();

    public static void Initialize(string directory)
    {
        _logPath = Path.Combine(directory, "jojot.log");
        // Roll if > 5MB (arbitrary; Claude's discretion)
        if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 5 * 1024 * 1024)
        {
            string rolled = _logPath + ".old";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(_logPath, rolled);
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex = null)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex is not null) line += $"\n  {ex}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (_fileLock)
        {
            try { File.AppendAllText(_logPath, line + "\n"); }
            catch { /* if log fails, only debug output is lost */ }
        }
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `System.Data.SQLite` (community) | `Microsoft.Data.Sqlite` (Microsoft) | .NET Core era | First-party support, .NET 10 aligned, no COM dependencies |
| XML serialization for IPC payloads | `System.Text.Json` with source gen | .NET 5–6 | Zero-allocation, faster, no XmlSerializer reflection overhead |
| `Dispatcher.BeginInvoke` | `Dispatcher.InvokeAsync` + `await` | .NET Framework 4.5+ | Proper async/await support; exceptions propagate correctly |
| `Application.DoEvents()` (WinForms) | `async/await` in WPF startup | .NET Core era | No reentrancy issues; proper async startup via `async void OnStartup` |
| Manual connection pool management | Single persistent connection (for single-process apps) | WAL mode adoption | WAL eliminates the need for multiple readers; single connection simplifies DATA-02 |

**Deprecated/outdated:**
- `System.Data.SQLite`: Still functional but community-maintained, heavier, COM-dependent registration. Use `Microsoft.Data.Sqlite` instead.
- `XmlSerializer` for IPC: No advantage over `System.Text.Json` for new code.
- `VisualBasicApplicationBase.IsSingleInstance`: VB.NET only; not applicable to C# WPF.

---

## Open Questions

1. **GetForegroundWindow P/Invoke availability**
   - What we know: `GetForegroundWindow` is a user32.dll export needed for `AttachThreadInput` focus activation.
   - What's unclear: Whether it needs explicit import or if WPF's `WindowInteropHelper` provides equivalent in .NET 10.
   - Recommendation: Use `[DllImport("user32.dll")]` explicitly — verified pattern across many WPF blog posts.

2. **Startup time baseline on target machine**
   - What we know: WPF cold-start is typically 1-3s including Defender scan (per dotnet/runtime#78379); ReadyToRun provides ~25% improvement for WPF.
   - What's unclear: What the actual cold/warm start time will be on the developer's machine for this specific app.
   - Recommendation: Per STATE.md action item — measure actual startup time and log it as Phase 1 baseline.

3. **PROC-05 process-stays-alive with `OnExplicitShutdown`: system tray or hidden?**
   - What we know: The requirements say the process stays alive when windows are closed, but there is no system tray requirement in Phase 1.
   - What's unclear: Whether there will be any visible indicator that JoJot is running with no window open (Phase 3 handles taskbar; Phase 1 just keeps the process alive).
   - Recommendation: In Phase 1, the process simply persists with no visible indicator. This is fine as the taskbar integration (Phase 3) will provide user-visible affordance.

---

## Sources

### Primary (HIGH confidence)
- [Microsoft.Data.Sqlite Overview](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) — usage patterns, installation
- [Microsoft.Data.Sqlite Connection Strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings) — WAL+Cache=Shared warning, keyword reference
- [NuGet: Microsoft.Data.Sqlite 10.0.3](https://www.nuget.org/packages/microsoft.data.sqlite/) — version 10.0.3 confirmed current (released 2026-02-10)
- [NamedPipeServerStream.WaitForConnectionAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream.waitforconnectionasync?view=net-9.0) — PipeOptions.Asynchronous requirement for CancellationToken confirmed
- [ReadyToRun Deployment Overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run) — PublishReadyToRun csproj property, RID requirement
- [SQLite WAL Documentation](https://sqlite.org/wal.html) — WAL mode mechanics, synchronous=NORMAL recommendation
- [SQLite PRAGMA Reference](https://sqlite.org/pragma.html) — integrity_check, quick_check, journal_mode

### Secondary (MEDIUM confidence)
- [Rick Strahl: Window Activation Headaches in WPF](https://weblog.west-wind.com/posts/2020/Oct/12/Window-Activation-Headaches-in-WPF) — AttachThreadInput pattern for cross-process window focus (well-known WPF resource)
- [AutoIt Consulting: Single-Instance WinForm with Mutex + Named Pipes](https://www.autoitconsulting.com/site/development/single-instance-winform-app-csharp-mutex-named-pipes/) — complete end-to-end pattern
- [dotnet/runtime issue #40674](https://github.com/dotnet/runtime/issues/40674) — confirms PipeOptions.Asynchronous requirement for WaitForConnectionAsync cancellation
- [SQLite recovery documentation](https://sqlite.org/recovery.html) — corruption recovery approach

### Tertiary (LOW confidence)
- [Community blogs on mutex GC pitfall in release builds](https://saebamini.com/Allowing-only-one-instance-of-a-C-app-to-run/) — GC.KeepAlive recommendation (widely repeated, plausible but not in official docs)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Microsoft.Data.Sqlite 10.0.3 confirmed on NuGet; all other libraries are in-box .NET 10
- Architecture: HIGH — Mutex+pipe pattern is well-documented; WAL configuration from official SQLite + Microsoft docs; ReadyToRun from official docs
- Pitfalls: MEDIUM-HIGH — Mutex GC and pipe cancellation pitfalls confirmed by official GitHub issues; window activation from official blog; Cache+WAL warning from official docs

**Research date:** 2026-03-02
**Valid until:** 2026-09-01 (stable libraries; ReadyToRun API unlikely to change)
