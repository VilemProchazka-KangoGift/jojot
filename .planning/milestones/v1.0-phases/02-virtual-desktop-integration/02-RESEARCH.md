# Phase 2: Virtual Desktop Integration - Research

**Researched:** 2026-03-02
**Domain:** Windows 11 Virtual Desktop COM Interop (undocumented APIs)
**Confidence:** MEDIUM

## Summary

Windows Virtual Desktop integration requires using a mix of one officially documented COM interface (`IVirtualDesktopManager`) and several undocumented internal interfaces (`IVirtualDesktopManagerInternal`, `IVirtualDesktop`, `IVirtualDesktopNotification`, `IVirtualDesktopNotificationService`). The undocumented interfaces change their COM GUIDs between major Windows 11 releases (22H2/23H2/24H2), requiring a build-number dispatch mechanism.

The core challenge is that Microsoft only officially documents `IVirtualDesktopManager` (3 methods: `IsWindowOnCurrentVirtualDesktop`, `GetWindowDesktopId`, `MoveWindowToDesktop`). Getting desktop names, enumerating desktops, and subscribing to rename/switch notifications all require undocumented COM interfaces accessed through the `ImmersiveShell` service provider. These interfaces have different GUIDs on 23H2 (build 22631) vs 24H2 (build 26100+).

**Primary recommendation:** Build a self-contained `VirtualDesktopService` that uses raw COM interop with a build-number dispatch dictionary. Do NOT use third-party NuGet packages (Grabacr07/VirtualDesktop targets .NET 6-8 only, not .NET 10; Slions.VirtualDesktop.WPF also targets .NET 6-8; neither package supports .NET 10).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Fallback experience**: Silent degradation — no visual indication when COM API unavailable. App launches normally in single-notepad mode; all notes go to 'default' session. Mid-session COM failure: freeze on last known desktop identity, re-detect on next restart. In fallback mode, still create a 'default' row in app_state.
- **Window title behavior**: No truncation of long desktop names. Em-dash separator with spaces: `JoJot — {desktop name}`. Title formats by priority: `JoJot — {desktop name}` > `JoJot — Desktop N` > `JoJot`. Live title updates via IVirtualDesktopNotification subscription — instant, not polled. On rename: update both window title AND app_state.desktop_name in SQLite immediately.
- **Session matching edge cases**: Ambiguous name match (multiple desktops share same name): skip name tier, fall through to index. Index matching only when exactly one unmatched session and one unmatched desktop at that index. Failed sessions marked orphaned (preserved for Phase 8). On successful match: update all three fields (desktop_guid, desktop_name, desktop_index). New desktops get fresh sessions; deleted desktop sessions become orphaned.
- **OS compatibility scope**: Windows 11 only; Windows 10 gets silent fallback. COM GUID mappings hardcoded in source code (static dictionary). Unsupported OS builds: graceful fallback + log warning. Requires code update + release for new Windows builds.

### Claude's Discretion
- Logging granularity for COM failures (once per session vs. every error)
- Which specific Windows 11 builds to support (23H2 and 24H2 minimum)
- COM notification subscription lifecycle management (when to subscribe/unsubscribe)
- Internal error handling strategy within VirtualDesktopService

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| VDSK-01 | Detect current virtual desktop via IVirtualDesktopManager COM API | Official documented API; CLSID_VirtualDesktopManager = AA509086-5CA9-4C25-8F95-589D3C07B48A |
| VDSK-02 | One independent JoJot window per virtual desktop with its own tabs and state | Requires IVirtualDesktopManagerInternal to enumerate all desktops; app_state table already has desktop_guid FK |
| VDSK-03 | Three-tier session matching on startup: GUID, desktop name, desktop index | IVirtualDesktop.GetId() for GUID, GetName() for name; desktop enumeration via IVirtualDesktopManagerInternal.GetDesktops() |
| VDSK-04 | Update stored GUID to current live GUID after successful match | Standard SQLite UPDATE on app_state; no COM-specific concern |
| VDSK-05 | Index matching only when exactly one unmatched session/desktop at that index | Pure business logic; no COM concern |
| VDSK-06 | Window title shows "JoJot — {desktop name}" or "JoJot — Desktop N" or "JoJot" | Desktop name from IVirtualDesktop.GetName(); index from enumeration order |
| VDSK-07 | Window title updates live via IVirtualDesktopNotification when desktop is renamed | Requires IVirtualDesktopNotification subscription via IVirtualDesktopNotificationService.Register() |
| VDSK-08 | Fallback to "default" GUID if virtual desktop API fails | Pure error handling; catch COMException at service boundary |
| VDSK-09 | Virtual desktop service abstraction layer isolating all COM interop from business logic | Service boundary pattern; static VirtualDesktopService class matching existing patterns |
</phase_requirements>

## Standard Stack

### Core
| Library/API | Version | Purpose | Why Standard |
|-------------|---------|---------|--------------|
| IVirtualDesktopManager | Windows 10+ (documented) | Check if window is on current desktop, get desktop GUID for window, move window | Only officially documented virtual desktop COM interface |
| IVirtualDesktopManagerInternal | Undocumented (build-specific GUIDs) | Enumerate desktops, get current desktop, create/remove desktops, set name | Required for desktop enumeration and management; no official alternative |
| IVirtualDesktop | Undocumented (build-specific GUIDs) | Get desktop GUID (GetId), name (GetName) | Only way to get desktop name and ID |
| IVirtualDesktopNotification | Undocumented (build-specific GUIDs) | Receive callbacks for desktop switch, rename, create, destroy | Only way to receive live desktop change notifications |
| IVirtualDesktopNotificationService | Undocumented (build-specific GUIDs) | Register/unregister notification callbacks | Registration point for notification callbacks |
| System.Runtime.InteropServices | .NET 10 built-in | COM interop marshaling, ComImport, Guid attributes | Standard .NET COM interop mechanism |

### Supporting
| Library/API | Version | Purpose | When to Use |
|-------------|---------|---------|-------------|
| Environment.OSVersion | .NET built-in | Detect Windows build number for GUID dispatch | Startup, before COM initialization |
| Registry (HKCU\...\VirtualDesktops) | Windows built-in | Fallback desktop enumeration if COM fails | Backup path; less reliable than COM |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw COM interop | Grabacr07/VirtualDesktop NuGet | Only targets .NET 6-8, not .NET 10; adds dependency that breaks on Windows updates |
| Raw COM interop | Slions.VirtualDesktop.WPF NuGet | Targets .NET 6-8, not .NET 10; uses runtime compilation; adds complexity |
| Build-number dictionary | Runtime compilation (like Slions) | Overcomplicated for 2-3 supported builds; dictionary is simpler and more maintainable |

## Architecture Patterns

### Recommended Project Structure
```
JoJot/
├── Services/
│   └── VirtualDesktopService.cs      # Public API — all business logic calls this
├── Interop/
│   ├── ComInterfaces.cs              # All COM interface definitions with GUIDs
│   ├── ComGuids.cs                   # Build-specific GUID dictionary
│   └── VirtualDesktopInterop.cs      # Raw COM activation and lifecycle
└── Models/
    └── DesktopInfo.cs                # POCO: Guid Id, string Name, int Index
```

### Pattern 1: Build-Number GUID Dispatch
**What:** A static dictionary maps Windows build number ranges to the correct COM interface GUIDs. At startup, `Environment.OSVersion.Version.Build` selects the right set.
**When to use:** Every COM initialization call.
**Example:**
```csharp
// ComGuids.cs
internal static class ComGuids
{
    // Documented (stable across all builds)
    public static readonly Guid CLSID_VirtualDesktopManager =
        new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    // Undocumented — varies by build
    public static readonly Guid CLSID_ImmersiveShell =
        new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    public static readonly Guid CLSID_VirtualDesktopManagerInternal =
        new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    // Build-specific IIDs
    private static readonly Dictionary<int, GuidSet> _buildMap = new()
    {
        // Windows 11 23H2 (build 22631)
        [22631] = new GuidSet(
            IVirtualDesktop: new("3F07F4BE-B107-441A-AF0F-39D82529072C"),
            IVirtualDesktopManagerInternal: new("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
            IVirtualDesktopNotification: new("CD403E52-DEED-4C13-B437-B98380F2B1E8"),
            IVirtualDesktopNotificationService: new("0CD45DE4-2F0F-4211-ACE2-1B3C7C750E13")
        ),
        // Windows 11 24H2 (build 26100)
        [26100] = new GuidSet(
            IVirtualDesktop: new("3F07F4BE-B107-441A-AF0F-39D82529072C"),
            IVirtualDesktopManagerInternal: new("53F5CA0B-158F-4124-900C-057158060B27"),
            IVirtualDesktopNotification: new("C179334C-4295-40D3-BEA1-C654D965605A"),
            IVirtualDesktopNotificationService: new("88846798-1611-4D18-946A-5B8B2B5B0B80")
        ),
    };

    public static GuidSet? Resolve(int buildNumber)
    {
        // Find exact match or closest lower build
        // Return null if unsupported → triggers fallback mode
    }
}
```

### Pattern 2: Service Boundary with Fallback
**What:** `VirtualDesktopService` exposes a clean async API. Internally, it wraps all COM calls in try/catch. Any COM failure triggers fallback mode.
**When to use:** This is THE pattern for Phase 2.
**Example:**
```csharp
public static class VirtualDesktopService
{
    private static bool _isAvailable;
    private static string _currentDesktopGuid = "default";
    private static string _currentDesktopName = "";

    public static async Task InitializeAsync()
    {
        try
        {
            // 1. Check OS build → resolve GUIDs
            // 2. CoCreateInstance for ImmersiveShell
            // 3. QueryService for IVirtualDesktopManagerInternal
            // 4. Register notification callback
            _isAvailable = true;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Virtual desktop API unavailable: {ex.Message}");
            _isAvailable = false;
            _currentDesktopGuid = "default";
        }
    }

    public static bool IsAvailable => _isAvailable;
    public static string CurrentDesktopGuid => _currentDesktopGuid;
    // ... etc
}
```

### Pattern 3: COM Notification via Implementor Class
**What:** A C# class implements `IVirtualDesktopNotification` as a COM callback. The service registers it with `IVirtualDesktopNotificationService.Register()` and receives a cookie (DWORD) for later unregistration.
**When to use:** For live desktop rename and switch notifications (VDSK-07).
**Critical pitfall from research:** ComIn<T> parameters must NOT have Release() called — the caller maintains lifetime. Reference count management is critical.
**Critical pitfall:** When explorer.exe crashes and restarts, notification cookies are reused. Must unregister old before registering new.

### Anti-Patterns to Avoid
- **Exposing COM types outside VirtualDesktopService:** No `IVirtualDesktop`, `IVirtualDesktopManagerInternal`, or COM GUIDs should appear in App.xaml.cs, MainWindow, or any business logic
- **Polling for desktop changes:** Use notification callbacks, never poll
- **Using NuGet packages for COM interop:** They target .NET 6-8 and break on Windows updates; raw interop with a GUID dictionary is more maintainable
- **Single set of GUIDs:** Hardcoding one build's GUIDs without dispatch will break on other builds

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Desktop GUID detection | Custom window enumeration | IVirtualDesktopManager.GetWindowDesktopId | Official documented API; handles edge cases |
| Build number detection | Custom WMI/registry queries | Environment.OSVersion.Version.Build | Built-in, reliable, fast |
| COM activation | Custom COM factories | Marshal.GetTypedObjectForIUnknown + CoCreateInstance via P/Invoke | Standard .NET COM interop path |
| Session matching algorithm | — | Must hand-roll | Unique business logic; no existing solution |

## Common Pitfalls

### Pitfall 1: COM GUIDs Change Between Windows Builds
**What goes wrong:** App works on developer's machine (23H2) but crashes on user's machine (24H2) with E_NOINTERFACE
**Why it happens:** Microsoft changes undocumented COM interface GUIDs between major releases
**How to avoid:** Build-number dispatch dictionary; test on both 23H2 and 24H2; unknown builds trigger fallback
**Warning signs:** COMException with HRESULT 0x80004002 (E_NOINTERFACE)

### Pitfall 2: COM Must Run on STA Thread
**What goes wrong:** COM calls from background threads cause random crashes or deadlocks
**Why it happens:** Shell COM objects require STA (Single-Threaded Apartment) threading
**How to avoid:** Initialize COM on the WPF UI thread (which is STA by default); if background work is needed, use Dispatcher.InvokeAsync to marshal back to UI thread
**Warning signs:** Intermittent crashes, "COM object that has been separated from its underlying RCW" errors

### Pitfall 3: Notification Reference Count Management
**What goes wrong:** After ~2000-3000 desktop switches, app crashes with access violation
**Why it happens:** COM notification callback parameters have pre-incremented reference counts; if the implementor calls Release() (e.g., via IDisposable or GC), the count goes negative
**How to avoid:** Notification callback parameters use ComIn semantics — do NOT call Release() on them. Extract needed data (GUID, name) immediately and let the caller manage lifetime
**Warning signs:** Crashes after extended use; works fine in quick testing

### Pitfall 4: Explorer.exe Crash Reuses Notification Cookies
**What goes wrong:** After explorer.exe crash+restart, unregistering the old notification accidentally unregisters the new one
**Why it happens:** The notification service reuses cookie values when explorer restarts
**How to avoid:** Track registration state; on explorer restart, assume old registration is dead and register fresh without unregistering first
**Warning signs:** Notifications stop working after explorer.exe restarts

### Pitfall 5: IVirtualDesktop.GetName() Returns Empty String
**What goes wrong:** Desktop name shows as empty instead of the Windows-default "Desktop 1", "Desktop 2"
**Why it happens:** GetName() returns the user-set name; if the user hasn't renamed the desktop, it returns empty string (not the display name Windows shows)
**How to avoid:** When GetName() returns empty, use the index-based fallback: "Desktop N" (where N = index + 1)
**Warning signs:** All untouched desktops show "JoJot" instead of "JoJot — Desktop 1"

### Pitfall 6: Fallback Mode Must Be Truly Silent
**What goes wrong:** User on Windows 10 or unsupported build sees error dialogs or missing features
**Why it happens:** COM initialization failure isn't properly caught at the service boundary
**How to avoid:** Wrap ALL COM operations in VirtualDesktopService in try/catch(Exception); set _isAvailable = false on any failure; all public methods check _isAvailable first and return safe defaults
**Warning signs:** Any unhandled exception from COM reaching App.xaml.cs

## Code Examples

### COM Interface Definition (C# with .NET interop)
```csharp
using System.Runtime.InteropServices;

// Documented — stable GUID
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
internal interface IVirtualDesktopManager
{
    bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
    Guid GetWindowDesktopId(IntPtr topLevelWindow);
    void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}

// Undocumented — GUID varies by build; shown here for 24H2
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
internal interface IVirtualDesktop
{
    bool IsViewVisible(IntPtr view);
    Guid GetId();

    [return: MarshalAs(UnmanagedType.HString)]
    string GetName();

    [return: MarshalAs(UnmanagedType.HString)]
    string GetWallpaperPath();

    bool IsRemote();
}
```

### COM Activation Pattern
```csharp
// Get IServiceProvider from ImmersiveShell
var shellType = Type.GetTypeFromCLSID(ComGuids.CLSID_ImmersiveShell);
var shell = Activator.CreateInstance(shellType!);
var provider = (IServiceProvider10)shell!;

// Query for VirtualDesktopManagerInternal
object managerObj;
provider.QueryService(
    ComGuids.CLSID_VirtualDesktopManagerInternal,
    guidSet.IVirtualDesktopManagerInternal,
    out managerObj);
var manager = (IVirtualDesktopManagerInternal)managerObj;
```

### Session Matching Algorithm (Three-Tier)
```csharp
// Tier 1: GUID match (exact)
foreach (var session in sessions)
{
    if (liveDesktops.Any(d => d.Id == session.DesktopGuid))
    {
        matched.Add(session); // Session still on same desktop
    }
}

// Tier 2: Name match (skip if ambiguous)
foreach (var unmatched in sessions.Except(matched))
{
    var nameMatches = liveDesktops.Where(d =>
        d.Name == unmatched.DesktopName && !alreadyMatched.Contains(d.Id));
    if (nameMatches.Count() == 1)
    {
        // Unique name match — reassign
        unmatched.DesktopGuid = nameMatches.Single().Id;
        matched.Add(unmatched);
    }
    // If 0 or 2+ matches → skip to index tier
}

// Tier 3: Index match (strict: exactly one unmatched session + one unmatched desktop at index)
// ... per VDSK-05 constraint
```

### Notification Registration
```csharp
// Implementation class for COM callback
[ComVisible(true)]
internal class VirtualDesktopNotificationListener : IVirtualDesktopNotification
{
    public event Action<Guid, string>? DesktopRenamed;
    public event Action<Guid, Guid>? DesktopSwitched;

    // COM callback methods — extract data, fire C# events, do NOT hold COM references
    public int VirtualDesktopRenamed(IVirtualDesktop desktop, string newName)
    {
        var id = desktop.GetId();
        DesktopRenamed?.Invoke(id, newName);
        return 0; // S_OK
    }
    // ... other methods return S_OK
}

// Registration
uint cookie;
notificationService.Register(listener, out cookie);
// Store cookie for later Unregister(cookie) on shutdown
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single GUID set for all Windows versions | Build-number dispatch dictionary | Windows 11 23H2 Release 3085 (late 2023) | Must maintain per-build GUID mappings |
| IVirtualDesktop without GetName() | GetName() returns HString | Windows 11 22H2+ | Can now get desktop name directly from COM |
| No rename notification | VirtualDesktopRenamed in IVirtualDesktopNotification | Windows 11 22H2+ | Live rename detection possible |

**Deprecated/outdated:**
- Windows 10 virtual desktop GUIDs: completely different interface layout; not relevant for JoJot (Win10 = fallback mode)
- Grabacr07/VirtualDesktop NuGet 5.x: targets .NET 6-8, uses C#/WinRT which may not be available on .NET 10

## Open Questions

1. **Exact 23H2 IVirtualDesktopNotification GUID**
   - What we know: Multiple sources suggest CD403E52-DEED-4C13-B437-B98380F2B1E8 for earlier Win11 builds
   - What's unclear: Whether 23H2 specifically changed this GUID in the "Release 3085" update
   - Recommendation: Test on actual 23H2 machine; if wrong GUID → COMException → fallback mode (safe). Update dictionary when confirmed.

2. **IVirtualDesktopNotificationService GUID per build**
   - What we know: The service is obtained via IServiceProvider.QueryService, not direct CoCreateInstance
   - What's unclear: Exact GUID per build for the notification service
   - Recommendation: Use the same QueryService pattern as VirtualDesktopManagerInternal; service GUID is likely stable. If not → fallback mode.

3. **COM notification method signatures per build**
   - What we know: 23H2 and 24H2 may have different parameter counts/types on notification methods
   - What's unclear: Whether the vtable layout changed
   - Recommendation: Define separate interface versions if needed; dispatch via build number. Start with 24H2 layout (most current), verify 23H2 at runtime.

4. **Windows 11 22H2 support**
   - What we know: 22H2 (build 22621) is still in extended support
   - Recommendation: Include 22H2 GUIDs if available; minimum is 23H2 + 24H2 per success criteria. 22H2 can use same GUIDs as 23H2 pre-3085 update.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) — official documented interface, CLSID, 3 methods
- [MScholtes/VirtualDesktop VirtualDesktop11-24H2.cs](https://github.com/MScholtes/VirtualDesktop/blob/master/VirtualDesktop11-24H2.cs) — complete COM interface definitions for 24H2
- [MScholtes/VirtualDesktop VirtualDesktop11.cs](https://github.com/MScholtes/VirtualDesktop/blob/master/VirtualDesktop11.cs) — COM interface definitions for 23H2

### Secondary (MEDIUM confidence)
- [Grabacr07/VirtualDesktop](https://github.com/Grabacr07/VirtualDesktop) — C# wrapper library for Windows 11; verified architecture patterns
- [Ciantic/VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor) — notification interface details, COM reference management pitfalls
- [Ciantic VirtualDesktopAccessor note-IVirtualDesktopNotification.md](https://github.com/Ciantic/VirtualDesktopAccessor/blob/rust/note-IVirtualDesktopNotification.md) — critical COM reference counting pitfalls

### Tertiary (LOW confidence)
- [Meziantou blog: Listing Windows virtual desktops using .NET](https://www.meziantou.net/listing-windows-virtual-desktops-using-dotnet.htm) — registry-based fallback approach
- Various web search results on IVirtualDesktopNotification GUIDs — unverified exact values

## Metadata

**Confidence breakdown:**
- Standard stack: MEDIUM — documented API is solid; undocumented APIs verified against multiple open-source implementations
- Architecture: HIGH — service boundary pattern well-established; build dispatch pattern proven by MScholtes and Grabacr07 projects
- Pitfalls: HIGH — multiple independent sources confirm same pitfalls (reference counting, STA threading, build-specific GUIDs)

**Research date:** 2026-03-02
**Valid until:** 2026-04-02 (30 days — APIs change with major Windows updates only)

---

*Phase: 02-virtual-desktop-integration*
*Researched: 2026-03-02*
