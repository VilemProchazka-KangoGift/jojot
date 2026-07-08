using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>Tests for the in-memory preference cache layered over the preferences table.</summary>
[Collection("Database")]
public class PreferenceStoreCacheTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task InitializeCache_ReturnsAllRows_AndServesReadsFromMemory()
    {
        await PreferenceStore.SetPreferenceAsync("theme", "dark");
        await PreferenceStore.SetPreferenceAsync("language", "cs");

        var loaded = await PreferenceStore.InitializeCacheAsync();

        loaded.Should().ContainKey("theme").WhoseValue.Should().Be("dark");
        loaded.Should().ContainKey("language").WhoseValue.Should().Be("cs");

        // Mutate the DB behind the cache's back — a cached read must NOT see it.
        await DatabaseCore.ExecuteNonQueryAsync(
            "UPDATE preferences SET value = 'light' WHERE key = 'theme';");

        (await PreferenceStore.GetPreferenceAsync("theme")).Should().Be("dark",
            "reads must be served from the cache once initialized");
    }

    [Fact]
    public async Task SetPreference_UpdatesCacheAndDatabase()
    {
        await PreferenceStore.InitializeCacheAsync();

        await PreferenceStore.SetPreferenceAsync("font_size", "18");

        (await PreferenceStore.GetPreferenceAsync("font_size")).Should().Be("18");
        // Verify the row really hit the DB, not just the cache.
        var dbValue = await DatabaseCore.ExecuteScalarAsync<string>(
            "SELECT value FROM preferences WHERE key = 'font_size';");
        dbValue.Should().Be("18");
    }

    [Fact]
    public async Task GetPreference_FallsBackToDatabase_WhenCacheNotInitialized()
    {
        await PreferenceStore.SetPreferenceAsync("no_cache_key", "db_value");
        // Cache was never initialized in this test — read must come from the DB.
        (await PreferenceStore.GetPreferenceAsync("no_cache_key")).Should().Be("db_value");
    }

    [Fact]
    public async Task PreloadProvisional_ServesReads_UntilRealCacheLoads()
    {
        PreferenceStore.PreloadProvisional(new Dictionary<string, string> { ["theme"] = "dark" });

        (await PreferenceStore.GetPreferenceAsync("theme")).Should().Be("dark");

        // Real load replaces the provisional values with DB truth.
        await PreferenceStore.SetPreferenceAsync("theme", "light"); // writes DB + cache
        await PreferenceStore.InitializeCacheAsync();
        (await PreferenceStore.GetPreferenceAsync("theme")).Should().Be("light");
    }
}
