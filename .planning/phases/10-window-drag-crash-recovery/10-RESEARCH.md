# Phase 10: Window Drag & Crash Recovery - Research

**Researched:** 2026-03-03
**Domain:** WPF/COM virtual desktop interop, window drag detection, crash recovery
**Confidence:** HIGH

## Summary

Phase 10 implements inter-desktop window drag detection, a lock overlay for conflict resolution, and crash recovery via the pending_moves table. All core infrastructure exists: the COM notification interface (`IVirtualDesktopNotification`) is registered and working, the `ViewVirtualDesktopChanged` callback fires when a window moves between desktops (currently a no-op), the `pending_moves` table schema is defined, and `IVirtualDesktopManager.MoveWindowToDesktop` is available for the "Go back" flow.

The main work is: (1) implement the detection chain from COM callback through VirtualDesktopService to MainWindow, (2) build the lock overlay XAML and interaction logic, (3) add `MoveWindowToDesktop` wrapper to VirtualDesktopInterop, (4) implement pending_moves CRUD in DatabaseService, and (5) wire crash recovery in the App.xaml.cs startup sequence.

**Primary recommendation:** Build on existing COM notification infrastructure. The `ViewVirtualDesktopChanged` callback is the detection mechanism. Add a new `WindowMovedToDesktop` event on VirtualDesktopService, wire it in MainWindow to show the lock overlay, and use the existing confirmation overlay pattern as a template.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Lock overlay: centered vertically, card-style over semi-transparent dark overlay (rgba 0,0,0,0.65)
- Context message above buttons showing target desktop name + situation
- Button labels: "Keep here" (reparent), "Merge notes" (merge), "Go back" (cancel)
- Same layout for 2-button and 3-button cases; buttons change dynamically
- Content visible but non-interactive behind overlay
- Merged tabs append at bottom of unpinned section in target window
- Pinned tabs from source stay pinned in target (join bottom of existing pins)
- Toast after merge: "Merged N notes from {source desktop name}"
- Target window keeps its currently active tab focused
- Lock overlay: 150ms fade-in animation
- Cancel: overlay fades out over 150ms while COM moves window back
- Reparent: overlay fades out over 150ms, window title updates to new desktop
- Merge: overlay fades out, standard WPF window close on dragged window
- Cancel failure: replace "Go back" with "Retry" + instruction text below
- "Keep here" and "Merge notes" remain available as alternatives during cancel failure
- Warning badge: append "(misplaced)" to window title on GUID mismatch
- Uses existing title update system, no custom icons
- Misplaced window gaining focus shows lock overlay automatically
- Crash recovery: read pending_moves on startup, restore to origin desktop
- Recovery toast: "Recovered window from interrupted move"
- Integration point: existing stub at App.xaml.cs:124

### Claude's Discretion
- Detection mechanism for window-to-desktop drag (ViewVirtualDesktopChanged is the answer)
- Exact overlay XAML structure and button styling
- pending_moves write/read implementation details
- Timing of overlay appearance relative to COM notification
- Error handling for COM failures during reparent/merge operations
- How "second drag while overlay active is ignored" is implemented (DRAG-08)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DRAG-01 | Detect window drag to another desktop via IVirtualDesktopNotification | ViewVirtualDesktopChanged callback + GetWindowDesktopId to detect which window moved |
| DRAG-02 | Write pending_moves row immediately on detection; apply lock overlay | DatabaseService CRUD for pending_moves, MainWindow lock overlay |
| DRAG-03 | Lock overlay: semi-transparent dark (rgba 0,0,0,0.65), content visible but non-interactive | XAML overlay pattern matches existing ConfirmationOverlay |
| DRAG-04 | Reparent button: re-scope window and all notes to new desktop | Update _windows dict in App, update notes.desktop_guid in DB |
| DRAG-05 | Merge button: append tabs to existing window, close dragged window | Reuse MigrateTabsAsync pattern from orphan recovery (Phase 8.3) |
| DRAG-06 | Cancel button: move window back to original desktop via MoveWindowToDesktop | New MoveWindowToDesktop wrapper in VirtualDesktopInterop |
| DRAG-07 | Cancel failure: replace Cancel with Retry + manual instruction message | Dynamic button visibility toggling in lock overlay |
| DRAG-08 | Second drag while overlay active is ignored | Boolean guard flag _isDragOverlayActive in MainWindow |
| DRAG-09 | Crash recovery: pending_moves rows on startup restore window to origin | App.xaml.cs startup sequence, after session matching |
| DRAG-10 | Persistent warning badge in title bar when GUID mismatch | Title format: "JoJot — {name} (misplaced)" + auto-show overlay on focus |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF (.NET 10) | net10.0-windows | UI framework | Project framework |
| IVirtualDesktopNotification COM | Win11 23H2/24H2 | Desktop event callbacks | Already registered and working in project |
| IVirtualDesktopManager COM | Win11 documented API | MoveWindowToDesktop | Only official API for programmatic window moves |
| Microsoft.Data.Sqlite | Existing | pending_moves CRUD | Already used project-wide |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Windows.Media.Animation | Built-in WPF | Overlay fade animation | 150ms DoubleAnimation on Opacity |
| System.Windows.Interop.HwndSource | Built-in WPF | Get HWND for COM calls | Already used for hotkey registration |

### Alternatives Considered
None — all infrastructure already exists in the project.

## Architecture Patterns

### Pattern 1: COM Callback → Service Event → UI Handler
**What:** The existing pattern for desktop events flows COM callback → VirtualDesktopNotificationListener event → VirtualDesktopService handler → public event → MainWindow handler.
**When to use:** All virtual desktop notifications follow this chain.
**Implementation:**

```
ViewVirtualDesktopChanged(IntPtr view)
  → new event: WindowViewChanged?.Invoke(view)
  → VirtualDesktopService.OnWindowViewChanged(view)
    → determine which window was affected via GetWindowDesktopId
    → determine target desktop GUID
    → fire public event: WindowMovedToDesktop?.Invoke(windowHwnd, fromGuid, toGuid)
  → MainWindow handler: ShowDragOverlay(fromGuid, toGuid)
```

### Pattern 2: Lock Overlay (modeled on ConfirmationOverlay)
**What:** A full-window overlay that blocks interaction with underlying content.
**When to use:** When user action is required before proceeding.
**Details:**
- Existing ConfirmationOverlay in MainWindow uses `Visibility.Collapsed`/`Visible` toggling
- Lock overlay follows the same pattern but with different content (card with action buttons)
- Keyboard shortcuts blocked while overlay visible (existing pattern from Phase 8.2)

### Pattern 3: Database-First State Change
**What:** Write pending_moves row BEFORE showing overlay; delete row after resolution.
**When to use:** Crash recovery requires knowing that a drag was in progress.
**Details:**
- INSERT INTO pending_moves immediately in the COM callback handler
- DELETE FROM pending_moves after successful reparent/merge/cancel
- On startup: SELECT * FROM pending_moves → if rows exist, crash happened during drag

### Pattern 4: Window Registry Update (App._windows)
**What:** The `_windows` dictionary maps desktop GUIDs to MainWindow instances.
**When to use:** Reparent changes a window's desktop association.
**Details:**
- Remove old GUID key, add new GUID key pointing to same MainWindow
- Update the window's stored desktop GUID
- Update window title to new desktop name

### Anti-Patterns to Avoid
- **Polling for window position:** Don't use a timer to detect desktop changes. The COM notification fires reliably.
- **Blocking COM callbacks:** COM callbacks run on the UI thread. Long operations (DB writes, COM calls) must be dispatched asynchronously.
- **Direct COM calls in MainWindow:** All COM operations should go through VirtualDesktopService/VirtualDesktopInterop boundaries.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Window move to desktop | Custom Win32 hooks | IVirtualDesktopManager.MoveWindowToDesktop | Only reliable API for programmatic desktop moves |
| Tab migration between windows | New migration logic | DatabaseService.MigrateTabsAsync pattern | Already proven in orphan recovery |
| Overlay animations | Custom storyboard code | DoubleAnimation on Opacity | Standard WPF animation, matches existing toast pattern |
| Desktop name resolution | Manual desktop enumeration | VirtualDesktopService.GetAllDesktops() | Already returns DesktopInfo with name, GUID, index |

## Common Pitfalls

### Pitfall 1: ViewVirtualDesktopChanged Receives IntPtr view, Not HWND
**What goes wrong:** The `view` parameter is an IApplicationView pointer, not a window handle (HWND). You cannot directly use it with GetWindowDesktopId.
**Why it happens:** The COM notification passes the internal Windows view object, which wraps the window.
**How to avoid:** Use the App-level `_windows` dictionary to identify which MainWindow was affected. After the callback fires, check each window's HWND against `VirtualDesktopInterop.GetWindowDesktopId(hwnd)` to see if it's now on a different desktop than expected.
**Warning signs:** COMException or access violation when treating `view` as HWND.

### Pitfall 2: COM Callback Runs on STA Thread
**What goes wrong:** COM callbacks run on the WPF dispatcher/STA thread. DB operations and further COM calls could cause reentrancy or deadlocks.
**Why it happens:** COM apartment threading model.
**How to avoid:** Use `Dispatcher.BeginInvoke` for UI updates. Use `Task.Run` for database writes (fire-and-forget with error logging, matching existing pattern from OnDesktopRenamed).
**Warning signs:** UI freeze after dragging window.

### Pitfall 3: Window Already Closed During Merge
**What goes wrong:** After merging tabs, the dragged window needs to close. But if the user already closed it or another event fires, calling Close() throws.
**Why it happens:** Race conditions between COM callbacks and user actions.
**How to avoid:** Check window state before closing. Use try/catch around Close(). Remove from `_windows` dict before closing to prevent double-processing.
**Warning signs:** ObjectDisposedException or InvalidOperationException on merge.

### Pitfall 4: MoveWindowToDesktop Fails Silently
**What goes wrong:** The COM call returns S_OK but the window doesn't actually move (especially if the target desktop was deleted).
**Why it happens:** Windows COM API design — some failures are soft.
**How to avoid:** After calling MoveWindowToDesktop, verify with GetWindowDesktopId that the window actually moved. If not, escalate to cancel-failure path (DRAG-07).
**Warning signs:** "Go back" appears to succeed but window stays on wrong desktop.

### Pitfall 5: Crash Recovery Timing in Startup Sequence
**What goes wrong:** Resolving pending_moves before session matching means the session GUID mapping is incomplete.
**Why it happens:** The startup sequence stub is at line 124, after session matching (correct order).
**How to avoid:** Keep the existing order: session match first, then pending_moves resolution. The pending_moves row has from_desktop and to_desktop GUIDs, so session matching isn't needed for recovery.
**Warning signs:** Windows appearing on wrong desktops after crash recovery.

### Pitfall 6: Focus-Triggered Overlay Recursion (DRAG-10)
**What goes wrong:** Showing the lock overlay when a misplaced window gains focus could fire the focus event again, creating a loop.
**Why it happens:** Overlay show changes focus state.
**How to avoid:** Use a guard flag `_isShowingDragOverlay` to prevent re-entry. Only trigger the auto-show from Window.Activated event, not GotFocus.
**Warning signs:** Stack overflow or flickering overlay.

## Code Examples

### COM Callback Detection
```csharp
// In VirtualDesktopNotificationListener
public event Action<IntPtr>? WindowViewChanged;

public int ViewVirtualDesktopChanged(IntPtr view)
{
    try
    {
        LogService.Info($"Notification: window view changed (view=0x{view:X})");
        WindowViewChanged?.Invoke(view);
    }
    catch (Exception ex)
    {
        LogService.Warn($"Error in ViewVirtualDesktopChanged callback: {ex.Message}");
    }
    return 0; // S_OK
}
```

### MoveWindowToDesktop Wrapper
```csharp
// In VirtualDesktopInterop
public static void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
{
    EnsureInitialized();
    int hr = _manager!.MoveWindowToDesktop(hwnd, ref desktopId);
    if (hr != 0)
        throw new COMException($"MoveWindowToDesktop failed (HRESULT: 0x{hr:X8})", hr);
}
```

### Pending Moves CRUD
```csharp
// In DatabaseService
public static async Task<int> InsertPendingMoveAsync(string windowId, string fromDesktop, string toDesktop)
{
    return await ExecuteNonQueryAsync(
        "INSERT INTO pending_moves (window_id, from_desktop, to_desktop) VALUES (@wid, @from, @to)",
        ("@wid", windowId), ("@from", fromDesktop), ("@to", toDesktop));
}

public static async Task DeletePendingMoveAsync(string windowId)
{
    await ExecuteNonQueryAsync(
        "DELETE FROM pending_moves WHERE window_id = @wid",
        ("@wid", windowId));
}

public static async Task<List<PendingMove>> GetPendingMovesAsync()
{
    var moves = new List<PendingMove>();
    await ExecuteReaderAsync(
        "SELECT id, window_id, from_desktop, to_desktop, detected_at FROM pending_moves",
        reader =>
        {
            moves.Add(new PendingMove(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        });
    return moves;
}
```

### Lock Overlay Fade Animation
```csharp
// 150ms fade-in matching existing animation timing
var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
DragOverlay.BeginAnimation(OpacityProperty, fadeIn);
DragOverlay.Visibility = Visibility.Visible;
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Polling GetWindowDesktopId | ViewVirtualDesktopChanged COM callback | Win10 era → Win11 | Eliminates polling overhead, instant detection |
| Win32 hooks for window movement | IVirtualDesktopManager.MoveWindowToDesktop | Win10 1803+ | Only supported API for cross-desktop moves |

## Open Questions

1. **ViewVirtualDesktopChanged parameter resolution**
   - What we know: The `IntPtr view` is an IApplicationView COM pointer, not a window HWND
   - What's unclear: Whether we can reliably map it back to a specific window
   - Recommendation: Instead of parsing the view, iterate App._windows and check each window's desktop GUID via GetWindowDesktopId after the callback fires. This is robust and avoids undocumented API surface.

2. **Race condition between COM callback and window registry**
   - What we know: COM callbacks are on the UI thread, same as WPF
   - What's unclear: Whether ViewVirtualDesktopChanged fires before or after the window's internal desktop association updates
   - Recommendation: Use Dispatcher.BeginInvoke with Normal priority to let the COM state settle before querying GetWindowDesktopId

## Sources

### Primary (HIGH confidence)
- Existing codebase: JoJot/Interop/ComInterfaces.cs — IVirtualDesktopNotification vtable with ViewVirtualDesktopChanged
- Existing codebase: JoJot/Interop/VirtualDesktopNotificationListener.cs — callback implementation with no-op ViewVirtualDesktopChanged
- Existing codebase: JoJot/Interop/VirtualDesktopInterop.cs — COM initialization, GetWindowDesktopId wrapper
- Existing codebase: JoJot/Services/VirtualDesktopService.cs — event chain pattern for DesktopRenamed, CurrentDesktopChanged
- Existing codebase: JoJot/Services/DatabaseService.cs — pending_moves table schema (lines 84-91)
- Existing codebase: JoJot/App.xaml.cs — _windows dictionary, Phase 10 stub at line 124

### Secondary (MEDIUM confidence)
- Microsoft IVirtualDesktopManager documentation — MoveWindowToDesktop is the documented API
- Existing project decisions in STATE.md — COM callback patterns, fire-and-forget DB writes

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all infrastructure exists in the codebase
- Architecture: HIGH - follows established COM callback → service event → UI handler pattern
- Pitfalls: HIGH - based on analysis of existing code and COM threading model

**Research date:** 2026-03-03
**Valid until:** Indefinite — infrastructure is stable, COM interfaces are pinned to specific builds
