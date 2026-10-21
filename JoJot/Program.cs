using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using JoJot.Interop;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

/// <summary>
/// Custom entry point. Second instances are handled here — mutex check, desktop query,
/// and IPC send — BEFORE any WPF type is touched, so they never pay App.xaml resource
/// parsing or PresentationFramework load. Only the primary instance enters RunPrimary,
/// which is deliberately non-inlined: referencing App inside Main's body would force the
/// JIT to load WPF assemblies even on the second-instance path.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        Directory.CreateDirectory(AppEnvironment.AppDataDirectory);
        LogService.Initialize(AppEnvironment.AppDataDirectory);
        DateTime processStart;
        using (var process = Process.GetCurrentProcess())
        {
            processStart = process.StartTime;
        }
        var preMainMs = (long)(DateTime.Now - processStart).TotalMilliseconds;
        LogService.Info("JoJot starting... ({PreMainMs}ms since process start)", preMainMs);

        bool createdNew = IpcService.TryAcquireMutex(out var mutex);
        if (!createdNew)
        {
            var result = SendNewTabToPrimary();
            // The everyday "new note" latency (taskbar middle-click while JoJot runs), end to end.
            LogService.Info("Second instance: new-tab {SendResult} {TotalMs}ms after process start",
                result, (long)(DateTime.Now - processStart).TotalMilliseconds);
            if (result != IpcService.SendResult.ZombieKilled)
            {
                mutex.Dispose();
                LogService.Shutdown();
                return; // exited without ever loading WPF
            }
            // Zombie was killed — fall through and take over as primary.
            LogService.Warn("Previous instance was unresponsive and terminated — continuing as primary");
        }

        RunPrimary(mutex, tookOverFromZombie: !createdNew);
    }

    /// <summary>
    /// Queries the current virtual desktop's GUID via COM and sends a new-tab command to the
    /// running primary instance over the named pipe. Fully synchronous: no dispatcher exists
    /// yet, and the send is a plain blocking pipe connect + write.
    /// </summary>
    private static IpcService.SendResult SendNewTabToPrimary()
    {
        string? senderDesktopGuid = null;
        try
        {
            VirtualDesktopInterop.Initialize();
            // GUID only (GetCurrentDesktop + GetId) — GetCurrentDesktop() would enumerate every
            // desktop (COM + registry per desktop) for an index the command never sends.
            senderDesktopGuid = VirtualDesktopInterop.GetCurrentDesktopId().ToString();
        }
        catch (Exception ex)
        {
            LogService.Warn("Second instance: failed to query desktop — first instance will use cached GUID: {Error}", ex.Message);
        }

        LogService.Info("Second instance (pre-WPF fast path) — sending new-tab command (desktop={DesktopGuid})", senderDesktopGuid);
        return IpcService.SendCommand(new NewTabCommand(DesktopGuid: senderDesktopGuid));
    }

    /// <summary>Boots WPF for the primary instance. NoInlining keeps App (and therefore
    /// PresentationFramework) out of Main's JIT scope.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunPrimary(Mutex mutex, bool tookOverFromZombie)
    {
        if (tookOverFromZombie)
        {
            mutex.Dispose();
            if (!App.TryReacquireMutexAsPrimary())
            {
                LogService.Shutdown();
                return; // another instance won the race
            }
        }
        else
        {
            App.SetSingleInstanceMutex(mutex);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
