using System.IO;
using System.Text.Json;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// Disk snapshot of startup-critical state (all preferences + per-desktop window geometry)
/// so the first window can appear with correct language/theme/placement before the database
/// is open. The database remains the source of truth: every preference/geometry write also
/// updates this file, and after the DB loads at startup the snapshot is reconciled to DB truth.
/// Writes are debounce-free but atomic (temp file + move); losing this file only costs one
/// slower startup.
/// </summary>
public static class StartupSnapshot
{
    private sealed class SnapshotData
    {
        public Dictionary<string, string> Preferences { get; set; } = [];
        public Dictionary<string, WindowGeometry> Geometry { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Lock _lock = new();
    private static SnapshotData _data = new();
    private static Task _lastWrite = Task.CompletedTask;

    internal static string? OverrideFilePathForTests { get; set; }

    private static string FilePath =>
        OverrideFilePathForTests ?? Path.Combine(AppEnvironment.AppDataDirectory, "startup-cache.json");

    /// <summary>Preferences from the loaded snapshot (empty until TryLoad succeeds).</summary>
    public static IReadOnlyDictionary<string, string> Preferences
    {
        get { lock (_lock) return new Dictionary<string, string>(_data.Preferences); }
    }

    /// <summary>
    /// Loads the snapshot from disk. Returns false when missing or unreadable;
    /// a corrupt file is deleted so the next run starts clean.
    /// </summary>
    public static bool TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            var loaded = JsonSerializer.Deserialize<SnapshotData>(File.ReadAllText(FilePath));
            if (loaded is null) return false;
            // System.Text.Json builds a fresh case-sensitive dictionary; restore ordinal-ignore-case
            // so GetGeometry lookups behave the same as a freshly-constructed SnapshotData.
            loaded.Geometry = new Dictionary<string, WindowGeometry>(loaded.Geometry, StringComparer.OrdinalIgnoreCase);
            lock (_lock) _data = loaded;
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn("StartupSnapshot: failed to load — discarding ({Error})", ex.Message);
            try { File.Delete(FilePath); } catch (Exception) { }
            return false;
        }
    }

    /// <summary>Returns the saved geometry for a desktop, or null if none recorded.</summary>
    public static WindowGeometry? GetGeometry(string desktopGuid)
    {
        lock (_lock) return _data.Geometry.TryGetValue(desktopGuid, out var geo) ? geo : null;
    }

    /// <summary>Records a preference change and rewrites the snapshot asynchronously.</summary>
    public static void UpdatePreference(string key, string value)
    {
        lock (_lock) _data.Preferences[key] = value;
        SaveInBackground();
    }

    /// <summary>Records a geometry change and rewrites the snapshot asynchronously.</summary>
    public static void UpdateGeometry(string desktopGuid, WindowGeometry geo)
    {
        lock (_lock) _data.Geometry[desktopGuid] = geo;
        SaveInBackground();
    }

    /// <summary>Replaces all preferences with database truth (startup reconcile).</summary>
    public static void ReplacePreferences(IReadOnlyDictionary<string, string> all)
    {
        lock (_lock) _data.Preferences = new Dictionary<string, string>(all);
        SaveInBackground();
    }

    private static void SaveInBackground()
    {
        lock (_lock)
        {
            // Chain writes so concurrent updates never interleave on the file.
            _lastWrite = _lastWrite.ContinueWith(
                _ => SaveNow(), TaskScheduler.Default).Unwrap();
        }
    }

    private static async Task SaveNow()
    {
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_data);
            }
            string tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogService.Warn("StartupSnapshot: save failed ({Error})", ex.Message);
        }
    }

    /// <summary>Awaits the pending write chain. Test hook.</summary>
    internal static Task FlushForTestsAsync()
    {
        lock (_lock) return _lastWrite;
    }

    /// <summary>Clears in-memory state. Test hook.</summary>
    internal static void ResetForTests()
    {
        lock (_lock)
        {
            _data = new SnapshotData();
            _lastWrite = Task.CompletedTask;
        }
    }
}
