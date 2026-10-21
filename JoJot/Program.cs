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
        using (var process = Process.GetCurrentProcess())
        {
            var preMainMs = (long)(DateTime.Now - process.StartTime).TotalMilliseconds;
            LogService.Info("JoJot starting... ({PreMainMs}ms since process start)", preMainMs);
        }

        bool createdNew = IpcService.TryAcquireMutex(out var mutex);
        if (!createdNew)
        {
            var result = SendNewTabToPrimary();
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
    /// Queries the current virtual desktop via COM and sends a new-tab command to the
    /// running primary instance over the named pipe. Fully synchronous (no dispatcher
    /// exists yet, so blocking on the async send cannot deadlock).
    /// </summary>
    private static IpcService.SendResult SendNewTabToPrimary()
    {
        string? senderDesktopGuid = null;
        try
        {
            VirtualDesktopInterop.Initialize();
            var (id, _, _) = VirtualDesktopInterop.GetCurrentDesktop();
            senderDesktopGuid = id.ToString();
        }
        catch (Exception ex)
        {
            LogService.Warn("Second instance: failed to query desktop — first instance will use cached GUID: {Error}", ex.Message);
        }

        LogService.Info("Second instance (pre-WPF fast path) — sending new-tab command (desktop={DesktopGuid})", senderDesktopGuid);
        return IpcService.SendCommandAsync(new NewTabCommand(DesktopGuid: senderDesktopGuid))
            .GetAwaiter().GetResult();
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
