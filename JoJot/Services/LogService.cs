using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace JoJot.Services;

/// <summary>
/// Dual-output logging service backed by Serilog.
/// Writes to a rolling log file and <see cref="System.Diagnostics.Debug"/> output.
/// Thread-safe. Log files rotate daily and when exceeding 5 MB, retaining 5 files.
/// Supports structured message templates, configurable minimum level, and per-service context.
/// </summary>
public static class LogService
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    /// <summary>
    /// Initializes the Serilog pipeline with file and debug sinks, enrichers, and level control.
    /// Must be called before any other logging methods.
    /// </summary>
    /// <param name="directory">Directory where <c>jojot.log</c> will be written.</param>
    public static void Initialize(string directory)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .WriteTo.File(
                path: Path.Combine(directory, "jojot.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                retainedFileCountLimit: 5,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u5}] [{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug(
                outputTemplate: "[{Level:u5}] [{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    // ─── Simple Messages ─────────────────────────────────────────────────────

    /// <summary>Logs a debug-level message (only visible when minimum level is Debug).</summary>
    public static void Debug(string message) => Log.Debug(message);

    /// <summary>Logs an informational message.</summary>
    public static void Info(string message) => Log.Information(message);

    /// <summary>Logs a warning message with optional exception details.</summary>
    public static void Warn(string message, Exception? ex = null) => Log.Warning(ex, message);

    /// <summary>Logs an error message with optional exception details.</summary>
    public static void Error(string message, Exception? ex = null) => Log.Error(ex, message);

    // ─── Structured Templates ────────────────────────────────────────────────

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

    // ─── Level Control ───────────────────────────────────────────────────────

    /// <summary>
    /// Changes the minimum log level at runtime. Messages below this level are discarded.
    /// Persisted to the preferences table via <see cref="DatabaseService"/>.
    /// </summary>
    public static void SetMinimumLevel(LogEventLevel level) => LevelSwitch.MinimumLevel = level;

    /// <summary>Returns the current minimum log level.</summary>
    public static LogEventLevel GetMinimumLevel() => LevelSwitch.MinimumLevel;

    // ─── Per-Service Context ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a contextual logger tagged with the given type's name as SourceContext.
    /// Usage: <c>private static readonly ILogger Log = LogService.ForContext&lt;DatabaseService&gt;();</c>
    /// </summary>
    public static ILogger ForContext<T>() => Log.ForContext<T>();

    /// <summary>
    /// Creates a contextual logger tagged with a custom property.
    /// </summary>
    public static ILogger ForContext(string propertyName, object? value) => Log.ForContext(propertyName, value);

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>Flushes all buffered log events and closes sinks. Call on application exit.</summary>
    public static void Shutdown() => Log.CloseAndFlush();
}
