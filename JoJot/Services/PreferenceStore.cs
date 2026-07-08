using Microsoft.EntityFrameworkCore;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the preferences table.
/// </summary>
public static class PreferenceStore
{
    private static Dictionary<string, string>? _cache;
    private static readonly Lock _cacheLock = new();

    /// <summary>
    /// Loads every preference row into the in-memory cache. Called once at startup after the
    /// schema is ensured. Returns the loaded map so callers can reconcile external snapshots.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> InitializeCacheAsync(CancellationToken ct = default)
    {
        await using var context = DatabaseCore.CreateContext();
        var all = await context.Preferences
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct).ConfigureAwait(false);
        lock (_cacheLock)
        {
            _cache = all;
        }
        return all;
    }

    /// <summary>
    /// Seeds the cache from a disk snapshot before the database is open, so language/theme
    /// reads during early startup are instant. A later InitializeCacheAsync replaces it with
    /// database truth. No-op if the real cache is already loaded.
    /// </summary>
    public static void PreloadProvisional(IReadOnlyDictionary<string, string> snapshot)
    {
        lock (_cacheLock)
        {
            _cache ??= new Dictionary<string, string>(snapshot);
        }
    }

    /// <summary>Clears the cache so tests start from an uncached state.</summary>
    internal static void ResetCacheForTests()
    {
        lock (_cacheLock)
        {
            _cache = null;
        }
    }

    /// <summary>
    /// Gets a preference value by key. Returns null if the key doesn't exist.
    /// </summary>
    public static async Task<string?> GetPreferenceAsync(string key, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cache is not null)
                return _cache.TryGetValue(key, out var cached) ? cached : null;
        }
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await context.Preferences
                .AsNoTracking()
                .Where(p => p.Key == key)
                .Select(p => p.Value)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("GetPreferenceAsync failed for key: {Key}", key, ex);
            return null;
        }
    }

    /// <summary>
    /// Upserts a preference value. Uses raw SQL for ON CONFLICT upsert.
    /// </summary>
    public static async Task SetPreferenceAsync(string key, string value)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO preferences (key, value) VALUES ({0}, {1}) ON CONFLICT(key) DO UPDATE SET value = {1}",
                key, value).ConfigureAwait(false);

            lock (_cacheLock)
            {
                if (_cache is not null)
                    _cache[key] = value;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("SetPreferenceAsync failed for key: {Key}", key, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }
}
