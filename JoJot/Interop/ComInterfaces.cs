using System.Runtime.InteropServices;

namespace JoJot.Interop;

// ─── Documented COM interfaces (stable GUIDs) ───────────────────────────

/// <summary>
/// IServiceProvider from the Windows Shell (ImmersiveShell).
/// Used to query for undocumented virtual desktop services.
/// This is the standard COM IServiceProvider, not System.IServiceProvider.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
internal interface IServiceProvider10
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}

/// <summary>
/// IObjectArray — COM collection interface used to enumerate virtual desktops.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
internal interface IObjectArray
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
}

/// <summary>
/// IVirtualDesktopManager — the only officially documented virtual desktop COM interface.
/// CLSID: AA509086-5CA9-4C25-8F95-589D3C07B48A
/// Provides: IsWindowOnCurrentVirtualDesktop, GetWindowDesktopId, MoveWindowToDesktop.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}

// ─── Undocumented COM interfaces (GUIDs vary by build) ──────────────────
//
// These interfaces are NOT officially documented by Microsoft.
// The GUIDs in the [Guid] attributes are for Windows 11 24H2 (build 26100+).
// For other builds, the interop layer uses Marshal.QueryInterface with the
// correct build-specific GUID from ComGuids.
//
// The vtable layout (method order) has been stable across 22H2/23H2/24H2.
// Only the IID changes between builds.

/// <summary>
/// IVirtualDesktop — represents a single virtual desktop.
/// Methods: IsViewVisible, GetId, GetName, GetWallpaperPath, IsRemote.
/// GUID shown is for 24H2; actual GUID resolved at runtime.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
internal interface IVirtualDesktop
{
    [PreserveSig]
    int IsViewVisible(IntPtr view, out bool visible);

    Guid GetId();

    [return: MarshalAs(UnmanagedType.HString)]
    string GetName();

    [return: MarshalAs(UnmanagedType.HString)]
    string GetWallpaperPath();

    [PreserveSig]
    int IsRemote(out bool isRemote);
}

/// <summary>
/// IVirtualDesktopManagerInternal — undocumented manager for desktop operations.
/// Provides: GetCount, GetCurrentDesktop, GetDesktops, FindDesktop, SetDesktopName, etc.
/// GUID shown is for 24H2; actual GUID resolved at runtime.
/// CRITICAL: Method order must match vtable layout exactly for COM interop to work.
/// The 24H2+ vtable does NOT take hWndOrMon parameters on most methods.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
internal interface IVirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int MoveViewToDesktop(IntPtr view, [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);

    [PreserveSig]
    int CanViewMoveDesktops(IntPtr view, out bool canMove);

    [PreserveSig]
    int GetCurrentDesktop([MarshalAs(UnmanagedType.Interface)] out IVirtualDesktop desktop);

    [PreserveSig]
    int GetDesktops([MarshalAs(UnmanagedType.Interface)] out IObjectArray desktops);

    [PreserveSig]
    int GetAdjacentDesktop(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop,
        int direction,
        [MarshalAs(UnmanagedType.Interface)] out IVirtualDesktop adjacentDesktop);

    [PreserveSig]
    int SwitchDesktop([MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);

    [PreserveSig]
    int SwitchDesktopAndMoveForegroundView([MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);

    [PreserveSig]
    int CreateDesktop([MarshalAs(UnmanagedType.Interface)] out IVirtualDesktop desktop);

    [PreserveSig]
    int MoveDesktop([MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop, int index);

    [PreserveSig]
    int RemoveDesktop(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop fallback);

    [PreserveSig]
    int FindDesktop(ref Guid desktopId, [MarshalAs(UnmanagedType.Interface)] out IVirtualDesktop desktop);
}

/// <summary>
/// IVirtualDesktopNotification — COM callback interface for desktop events.
/// Implement this interface to receive notifications when desktops are
/// renamed, switched, created, or destroyed.
/// CRITICAL: Do NOT call Release() on any IntPtr/object parameters.
/// Windows manages their lifetime during the callback.
/// GUID shown is for 24H2; actual GUID resolved at runtime.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("C179334C-4295-40D3-BEA1-C654D965605A")]
internal interface IVirtualDesktopNotification
{
    [PreserveSig]
    int VirtualDesktopCreated(IntPtr monitors, [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);

    [PreserveSig]
    int VirtualDesktopDestroyBegin(
        IntPtr monitors,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopDestroyed,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopFallback);

    [PreserveSig]
    int VirtualDesktopDestroyFailed(
        IntPtr monitors,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopDestroyed,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopFallback);

    [PreserveSig]
    int VirtualDesktopDestroyed(
        IntPtr monitors,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopDestroyed,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopFallback);

    [PreserveSig]
    int VirtualDesktopMoved(
        IntPtr monitors,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop,
        int oldIndex,
        int newIndex);

    [PreserveSig]
    int VirtualDesktopRenamed(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop,
        [MarshalAs(UnmanagedType.HString)] string newName);

    [PreserveSig]
    int ViewVirtualDesktopChanged(IntPtr view);

    [PreserveSig]
    int CurrentVirtualDesktopChanged(
        IntPtr monitors,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopOld,
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktopNew);

    [PreserveSig]
    int VirtualDesktopWallpaperChanged(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop,
        [MarshalAs(UnmanagedType.HString)] string path);

    [PreserveSig]
    int VirtualDesktopSwitchOverCompleted(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);

    [PreserveSig]
    int RemoteVirtualDesktopConnected(
        [MarshalAs(UnmanagedType.Interface)] IVirtualDesktop desktop);
}

/// <summary>
/// IVirtualDesktopNotificationService — registration point for notification callbacks.
/// Register an IVirtualDesktopNotification implementation to receive desktop events.
/// Register() returns a cookie (uint) for later Unregister().
/// GUID shown is for 24H2; actual GUID resolved at runtime.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("88846798-1611-4D18-946A-5B8B2B5B0B80")]
internal interface IVirtualDesktopNotificationService
{
    [PreserveSig]
    int Register([MarshalAs(UnmanagedType.Interface)] IVirtualDesktopNotification notification, out uint cookie);

    [PreserveSig]
    int Unregister(uint cookie);
}
