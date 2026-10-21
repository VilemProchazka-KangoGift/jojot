using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>Tests the PRAGMA user_version schema stamp fast path.</summary>
[Collection("Database")]
public class SchemaVersionTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task FreshDatabase_HasVersionZero_UntilMigrationsStampIt()
    {
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(0);

        await DatabaseCore.RunPendingMigrationsAsync();

        (await DatabaseCore.GetUserVersionAsync()).Should().Be(DatabaseCore.CurrentSchemaVersion);
    }

    [Fact]
    public async Task EnsureSchema_SkipsWork_WhenStamped()
    {
        await DatabaseCore.RunPendingMigrationsAsync(); // stamps

        bool didWork = await DatabaseCore.EnsureSchemaAsync();

        didWork.Should().BeFalse("a stamped schema must skip EnsureCreated");
    }

    [Fact]
    public async Task EnsureSchema_RunsWork_WhenNotStamped()
    {
        // TestDatabase already ran EnsureSchemaAsync once, but the stamp is only
        // written by RunPendingMigrationsAsync — so a second call must still do work.
        bool didWork = await DatabaseCore.EnsureSchemaAsync();

        didWork.Should().BeTrue();
    }

    [Fact]
    public async Task Migrations_SkipProbes_WhenStamped()
    {
        await DatabaseCore.RunPendingMigrationsAsync(); // stamps
        // Second run must return without touching table_info (observable only via
        // timing/logs, so assert the cheap contract: version unchanged and no throw).
        await DatabaseCore.RunPendingMigrationsAsync();
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(DatabaseCore.CurrentSchemaVersion);
    }

    // ─── v2: idx_notes_desktop (desktop_guid, pinned DESC, sort_order) ───

    private const string IndexSqlQuery =
        "SELECT sql FROM sqlite_master WHERE type='index' AND name='idx_notes_desktop'";

    /// <summary>Returns "column:desc" for each key column of idx_notes_desktop, in index order.</summary>
    private static async Task<string> GetIndexKeyColumnsAsync()
    {
        return await DatabaseCore.ExecuteScalarAsync<string>(
            "SELECT group_concat(name || ':' || \"desc\", ',') FROM " +
            "(SELECT name, \"desc\" FROM pragma_index_xinfo('idx_notes_desktop') WHERE key = 1 ORDER BY seqno)");
    }

    [Fact]
    public void CurrentSchemaVersion_IsTwo()
    {
        DatabaseCore.CurrentSchemaVersion.Should().Be(2);
    }

    [Fact]
    public async Task FreshDatabase_IndexHasPinnedDescending_AfterSchemaAndMigrations()
    {
        await DatabaseCore.RunPendingMigrationsAsync();

        (await GetIndexKeyColumnsAsync()).Should().Be("desktop_guid:0,pinned:1,sort_order:0");
        (await DatabaseCore.ExecuteScalarAsync<string>(IndexSqlQuery)).Should().Contain("DESC");
        (await DatabaseCore.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND tbl_name='notes' AND sql IS NOT NULL"))
            .Should().Be(1, "the model index and the migration index must converge on one index");
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(2);
    }

    [Fact]
    public async Task V1Database_IsMigratedToV2_WithDescendingIndex()
    {
        // Recreate the v1 shape: all-ascending index, stamped at version 1.
        await DatabaseCore.ExecuteNonQueryAsync("DROP INDEX IF EXISTS idx_notes_desktop;");
        await DatabaseCore.ExecuteNonQueryAsync(
            "CREATE INDEX idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);");
        await DatabaseCore.ExecuteNonQueryAsync("PRAGMA user_version = 1;");
        (await GetIndexKeyColumnsAsync()).Should().Be("desktop_guid:0,pinned:0,sort_order:0");

        await DatabaseCore.RunPendingMigrationsAsync();

        (await GetIndexKeyColumnsAsync()).Should().Be("desktop_guid:0,pinned:1,sort_order:0");
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(2);
        (await DatabaseCore.VerifyIntegrityAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task V1Database_Migration_PreservesNotes()
    {
        await NoteStore.InsertNoteAsync("mig-desk", "Kept", "body", true, 4);
        await DatabaseCore.ExecuteNonQueryAsync("DROP INDEX IF EXISTS idx_notes_desktop;");
        await DatabaseCore.ExecuteNonQueryAsync(
            "CREATE INDEX idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);");
        await DatabaseCore.ExecuteNonQueryAsync("PRAGMA user_version = 1;");

        await DatabaseCore.RunPendingMigrationsAsync();

        var notes = await NoteStore.GetNotesForDesktopAsync("mig-desk");
        notes.Should().ContainSingle().Which.Content.Should().Be("body");
    }
}
