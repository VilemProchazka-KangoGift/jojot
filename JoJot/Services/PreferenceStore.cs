using Microsoft.EntityFrameworkCore;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the preferences table.
/// </summary>
public static class PreferenceStore
{
    /// <summary>
    /// Gets a preference value by key. Returns null if the key doesn't exist.
    /// </summary>
    public static async Task<string?> GetPreferenceAsync(string key, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
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
        finally
        {
            DatabaseCore.ReleaseWriteLock();
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
            using var context = DatabaseCore.CreateContext();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO preferences (key, value) VALUES ({0}, {1}) ON CONFLICT(key) DO UPDATE SET value = {1}",
                key, value).ConfigureAwait(false);
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
