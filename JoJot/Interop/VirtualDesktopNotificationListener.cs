using System.Runtime.InteropServices;
using JoJot.Services;

namespace JoJot.Interop;

/// <summary>
/// COM callback implementor for virtual desktop notifications.
/// Windows calls these methods when desktop events occur (rename, switch, create, destroy).
/// Registered via IVirtualDesktopNotificationService.Register().
///
/// CRITICAL: Do NOT call Marshal.Release() on any parameters passed into these callbacks.
/// Windows manages their lifetime during the callback invocation.
///
/// Method order must match the IVirtualDesktopNotification vtable layout exactly.
/// The interface GUID varies by Windows build; the vtable layout is stable across 22H2/23H2/24H2.
/// </summary>
[ComVisible(true)]
internal sealed class VirtualDesktopNotificationListener : IVirtualDesktopNotification
{
    /// <summary>Fired when a desktop is renamed. Args: (desktopId, newName)</summary>
    public event Action<Guid, string>? DesktopRenamed;

    /// <summary>Fired when the current desktop changes. Args: (oldDesktopId, newDesktopId)</summary>
    public event Action<Guid, Guid>? CurrentDesktopChanged;

    /// <summary>Fired when a new desktop is created. Args: (desktopId)</summary>
    public event Action<Guid>? DesktopCreated;

    /// <summary>Fired when a desktop is destroyed. Args: (destroyedDesktopId)</summary>
    public event Action<Guid>? DesktopDestroyed;

    /// <summary>Fired when a window's view changes desktop. Args: (viewPtr)</summary>
    public event Action<IntPtr>? WindowViewChanged;

    /// <inheritdoc />
    public int VirtualDesktopCreated(IntPtr monitors, IVirtualDesktop desktop)
    {
        try
        {
            Guid id = desktop.GetId();
            LogService.Info($"Notification: desktop created ({id})");
            DesktopCreated?.Invoke(id);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Error in VirtualDesktopCreated callback: {ex.Message}");
        }
        return 0; // S_OK
    }

    /// <inheritdoc />
    public int VirtualDesktopDestroyBegin(IntPtr monitors, IVirtualDesktop desktopDestroyed, IVirtualDesktop desktopFallback)
    {
        return 0; // S_OK — no action needed
    }

    /// <inheritdoc />
    public int VirtualDesktopDestroyFailed(IntPtr monitors, IVirtualDesktop desktopDestroyed, IVirtualDesktop desktopFallback)
    {
        return 0; // S_OK — no action needed
    }

    /// <inheritdoc />
    public int VirtualDesktopDestroyed(IntPtr monitors, IVirtualDesktop desktopDestroyed, IVirtualDesktop desktopFallback)
    {
        try
        {
            Guid id = desktopDestroyed.GetId();
            LogService.Info($"Notification: desktop destroyed ({id})");
            DesktopDestroyed?.Invoke(id);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Error in VirtualDesktopDestroyed callback: {ex.Message}");
        }
        return 0; // S_OK
    }

    /// <inheritdoc />
    public int VirtualDesktopMoved(IntPtr monitors, IVirtualDesktop desktop, int oldIndex, int newIndex)
    {
        LogService.Info($"Notification: desktop moved (index {oldIndex} -> {newIndex})");
        return 0; // S_OK — index changes handled via session matching on next startup
    }

    /// <inheritdoc />
    public int VirtualDesktopRenamed(IVirtualDesktop desktop, string newName)
    {
        try
        {
            Guid id = desktop.GetId();
            string safeName = newName ?? "";
            LogService.Info($"Notification: desktop renamed ({id}) -> \"{safeName}\"");
            DesktopRenamed?.Invoke(id, safeName);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Error in VirtualDesktopRenamed callback: {ex.Message}");
        }
        return 0; // S_OK
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public int CurrentVirtualDesktopChanged(IntPtr monitors, IVirtualDesktop desktopOld, IVirtualDesktop desktopNew)
    {
        try
        {
            Guid oldId = desktopOld.GetId();
            Guid newId = desktopNew.GetId();
            LogService.Info($"Notification: desktop switched ({oldId} -> {newId})");
            CurrentDesktopChanged?.Invoke(oldId, newId);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Error in CurrentVirtualDesktopChanged callback: {ex.Message}");
        }
        return 0; // S_OK
    }

    /// <inheritdoc />
    public int VirtualDesktopWallpaperChanged(IVirtualDesktop desktop, string path)
    {
        return 0; // S_OK — wallpaper changes not relevant to JoJot
    }

    /// <inheritdoc />
    public int VirtualDesktopSwitchOverCompleted(IVirtualDesktop desktop)
    {
        return 0; // S_OK — switch animation complete, no action needed
    }

    /// <inheritdoc />
    public int RemoteVirtualDesktopConnected(IVirtualDesktop desktop)
    {
        return 0; // S_OK — remote desktop not relevant to JoJot
    }
}
