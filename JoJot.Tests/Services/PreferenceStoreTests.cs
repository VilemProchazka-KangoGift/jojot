using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Integration tests for PreferenceStore operations beyond basic CRUD
/// (which is already covered in DatabaseServiceTests).
/// </summary>
[Collection("Database")]
public class PreferenceStoreTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task SetPreference_CreatesNewKey()
    {
        await PreferenceStore.SetPreferenceAsync("new_key", "new_value");

        var value = await PreferenceStore.GetPreferenceAsync("new_key");
        value.Should().Be("new_value");
    }

    [Fact]
    public async Task SetPreference_UpsertOverwrites()
    {
        await PreferenceStore.SetPreferenceAsync("upsert_key", "first");
        await PreferenceStore.SetPreferenceAsync("upsert_key", "second");
        await PreferenceStore.SetPreferenceAsync("upsert_key", "third");

        var value = await PreferenceStore.GetPreferenceAsync("upsert_key");
        value.Should().Be("third");
    }

    [Fact]
    public async Task GetPreference_ReturnsNull_WhenKeyDoesNotExist()
    {
        var value = await PreferenceStore.GetPreferenceAsync("totally_missing_key");
        value.Should().BeNull();
    }

    [Fact]
    public async Task SetPreference_HandlesEmptyStringValue()
    {
        await PreferenceStore.SetPreferenceAsync("empty_val", "");

        var value = await PreferenceStore.GetPreferenceAsync("empty_val");
        value.Should().Be("");
    }

    [Fact]
    public async Task SetPreference_HandlesSpecialCharacters()
    {
        await PreferenceStore.SetPreferenceAsync("special", "it's a \"test\" with 'quotes'");

        var value = await PreferenceStore.GetPreferenceAsync("special");
        value.Should().Be("it's a \"test\" with 'quotes'");
    }

    [Fact]
    public async Task SetPreference_HandlesLongValues()
    {
        var longValue = new string('x', 10_000);
        await PreferenceStore.SetPreferenceAsync("long_key", longValue);

        var value = await PreferenceStore.GetPreferenceAsync("long_key");
        value.Should().Be(longValue);
    }

    [Fact]
    public async Task MultipleKeys_AreIndependent()
    {
        await PreferenceStore.SetPreferenceAsync("key_a", "value_a");
        await PreferenceStore.SetPreferenceAsync("key_b", "value_b");

        (await PreferenceStore.GetPreferenceAsync("key_a")).Should().Be("value_a");
        (await PreferenceStore.GetPreferenceAsync("key_b")).Should().Be("value_b");
    }
}
