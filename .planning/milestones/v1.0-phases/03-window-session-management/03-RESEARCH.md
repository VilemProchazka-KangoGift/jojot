# Phase 3: Window & Session Management - Research

**Researched:** 2026-03-02
**Domain:** WPF multi-window lifecycle, geometry persistence, IPC routing, Win32 window placement
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Default window dimensions**
- Compact notepad-sized window (~500x600px) for first-time launch on a desktop with no saved geometry
- Centered on the primary monitor (or current monitor if multi-monitor)
- Freely resizable with a minimum size constraint (min ~300x400 so UI never breaks)
- Each desktop stores its own geometry independently in app_state (per-desktop, not global)

**Geometry persistence fidelity**
- Persist maximized/normal window state — add a `window_state` column to app_state (schema migration)
- If closed while maximized, reopen maximized; also remember pre-maximize size for un-maximize
- Save geometry on window close only (TASK-05 handler), not on every move/resize
- Multi-monitor: save absolute screen coordinates — window reopens on the same monitor if still connected
- Off-screen recovery: detect if saved position is off-screen and snap to nearest visible monitor edge; keep size intact, only adjust position

**Window lifecycle on desktop switch**
- No auto-create: windows only appear when the user explicitly clicks the taskbar icon or launches JoJot
- On startup, only create a window for the current desktop — other desktop windows are created on-demand via taskbar click
- No system tray icon: when all windows are closed, the process is invisible; user re-launches .exe to trigger IPC and get a window back
- When IPC activate arrives and the current desktop has no window, restore the previous session (reload tabs from database for this desktop — user gets back exactly where they left off)

**Close-and-relaunch feel**
- X button destroys the window (per TASK-05): save geometry, flush content, delete empty tabs, then fully destroy — process stays alive
- Changes the current hide behavior (Phase 1 PROC-05) to actual destroy — OnClosing no longer cancels the close
- Instant window reappearance when re-launching: since the process is already running, IPC path should be near-instant; target under 200ms to visible window
- No opening animation — window appears instantly; matches "zero friction" philosophy
- Editor auto-focused on window create: active tab's editor gets focus with cursor at saved position so user can start typing immediately

### Claude's Discretion
- Exact minimum window size values (around 300x400 but fine-tune based on layout)
- Exact default window dimensions (around 500x600 but adjust for DPI)
- Off-screen detection algorithm details
- Schema migration implementation for window_state column
- WindowActivationHelper enhancements for multi-window scenario

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TASK-01 | Left-click on taskbar icon: focus existing window or create new window for current desktop | IPC routing through per-desktop window registry; `ActivateCommand` already exists, needs routing logic |
| TASK-02 | Middle-click on taskbar icon: quick capture — new empty tab on current desktop, focused immediately | `NewTabCommand` IPC message already defined; second instance must detect middle-click and send `NewTabCommand` with desktop GUID |
| TASK-03 | Middle-click with no existing window: spawn window, load saved tabs, but create and focus new empty tab | Window creation on-demand from IPC handler; same code path as TASK-01 creation but with additional new-tab signal |
| TASK-04 | Restore saved window geometry (position, size) per desktop | `GetWindowPlacement`/`SetWindowPlacement` P/Invoke + `window_state` DB column; off-screen recovery via `Screen.AllScreens` |
| TASK-05 | Window close: flush content, delete empty tabs, save geometry, destroy window (process stays alive) | `OnClosing` changes from `e.Cancel=true`/`Hide()` to save+destroy; `FlushAndClose()` stub needs full implementation |
</phase_requirements>

---

## Summary

Phase 3 replaces the single-window model with a per-desktop window registry (`Dictionary<string, MainWindow>`) held in `App`. The three main technical areas are: (1) window lifecycle — create-on-demand, destroy-on-close (not hide), and proper WPF resource cleanup; (2) geometry persistence — using Win32 `GetWindowPlacement`/`SetWindowPlacement` to capture position/size/state atomically plus a schema migration to add `window_state` to `app_state`; and (3) IPC routing — `App.HandleIpcCommand` must resolve the current desktop GUID and dispatch to the correct window or create one if absent.

The biggest technical gap in the existing code is that `App._mainWindow` is a single field rather than a registry, `OnClosing` hides instead of destroying, and the geometry columns in `app_state` have no read/write methods in `DatabaseService`. All three must be addressed in this phase. The middle-click path (TASK-02/03) is also new: the second instance must detect whether the OS launched it via a middle-click on the taskbar button. That detection is not natively available via a flag — the practical approach is to launch JoJot with a `--new-tab` command-line argument from a separate shortcut, or (more correctly for standard left-click vs middle-click) intercept the IPC side because the OS only launches a new EXE instance; WPF does not receive the middle-click event directly.

The confirmed standard approach: detect middle-click by having the second instance send `NewTabCommand` when launched with a `--new-tab` flag (or a separate registered middle-click action via jumplist/taskbar thumbnail), while left-click sends `ActivateCommand`. This is what the existing `NewTabCommand` IPC type was designed for.

**Primary recommendation:** Replace `App._mainWindow` with `Dictionary<string, MainWindow>`, implement geometry read/write in `DatabaseService`, migrate schema to add `window_state`, and update `OnClosing` to save-and-destroy. The pattern for window creation, restoration, geometry, and IPC routing is all well-understood and low-risk.

---

## Standard Stack

### Core

| Library / API | Version | Purpose | Why Standard |
|---|---|---|---|
| `System.Windows.Window` | .NET 10 built-in | Window creation, show/hide/close lifecycle | WPF native — no alternatives needed |
| `user32.dll` `GetWindowPlacement` / `SetWindowPlacement` | Win32 | Atomic save/restore of position, size, and state including maximized | Handles workspace-coordinate normalization, maximized-restore size, and multi-monitor correctly; the `Window.Left`/`Width` approach does not preserve pre-maximize size |
| `System.Windows.Forms.Screen` | .NET 10 via `System.Windows.Forms` reference | Off-screen detection via `Screen.AllScreens` bounds | Official Microsoft API for enumerating monitor working areas; already allowed in WPF via framework reference |
| `System.Windows.Interop.HwndSource` | .NET 10 built-in | `WndProc` hook for window message interception | Required to receive Win32 messages (e.g., `WM_NCMBUTTONDOWN`) in WPF windows |
| `System.Windows.Interop.WindowInteropHelper` | .NET 10 built-in | Get `HWND` from `Window` | Required for P/Invoke calls and `HwndSource` |
| `Microsoft.Data.Sqlite` | Already in project | Schema migration (`ALTER TABLE app_state ADD COLUMN window_state`) | Already established in codebase |

### Supporting

| Library / API | Version | Purpose | When to Use |
|---|---|---|---|
| `WindowActivationHelper` (existing) | Project | Bring window to foreground cross-process | Already handles `AttachThreadInput` + `SetForegroundWindow`; needs no changes for multi-window — just pass the correct window reference |
| `VirtualDesktopService.CurrentDesktopGuid` (existing) | Project | Determine which desktop the IPC command targets | Key for window registry lookup |
| `IpcMessage` / `NewTabCommand` / `ActivateCommand` (existing) | Project | Wire second-instance signals to correct handler | Already JSON-polymorphic; `NewTabCommand` carries `DesktopGuid` or the first instance resolves it from current desktop |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|---|---|---|
| `GetWindowPlacement` / `SetWindowPlacement` | `Window.Left` / `Top` / `Width` / `Height` + `Window.WindowState` | `WindowState` approach does NOT preserve the pre-maximize restore rect; `GetWindowPlacement.rcNormalPosition` carries it natively. Use `GetWindowPlacement`. |
| `Screen.AllScreens` (WinForms) | `WpfScreenHelper` NuGet (micdenny) | NuGet adds a dependency; `Screen.AllScreens` is built into the framework and sufficient for bounds checking |
| `WndProc` hook for middle-click | OS Jumplist / thumbnail button API | Jumplist requires shell integration setup; for a single-EXE approach, sending `--new-tab` arg from second instance is simpler |

**Installation:** No new packages needed — everything is built-in or already referenced.

---

## Architecture Patterns

### Recommended Project Structure

No new folders needed. Changes concentrate in:

```
JoJot/
├── App.xaml.cs                  # _windows registry, HandleIpcCommand routing, window factory
├── MainWindow.xaml.cs           # OnClosing → save+destroy; FlushAndClose full impl; geometry restore
├── Services/
│   ├── DatabaseService.cs       # Add: GetWindowGeometryAsync, SaveWindowGeometryAsync; schema migration
│   └── WindowActivationHelper.cs # No changes needed
└── Models/
    └── WindowGeometry.cs        # NEW: plain record to carry position/size/state between DB and window
```

### Pattern 1: Per-Desktop Window Registry

**What:** `App` holds `Dictionary<string, MainWindow> _windows` keyed by desktop GUID string. On IPC command, look up the window for the current desktop; create if absent.

**When to use:** Everywhere a window reference is needed — IPC handler, desktop-switch handler, startup.

**Example:**
```csharp
// In App.xaml.cs
private readonly Dictionary<string, MainWindow> _windows = new();

private async void HandleIpcCommand(IpcMessage message)
{
    string desktopGuid = VirtualDesktopService.CurrentDesktopGuid;

    switch (message)
    {
        case ActivateCommand:
            await EnsureWindowForDesktop(desktopGuid, focusOnly: true);
            break;

        case NewTabCommand:
            await EnsureWindowForDesktop(desktopGuid, focusOnly: false, createTab: true);
            break;
    }
}

private async Task EnsureWindowForDesktop(string desktopGuid, bool focusOnly, bool createTab = false)
{
    if (_windows.TryGetValue(desktopGuid, out var existingWindow))
    {
        WindowActivationHelper.ActivateWindow(existingWindow);
        if (createTab) existingWindow.RequestNewTab();
    }
    else
    {
        var window = await CreateWindowForDesktop(desktopGuid);
        if (createTab) window.RequestNewTab();
    }
}
```

### Pattern 2: Create-On-Demand Window Factory

**What:** `App.CreateWindowForDesktop()` creates a `MainWindow`, wires its `Closed` event to remove it from the registry, restores saved geometry, and shows it.

**When to use:** Any time the registry lookup returns null.

**Example:**
```csharp
private async Task<MainWindow> CreateWindowForDesktop(string desktopGuid)
{
    var window = new MainWindow(desktopGuid);

    // Wire Closed event to remove from registry
    window.Closed += (_, _) => _windows.Remove(desktopGuid);

    // Restore geometry before Show()
    var geo = await DatabaseService.GetWindowGeometryAsync(desktopGuid);
    window.ApplyGeometry(geo);   // sets Left, Top, Width, Height, WindowState

    _windows[desktopGuid] = window;
    window.Show();

    // Set title
    var info = VirtualDesktopService.GetCurrentDesktopInfo();
    window.UpdateDesktopTitle(info.Name, info.Index);

    WindowActivationHelper.ActivateWindow(window);
    return window;
}
```

**Critical:** Subscribe to `window.Closed` — not `window.Closing` — so the registry entry is cleaned up only after the window is actually destroyed. The WPF docs confirm: "A window can't be reopened after it's closed." After `Closed` fires, the `MainWindow` instance is dead and must be replaced with a new `new MainWindow()`.

### Pattern 3: Geometry Save/Restore via GetWindowPlacement

**What:** Use Win32 `GetWindowPlacement` to save the full window state atomically (including pre-maximize rect), and `SetWindowPlacement` to restore it.

**Why GetWindowPlacement over Window.Left/Width:** The WPF `Window.Left`, `Top`, `Width`, `Height` properties always reflect the **normal** state even when maximized. They do not store the restore rect. `GetWindowPlacement.rcNormalPosition` carries both the normal rect AND the `showCmd` (SW_SHOWMAXIMIZED / SW_SHOWNORMAL), so restore is single-call and correct.

**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement

[StructLayout(LayoutKind.Sequential)]
private struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
[DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);

// Save on close
public WindowGeometry CaptureGeometry()
{
    var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
    IntPtr hwnd = new WindowInteropHelper(this).Handle;
    GetWindowPlacement(hwnd, ref wp);

    return new WindowGeometry(
        Left: wp.rcNormalPosition.Left,
        Top: wp.rcNormalPosition.Top,
        Width: wp.rcNormalPosition.Right - wp.rcNormalPosition.Left,
        Height: wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top,
        IsMaximized: wp.showCmd == SW_SHOWMAXIMIZED);
}

// Restore on window creation (call before Show())
public void ApplyGeometry(WindowGeometry? geo)
{
    if (geo is null)
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 500; Height = 600;
        return;
    }

    // Off-screen recovery first
    var corrected = OffScreenHelper.ClampToNearestScreen(geo);

    Left = corrected.Left;
    Top = corrected.Top;
    Width = corrected.Width;
    Height = corrected.Height;
    WindowStartupLocation = WindowStartupLocation.Manual;

    if (corrected.IsMaximized)
        WindowState = WindowState.Maximized;
}
```

**Important:** `WINDOWPLACEMENT` coordinates are **workspace coordinates** (offset by taskbar position), not screen coordinates. `GetWindowPlacement` and `SetWindowPlacement` are self-consistent: always use them together, never mix with `SetWindowPos` using the same coordinates.

### Pattern 4: Off-Screen Recovery

**What:** Before applying saved position, check that at least part of the window overlaps a live screen's working area. If not, snap position to the nearest screen edge.

**When to use:** Always, as monitors can be disconnected between sessions.

**Example:**
```csharp
// Uses System.Windows.Forms.Screen (add reference to System.Windows.Forms if not present)
public static WindowGeometry ClampToNearestScreen(WindowGeometry geo)
{
    var windowRect = new System.Drawing.Rectangle(
        (int)geo.Left, (int)geo.Top, (int)geo.Width, (int)geo.Height);

    // Check if any screen contains at least the top-left 50x50 area
    bool isVisible = System.Windows.Forms.Screen.AllScreens.Any(s =>
        s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle(
            (int)geo.Left, (int)geo.Top, 50, 50)));

    if (isVisible) return geo;

    // Snap to nearest screen's working area
    var nearest = System.Windows.Forms.Screen.AllScreens
        .OrderBy(s => Distance(s.WorkingArea, (int)geo.Left, (int)geo.Top))
        .First();

    double newLeft = Math.Max(nearest.WorkingArea.Left,
        Math.Min(geo.Left, nearest.WorkingArea.Right - geo.Width));
    double newTop = Math.Max(nearest.WorkingArea.Top,
        Math.Min(geo.Top, nearest.WorkingArea.Bottom - geo.Height));

    return geo with { Left = newLeft, Top = newTop };
    // Keep size intact per user decision
}
```

### Pattern 5: OnClosing — Destroy Not Hide

**What:** Remove the `e.Cancel = true; Hide()` from `MainWindow.OnClosing`. Instead let the close proceed naturally. Do sync save in `OnClosing` (before the window dies), or via async in a `Closing` event handler.

**Critical:** The WPF docs state "a window can't be reopened after it's closed." Once `Close()` is called and `Closing` is not cancelled, `Closed` fires and the HWND is destroyed. The `App` registry's `Closed` handler removes the entry. Next IPC activate creates a fresh `new MainWindow()`.

**Example:**
```csharp
protected override async void OnClosing(CancelEventArgs e)
{
    base.OnClosing(e);
    // Do NOT set e.Cancel = true anymore (Phase 3 change from Phase 1)

    // Save geometry synchronously before window handle is gone
    var geo = CaptureGeometry();
    // Fire-and-forget is acceptable here since the process stays alive
    _ = DatabaseService.SaveWindowGeometryAsync(_desktopGuid, geo);

    // Flush content and delete empty tabs (stub for now, full impl in Phase 6)
    LogService.Info($"Window closing for desktop {_desktopGuid} — geometry saved");
}
```

### Pattern 6: Schema Migration for window_state

**What:** `app_state` already has geometry columns (`window_left`, `window_top`, `window_width`, `window_height`) but no `window_state`. Add it via background migration.

**SQLite constraint:** `ALTER TABLE ... ADD COLUMN` with a nullable column (no NOT NULL + no DEFAULT needed if storing as INTEGER NULL) is safe and idempotent-friendly.

**Example:**
```csharp
// In DatabaseService.RunPendingMigrationsAsync()
public static async Task RunPendingMigrationsAsync()
{
    // Migration 1: add window_state column to app_state
    // Use "IF NOT EXISTS" workaround: check column existence first
    bool hasWindowState = await ColumnExistsAsync("app_state", "window_state");
    if (!hasWindowState)
    {
        await ExecuteNonQueryAsync(
            "ALTER TABLE app_state ADD COLUMN window_state TEXT;");
        LogService.Info("Migration: added window_state column to app_state");
    }
}

private static async Task<bool> ColumnExistsAsync(string table, string column)
{
    bool found = false;
    await ExecuteReaderAsync(
        $"PRAGMA table_info({table});",
        reader =>
        {
            while (reader.Read())
            {
                if (reader.GetString(1) == column) { found = true; break; }
            }
        });
    return found;
}
```

**Note:** SQLite `ALTER TABLE ADD COLUMN` does not support `IF NOT EXISTS` syntax, so the column-existence check via `PRAGMA table_info` is the correct approach. The migration runs in the background after window shown (STRT-03, DATA-07), so it does not block startup.

### Anti-Patterns to Avoid

- **Mixing GetWindowPlacement coords with SetWindowPos:** `WINDOWPLACEMENT` uses workspace coordinates; `SetWindowPos` uses screen coordinates. Mixing causes drift toward the screen top-left. Never mix them.
- **Saving geometry on every resize/move:** Expensive and creates DB contention. Save on `Closing` only, as decided.
- **Reusing a closed `Window` instance:** WPF is explicit: a closed window cannot be reopened. Always `new MainWindow()` when recreating.
- **Hiding instead of destroying in Phase 3:** The Phase 1 `OnClosing` cancels and hides. Phase 3 must remove the cancel and let close proceed. Hidden windows still consume HWND, thread-input queues, and taskbar presence. Per user decision, destroy is correct.
- **Using `Window.Left` / `Window.Top` to detect off-screen when maximized:** These properties still report normal-state values even when maximized. Use `GetWindowPlacement.rcNormalPosition` for saved coordinates.
- **Calling `DatabaseService.GetWindowGeometryAsync` with inline SQL:** Always use parameterized queries (avoid the `EscapeSql` pattern already marked as legacy in `StartupService`).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---|---|---|---|
| Save/restore window position + state atomically | Custom struct + multiple DB columns + WPF properties | `GetWindowPlacement` + `SetWindowPlacement` | Handles pre-maximize rect, workspace coords, and all three states in one call |
| Multi-monitor bounds checking | Custom monitor enumeration via `EnumDisplayMonitors` | `System.Windows.Forms.Screen.AllScreens` | Already available in .NET 10, handles DPI-aware bounds, handles disconnected monitors |
| Focus stealing prevention | Custom AttachThreadInput logic | Existing `WindowActivationHelper.ActivateWindow()` | Already handles cross-thread focus in the codebase; just pass the right window |

**Key insight:** The entire geometry persistence story is solved by two Win32 calls. The complexity is in the edge cases (off-screen, schema migration, multi-window registry), not the geometry read/write itself.

---

## Common Pitfalls

### Pitfall 1: Closed Window Cannot Be Reopened

**What goes wrong:** Code calls `window.Show()` on a `MainWindow` instance after `window.Close()` has completed — throws `InvalidOperationException`.

**Why it happens:** WPF destroys the HWND on close. The .NET object becomes a zombie: exists in memory but cannot be shown again.

**How to avoid:** In the `Closed` event handler, remove the entry from `_windows`. In `EnsureWindowForDesktop`, always do `new MainWindow()` when the registry has no entry.

**Warning signs:** `InvalidOperationException: Cannot set Visibility or call Show, ShowDialog, Close, or Hide while window is closing` or after `Closed`.

### Pitfall 2: WINDOWPLACEMENT Coordinate System Mismatch

**What goes wrong:** Restore uses `GetWindowPlacement.rcNormalPosition` values but then calls `window.Left = rcNormalPosition.Left` — window appears offset by taskbar height.

**Why it happens:** `WINDOWPLACEMENT` uses workspace coordinates (origin at top-left of work area, excluding taskbar). WPF `Window.Left` uses screen coordinates.

**How to avoid:** Use `SetWindowPlacement` to restore (not `Window.Left`/`Top`). The two APIs are self-consistent. Alternatively, if you must use WPF properties, convert workspace→screen coords via `SystemParameters.WorkArea` (single-monitor only) or avoid entirely.

**Warning signs:** Window appears shifted upward by the taskbar height on systems with top/bottom taskbar.

### Pitfall 3: Schema Migration Before DB Open

**What goes wrong:** `RunPendingMigrationsAsync` runs before `DatabaseService.OpenAsync` completes, or `_connection` is null.

**Why it happens:** Background task scheduling race condition.

**How to avoid:** Existing pattern is correct — migrations are in `Step 11` (background after window shown), DB opens in `Step 4`. Do not change the startup ordering.

**Warning signs:** `NullReferenceException` in `ExecuteNonQueryAsync` on `_connection!.CreateCommand()`.

### Pitfall 4: IPC Desktop GUID Resolved Too Early

**What goes wrong:** `HandleIpcCommand` resolves `VirtualDesktopService.CurrentDesktopGuid` at IPC receive time, but the desktop has switched since the second instance sent the command.

**Why it happens:** There is a race between when the second instance detects the current desktop and when the first instance processes the message.

**How to avoid:** Per the existing design, the second instance sends the command and exits immediately. The first instance resolves the desktop from `VirtualDesktopService.CurrentDesktopGuid` at the time it handles the command — this is the correct live state. The `ShowDesktopCommand(DesktopGuid)` model exists for explicit desktop routing if needed in the future.

**Warning signs:** Window appears on wrong desktop after rapid desktop switching.

### Pitfall 5: Closing Event vs Closed Event for Registry Cleanup

**What goes wrong:** Registry cleanup (`_windows.Remove(desktopGuid)`) is done in `OnClosing` — but `Closing` can be cancelled. If another code path cancels the close, the window is removed from the registry while still alive.

**Why it happens:** `Closing` is pre-close (cancellable); `Closed` is post-close (not cancellable).

**How to avoid:** Always remove from registry in the `Closed` event handler, not in `Closing`. Save geometry in `Closing` (where the window handle is still valid), clean up registry in `Closed`.

**Warning signs:** IPC activate creates a new window but the old (still visible) window is unreachable.

### Pitfall 6: Middle-Click Second-Instance Launch — No Built-In Flag

**What goes wrong:** Assuming WPF or Windows provides a flag in `StartupEventArgs` indicating whether the process was launched by a middle-click on the taskbar button.

**Why it happens:** Windows simply launches a new EXE instance for taskbar middle-click (on apps configured this way), with no distinguishing signal.

**How to avoid:** The correct approach is to register the JoJot EXE with a command-line argument convention: standard left-click launches normally (sends `ActivateCommand`), while the middle-click action is mapped via a separate Windows JumpList entry or a secondary shortcut that appends `--new-tab` to the command line. The second instance receiving `--new-tab` sends `NewTabCommand` instead of `ActivateCommand`. The `NewTabCommand` IPC type already exists in the codebase for this purpose. This approach is LOW confidence on the middle-click OS mechanics and needs validation.

**Warning signs:** Middle-click on taskbar does nothing different from left-click.

---

## Code Examples

Verified patterns from official sources:

### Window Registry in App

```csharp
// App.xaml.cs — replaces single _mainWindow field
private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

// Startup: create only for current desktop
private async Task ShowWindowForCurrentDesktop()
{
    string guid = VirtualDesktopService.CurrentDesktopGuid;
    if (!_windows.ContainsKey(guid))
        await CreateWindowForDesktop(guid);
}

private async Task<MainWindow> CreateWindowForDesktop(string desktopGuid)
{
    var geo = await DatabaseService.GetWindowGeometryAsync(desktopGuid);
    var window = new MainWindow(desktopGuid);
    window.Closed += (_, _) => _windows.Remove(desktopGuid);
    window.ApplyGeometry(geo);
    _windows[desktopGuid] = window;
    window.Show();
    WindowActivationHelper.ActivateWindow(window);
    return window;
}
```

### GetWindowGeometryAsync / SaveWindowGeometryAsync in DatabaseService

```csharp
// Source: existing DatabaseService patterns (parameterized queries, _writeLock)

public static async Task<WindowGeometry?> GetWindowGeometryAsync(string desktopGuid)
{
    WindowGeometry? result = null;
    await _writeLock.WaitAsync();
    try
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT window_left, window_top, window_width, window_height, window_state
            FROM app_state WHERE desktop_guid = @guid;
            """;
        cmd.Parameters.AddWithValue("@guid", desktopGuid);
        using var reader = await cmd.ExecuteReaderAsync();
        if (reader.Read() && !reader.IsDBNull(0))
        {
            result = new WindowGeometry(
                Left:        reader.GetDouble(0),
                Top:         reader.GetDouble(1),
                Width:       reader.GetDouble(2),
                Height:      reader.GetDouble(3),
                IsMaximized: reader.IsDBNull(4) ? false : reader.GetString(4) == "Maximized");
        }
    }
    finally { _writeLock.Release(); }
    return result;
}

public static async Task SaveWindowGeometryAsync(string desktopGuid, WindowGeometry geo)
{
    await _writeLock.WaitAsync();
    try
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE app_state
            SET window_left=@l, window_top=@t, window_width=@w, window_height=@h, window_state=@s
            WHERE desktop_guid=@guid;
            """;
        cmd.Parameters.AddWithValue("@l", geo.Left);
        cmd.Parameters.AddWithValue("@t", geo.Top);
        cmd.Parameters.AddWithValue("@w", geo.Width);
        cmd.Parameters.AddWithValue("@h", geo.Height);
        cmd.Parameters.AddWithValue("@s", geo.IsMaximized ? "Maximized" : "Normal");
        cmd.Parameters.AddWithValue("@guid", desktopGuid);
        await cmd.ExecuteNonQueryAsync();
    }
    finally { _writeLock.Release(); }
}
```

### WindowGeometry Record

```csharp
// JoJot/Models/WindowGeometry.cs
namespace JoJot.Models;

/// <summary>
/// Captured geometry for a desktop's window. Persisted to app_state.
/// Left/Top/Width/Height are in WPF device-independent units (1/96 inch).
/// </summary>
public sealed record WindowGeometry(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
```

### WINDOWPLACEMENT P/Invoke

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

private const int SW_SHOWNORMAL    = 1;
private const int SW_SHOWMAXIMIZED = 3;

[DllImport("user32.dll", SetLastError = true)]
private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

[DllImport("user32.dll", SetLastError = true)]
private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
```

### Off-Screen Clamp

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.screen.allscreens
using WinForms = System.Windows.Forms;

public static WindowGeometry ClampToNearestScreen(WindowGeometry geo)
{
    var testRect = new System.Drawing.Rectangle(
        (int)geo.Left, (int)geo.Top, 50, 50);   // top-left corner must be visible

    bool visible = WinForms.Screen.AllScreens.Any(s =>
        s.WorkingArea.IntersectsWith(testRect));

    if (visible) return geo;

    // Find nearest screen by distance from saved top-left
    var nearest = WinForms.Screen.AllScreens
        .OrderBy(s =>
        {
            int dx = Math.Max(s.WorkingArea.Left - (int)geo.Left, 0) +
                     Math.Max((int)geo.Left - s.WorkingArea.Right, 0);
            int dy = Math.Max(s.WorkingArea.Top - (int)geo.Top, 0) +
                     Math.Max((int)geo.Top - s.WorkingArea.Bottom, 0);
            return dx * dx + dy * dy;
        })
        .First();

    var wa = nearest.WorkingArea;
    double newLeft = Math.Clamp(geo.Left, wa.Left, wa.Right - geo.Width);
    double newTop  = Math.Clamp(geo.Top,  wa.Top,  wa.Bottom - geo.Height);

    return geo with { Left = newLeft, Top = newTop };
}
```

### IPC Routing Update

```csharp
// App.xaml.cs HandleIpcCommand — updated for multi-window
private async void HandleIpcCommand(IpcMessage message)
{
    string desktopGuid = VirtualDesktopService.CurrentDesktopGuid;

    switch (message)
    {
        case ActivateCommand:
            LogService.Info($"IPC: activate — desktop {desktopGuid}");
            if (_windows.TryGetValue(desktopGuid, out var w))
                WindowActivationHelper.ActivateWindow(w);
            else
                await CreateWindowForDesktop(desktopGuid);
            break;

        case NewTabCommand:
            LogService.Info($"IPC: new-tab — desktop {desktopGuid}");
            if (_windows.TryGetValue(desktopGuid, out var w2))
            {
                WindowActivationHelper.ActivateWindow(w2);
                w2.RequestNewTab();   // stub until Phase 4 implements tabs
            }
            else
            {
                var newWindow = await CreateWindowForDesktop(desktopGuid);
                newWindow.RequestNewTab();
            }
            break;
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|---|---|---|---|
| `Window.Left` / `Top` for geometry save | `GetWindowPlacement` P/Invoke | Established Win32 pattern | Correctly captures pre-maximize rect and workspace coords |
| Single `_mainWindow` field | `Dictionary<string, MainWindow>` keyed by desktop GUID | Phase 3 (this phase) | Enables per-desktop window management |
| `OnClosing` cancels and hides | `OnClosing` saves geometry, lets close proceed | Phase 3 (this phase) | Satisfies TASK-05; window truly destroyed |
| `RunPendingMigrationsAsync` is a no-op stub | Adds `window_state` column via `ALTER TABLE` | Phase 3 (this phase) | Enables maximized state persistence |

**Deprecated/outdated:**
- Phase 1 `OnClosing` hide behavior: replaced by save-and-destroy in Phase 3. The comment `// Phase 3 will handle multi-window visibility per desktop` in `App.xaml.cs` confirms the intent.
- `App._mainWindow` single field: replaced by `_windows` dictionary.

---

## Open Questions

1. **Middle-click taskbar detection mechanism**
   - What we know: Windows launches a new EXE instance for taskbar middle-click on apps that have "launch new instance" behavior; the IPC `NewTabCommand` type is already designed for this signal; Phase 1/2 have `ShowDesktopCommand` for desktop routing
   - What's unclear: Whether JoJot's taskbar button is configured (or can be configured) to trigger a new EXE instance on middle-click; Windows 11 taskbar middle-click behavior differs from Windows 10; there is no built-in WPF hook for this
   - Recommendation: Plan for `--new-tab` command-line argument from a secondary shortcut or JumpList entry as the middle-click trigger. The second instance sends `NewTabCommand`; first instance routes it. If the OS does not trigger a new instance on middle-click (the default behavior is usually "open new instance" only if the app has `AppUserModelId` set and `SingleInstanceOnly=false`), TASK-02 may require a WndProc hook (`WM_NCMBUTTONDOWN` on the taskbar thumbnail) in the *first* instance — which is a significantly different approach. Flag as LOW confidence and validate early in Phase 3 planning.

2. **DPI awareness for geometry coordinates**
   - What we know: `WINDOWPLACEMENT.rcNormalPosition` is in workspace coordinates at the system DPI. WPF uses device-independent units (DIUs). `Screen.AllScreens` returns physical pixel coordinates.
   - What's unclear: Whether coordinates from `GetWindowPlacement` (Win32 workspace pixels) and from WPF properties (`Window.Left` in DIUs at 96 DPI) will be consistent on non-100% DPI systems. The project targets .NET 10 which has per-monitor DPI v2 awareness improvements.
   - Recommendation: Use `GetWindowPlacement`/`SetWindowPlacement` exclusively (never mix with WPF `Window.Left`/`Top`). This self-consistent pair handles DPI automatically. For off-screen detection, the `Screen.WorkingArea` is in physical pixels — if using WPF DIU coords for comparison, apply DPI scaling factor. This is medium complexity but well-understood. Flag for validation on a 150% DPI system.

3. **Tab flush in OnClosing without tab system**
   - What we know: Phase 3 must implement `FlushAndClose()` (TASK-05 says "flush content, delete empty tabs, save geometry, destroy window"). But tabs and editor content are Phase 4+.
   - What's unclear: Should `OnClosing` in Phase 3 have a stub or full implementation?
   - Recommendation: Phase 3 implements geometry save + actual destroy (the structural change). Tab flush is a stub call that does nothing until Phase 6 provides the content layer. Plan accordingly — don't block Phase 3 on Phase 4+ work.

---

## Sources

### Primary (HIGH confidence)
- `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement` — WINDOWPLACEMENT structure fields, coordinate system, workspace vs screen coordinate distinction
- `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement` — GetWindowPlacement/SetWindowPlacement P/Invoke signatures and length field requirement
- `https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/?view=netdesktop-10.0` — WPF Window lifecycle (opening, closing, Closed event, "cannot be reopened after closed"), WindowState values, RestoreBounds, ShowActivated, multi-window ownership
- `https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.screen.allscreens?view=windowsdesktop-9.0` — Screen.AllScreens for off-screen detection
- Project source files: `App.xaml.cs`, `MainWindow.xaml.cs`, `DatabaseService.cs`, `IpcService.cs`, `VirtualDesktopService.cs`, `WindowActivationHelper.cs`, `IpcMessage.cs` — ground truth for existing code and integration points

### Secondary (MEDIUM confidence)
- `https://gist.github.com/AlonAm/fdebf420efcc7fc23933612becf8d1be` — WPF WindowPlacement gist confirming P/Invoke pattern and length initialization
- `https://engy.us/blog/2010/03/08/saving-window-size-and-location-in-wpf-and-winforms/` — David Rickard (ex-Microsoft) on WPF geometry persistence, workspace coordinate pitfall
- `https://learn.microsoft.com/en-us/samples/microsoft/wpf-samples/save-window-placement-state-sample/` — Official Microsoft WPF sample for window placement state

### Tertiary (LOW confidence)
- WebSearch results on taskbar middle-click detection — no authoritative source found; treated as open question

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — built-in .NET/Win32 APIs, confirmed in official docs
- Architecture (registry, factory, lifecycle): HIGH — confirmed by WPF docs (window cannot reopen after close), existing codebase patterns match
- Geometry persistence: HIGH — GetWindowPlacement/SetWindowPlacement is the established pattern; workspace coordinate warning is from official docs
- Off-screen recovery: MEDIUM — Screen.AllScreens approach is correct, DPI interaction on non-100% displays needs validation
- Middle-click TASK-02: LOW — OS behavior not fully confirmed; open question flagged

**Research date:** 2026-03-02
**Valid until:** 2026-04-01 (stable Win32/WPF APIs; 30-day window)
