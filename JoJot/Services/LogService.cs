using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace JoJot.Services;

/// <summary>
/// Logging service backed by Serilog.
/// Writes to a rolling daily log file (<c>jojot{yyyyMMdd}.log</c>) and, in Debug builds only,
/// to <see cref="System.Diagnostics.Debug"/> output. Thread-safe. Retains 5 files; each file is
/// capped at 5 MB. The file is opened in shared mode so the primary instance and a short-lived
/// second instance append to the same file (lines carry the process id).
/// Supports structured message templates, configurable minimum level, and per-service context.
/// </summary>
public static class LogService
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    /// <summary>
    /// Log files from an older naming scheme. The current retention only applies to the
    /// <c>jojot{Date}.log</c> pattern, so these were never deleted (one was seen at 71 MB).
    /// </summary>
    internal static readonly string[] LegacyLogFileNames = ["jojot.log", "jojot.log.old"];

    /// <summary>
    /// Initializes the Serilog pipeline with the file sink (plus the debug sink in Debug builds),
    /// enrichers, and level control, then removes legacy log files.
    /// Must be called before any other logging methods.
    /// </summary>
    /// <param name="directory">Directory where the <c>jojot{Date}.log</c> files are written.</param>
    public static void Initialize(string directory)
    {
        Log.Logger = CreateConfiguration(directory).CreateLogger();

        var deleted = CleanupLegacyLogs(directory);
        if (deleted.Count > 0)
        {
            Info("LogService: deleted legacy log file(s) {LegacyFiles}", string.Join(", ", deleted));
        }
    }

    /// <summary>
    /// Builds the production pipeline (sinks, enrichers, level switch) without installing it, so
    /// tests can create an independent logger with the exact production file-sink options.
    /// </summary>
    internal static LoggerConfiguration CreateConfiguration(string directory)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            // shared: true — a second instance (taskbar "new note") logs while the primary holds the
            // file. Exclusive mode made Serilog spill each second instance into a jojot{Date}_001.log
            // file, eating the 5-file retention. Shared mode is compatible with rollingInterval and
            // fileSizeLimitBytes; it only rules out buffered: true (left at its false default) and
            // file lifecycle hooks (none used).
            .WriteTo.Async(a => a.File(
                path: Path.Combine(directory, "jojot.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                retainedFileCountLimit: 5,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u5}] [{ProcessId}:{ThreadId}] {Message:lj}{NewLine}{Exception}"));
#if DEBUG
        // The Debug sink writes synchronously on the logging thread — Debug builds only.
        config = config.WriteTo.Debug(
            outputTemplate: "[{Level:u5}] [{ThreadId}] {Message:lj}{NewLine}{Exception}");
#endif
        return config;
    }

    /// <summary>
    /// Deletes exactly the <see cref="LegacyLogFileNames"/> from <paramref name="directory"/> if
    /// present. Never throws: a file that cannot be deleted (locked, access denied) is skipped and
    /// retried on the next start. Returns the names that were deleted.
    /// </summary>
    internal static IReadOnlyList<string> CleanupLegacyLogs(string directory)
    {
        var deleted = new List<string>();
        foreach (var name in LegacyLogFileNames)
        {
            try
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn("LogService: could not delete legacy log file {LegacyFile}", name, ex);
            }
        }
        return deleted;
    }

    /// <summary>
    /// Initializes a no-op logger with no sinks. For use in unit tests to prevent
    /// file system access while keeping log calls safe.
    /// </summary>
    internal static void InitializeNoop()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Fatal()
            .CreateLogger();
    }

    // ─── Simple Messages ────────────────────────────────────────────────

    /// <summary>Logs a debug-level message (only visible when minimum level is Debug).</summary>
    public static void Debug(string message) => Log.Debug(message);

    /// <summary>Logs an informational message.</summary>
    public static void Info(string message) => Log.Information(message);

    /// <summary>Logs a warning message with optional exception details.</summary>
    public static void Warn(string message, Exception? ex = null) => Log.Warning(ex, message);

    /// <summary>Logs an error message with optional exception details.</summary>
    public static void Error(string message, Exception? ex = null) => Log.Error(ex, message);

    // ─── Structured Templates ───────────────────────────────────────────

    /// <summary>Logs a debug-level structured message template.</summary>
    public static void Debug<T>(string template, T value) => Log.Debug(template, value);

    /// <summary>Logs a debug-level structured message template.</summary>
    public static void Debug<T0, T1>(string template, T0 v0, T1 v1) => Log.Debug(template, v0, v1);

    /// <summary>Logs a debug-level structured message template.</summary>
    public static void Debug<T0, T1, T2>(string template, T0 v0, T1 v1, T2 v2) => Log.Debug(template, v0, v1, v2);

    /// <summary>Logs an informational structured message template.</summary>
    public static void Info<T>(string template, T value) => Log.Information(template, value);

    /// <summary>Logs an informational structured message template.</summary>
    public static void Info<T0, T1>(string template, T0 v0, T1 v1) => Log.Information(template, v0, v1);

    /// <summary>Logs an informational structured message template.</summary>
    public static void Info<T0, T1, T2>(string template, T0 v0, T1 v1, T2 v2) => Log.Information(template, v0, v1, v2);

    /// <summary>Logs an informational structured message template.</summary>
    public static void Info<T0, T1, T2, T3>(string template, T0 v0, T1 v1, T2 v2, T3 v3) => Log.Information(template, v0, v1, v2, v3);

    /// <summary>Logs an informational structured message template.</summary>
    public static void Info<T0, T1, T2, T3, T4>(string template, T0 v0, T1 v1, T2 v2, T3 v3, T4 v4) => Log.Information(template, v0, v1, v2, v3, v4);

    /// <summary>Logs a warning structured message template with optional exception.</summary>
    public static void Warn<T>(string template, T value, Exception? ex = null) => Log.Warning(ex, template, value);

    /// <summary>Logs a warning structured message template with optional exception.</summary>
    public static void Warn<T0, T1>(string template, T0 v0, T1 v1, Exception? ex = null) => Log.Warning(ex, template, v0, v1);

    /// <summary>Logs an error structured message template with optional exception.</summary>
    public static void Error<T>(string template, T value, Exception? ex = null) => Log.Error(ex, template, value);

    /// <summary>Logs an error structured message template with optional exception.</summary>
    public static void Error<T0, T1>(string template, T0 v0, T1 v1, Exception? ex = null) => Log.Error(ex, template, v0, v1);

    /// <summary>Logs an error structured message template with optional exception.</summary>
    public static void Error<T0, T1, T2>(string template, T0 v0, T1 v1, T2 v2, Exception? ex = null) => Log.Error(ex, template, v0, v1, v2);

    // ─── Level Control ──────────────────────────────────────────────────

    /// <summary>
    /// Changes the minimum log level at runtime. Messages below this level are discarded.
    /// Persisted to the preferences table via <see cref="DatabaseService"/>.
    /// </summary>
    public static void SetMinimumLevel(LogEventLevel level) => LevelSwitch.MinimumLevel = level;

    /// <summary>Returns the current minimum log level.</summary>
    public static LogEventLevel GetMinimumLevel() => LevelSwitch.MinimumLevel;

    // ─── Per-Service Context ────────────────────────────────────────────

    /// <summary>
    /// Creates a contextual logger tagged with the given type's name as SourceContext.
    /// Usage: <c>private static readonly ILogger Log = LogService.ForContext&lt;DatabaseService&gt;();</c>
    /// </summary>
    public static ILogger ForContext<T>() => Log.ForContext<T>();

    /// <summary>
    /// Creates a contextual logger tagged with a custom property.
    /// </summary>
    public static ILogger ForContext(string propertyName, object? value) => Log.ForContext(propertyName, value);

    // ─── Lifecycle ──────────────────────────────────────────────────────

    /// <summary>Flushes all buffered log events and closes sinks. Call on application exit.</summary>
    public static void Shutdown() => Log.CloseAndFlush();
}
