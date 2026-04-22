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

    /// <summary>Named pipe name for IPC communication.</summary>
    public const string PipeName = "JoJot_IPC" + Suffix;

    /// <summary>Global mutex name for single-instance enforcement.</summary>
    public const string MutexName = "Global\\JoJot_SingleInstance" + Suffix;

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
