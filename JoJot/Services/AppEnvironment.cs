using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace JoJot.Services;

/// <summary>
/// Provides environment-aware paths and identifiers that differ between
/// Debug and Release builds, preventing debug sessions from sharing
/// the production database, logs, IPC pipe, and single-instance mutex.
/// </summary>
public static class AppEnvironment
{
#if DEBUG
    public const bool IsDebug = true;
    private const string FolderName = "JoJot.Dev";
    private const string Suffix = ".Dev";
#else
    public const bool IsDebug = false;
    private const string FolderName = "JoJot";
    private const string Suffix = "";
#endif

    /// <summary>App data directory: %LocalAppData%\JoJot (release) or %LocalAppData%\JoJot.Dev (debug).</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>Full path to the SQLite database file.</summary>
    public static string DatabasePath { get; } = Path.Combine(AppDataDirectory, "jojot.db");

    /// <summary>Windows session id — used to scope single-instance to the interactive session.</summary>
    private static readonly int SessionId = GetSessionId();

    private static int GetSessionId()
    {
        using var p = Process.GetCurrentProcess();
        return p.SessionId;
    }

    /// <summary>
    /// Named pipe name for IPC communication. Scoped to the Windows session id: pipe names are
    /// machine-global (unlike mutexes they have no per-session namespace), so on an RDP / multi-user
    /// host a second user's pipe would otherwise collide with the first user's. Session marker is
    /// placed before the Dev/Release suffix.
    /// </summary>
    public static string PipeName { get; } = $"JoJot_IPC_S{SessionId}{Suffix}";

    /// <summary>
    /// Single-instance mutex name. Uses the <c>Local\</c> (per-session) namespace rather than
    /// <c>Global\</c> so single-instance enforcement is per interactive session — a second user on
    /// a shared/RDP host can run their own instance instead of being blocked (and unable to reach
    /// the other session's process over IPC).
    /// </summary>
    public static string MutexName { get; } = $"Local\\JoJot_SingleInstance{Suffix}";

    /// <summary>CalVer version string (YYYY.M.Count) sourced from AssemblyVersion.</summary>
    public static string Version { get; } = BuildVersion();

    /// <summary>Version with a " (Dev)" marker appended in debug builds.</summary>
    public static string VersionDisplay { get; } = IsDebug ? $"{Version} (Dev)" : Version;

    private static string BuildVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version!;
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
