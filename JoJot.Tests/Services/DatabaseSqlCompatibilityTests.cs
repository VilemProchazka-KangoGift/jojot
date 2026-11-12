using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>Compatibility checks against SQLite metadata and pre-stamp databases.</summary>
[Collection("Database")]
public class DatabaseSqlCompatibilityTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task FreshSchema_ExposesOriginalColumnsDefaultsAndIndexes()
    {
        // table_info pins every persisted column's name, type, nullability, default and PK.
        var expected = new Dictionary<string, string>
        {
            ["app_state"] = "id:INTEGER:1:-:1|desktop_guid:TEXT:1:-:0|desktop_name:TEXT:0:-:0|desktop_index:INTEGER:0:-:0|window_left:REAL:0:-:0|window_top:REAL:0:-:0|window_width:REAL:0:-:0|window_height:REAL:0:-:0|active_tab_id:INTEGER:0:-:0|scroll_offset:REAL:0:-:0|window_state:TEXT:0:-:0",
            ["notes"] = "id:INTEGER:1:-:1|desktop_guid:TEXT:1:-:0|name:TEXT:0:-:0|content:TEXT:1:'':0|pinned:INTEGER:1:0:0|created_at:TEXT:1:datetime('now'):0|updated_at:TEXT:1:datetime('now'):0|sort_order:INTEGER:1:0:0|editor_scroll_offset:INTEGER:1:0:0|cursor_position:INTEGER:1:0:0|file_path:TEXT:0:-:0",
            ["pending_moves"] = "id:INTEGER:1:-:1|window_id:TEXT:1:-:0|from_desktop:TEXT:1:-:0|to_desktop:TEXT:0:-:0|detected_at:TEXT:1:datetime('now'):0",
            ["preferences"] = "key:TEXT:1:-:1|value:TEXT:1:-:0"
        };
        foreach (var (table, columns) in expected)
        {
            var actual = await DatabaseCore.ExecuteScalarAsync<string>(
                $"SELECT group_concat(name || ':' || type || ':' || \"notnull\" || ':' || coalesce(dflt_value, '-') || ':' || pk, '|') FROM pragma_table_info('{table}');");
            actual.Should().Be(columns, $"the {table} schema must preserve the prior model");
        }

        (await DatabaseCore.ExecuteScalarAsync<long>("SELECT \"unique\" FROM pragma_index_list('app_state') WHERE name='IX_app_state_desktop_guid';")).Should().Be(1);
        (await DatabaseCore.ExecuteScalarAsync<string>("SELECT group_concat(name || ':' || \"desc\", '|') FROM pragma_index_xinfo('idx_notes_desktop') WHERE key=1;")).Should().Be("desktop_guid:0|pinned:1|sort_order:0");
        (await DatabaseCore.ExecuteScalarAsync<long>("PRAGMA foreign_keys;")).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExistingDatabase_MigrationPreservesDataAndCurrentSchema(int version)
    {
        var legacyDate = DateTime.UtcNow.AddDays(-2);
        await DatabaseCore.ExecuteNonQueryAsync("INSERT INTO notes (id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, file_path) VALUES (73, 'desk', NULL, 'legacy', 1, @date, @date, 4, NULL);", ("@date", legacyDate));
        await DatabaseCore.ExecuteNonQueryAsync("INSERT INTO app_state (id, desktop_guid, desktop_name) VALUES (17, 'desk', NULL); INSERT INTO preferences (key, value) VALUES ('legacy', 'kept'); INSERT INTO pending_moves (id, window_id, from_desktop, to_desktop) VALUES (9, 'window', 'desk', NULL);");
        if (version == 0)
        {
            await DatabaseCore.ExecuteNonQueryAsync("ALTER TABLE notes DROP COLUMN file_path; ALTER TABLE app_state DROP COLUMN window_state;");
        }
        if (version < 2)
        {
            await DatabaseCore.ExecuteNonQueryAsync("DROP INDEX idx_notes_desktop; CREATE INDEX idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);");
        }
        await DatabaseCore.ExecuteNonQueryAsync($"PRAGMA user_version = {version};");
        var schemaBefore = await SchemaSnapshotAsync();

        await DatabaseCore.EnsureSchemaAsync();
        (await DatabaseCore.VerifyIntegrityAsync()).Should().BeTrue();
        await DatabaseCore.RunPendingMigrationsAsync();

        (await DatabaseCore.GetUserVersionAsync()).Should().Be(2);
        if (version == 2) (await SchemaSnapshotAsync()).Should().Be(schemaBefore);
        (await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes WHERE id=73 AND desktop_guid='desk' AND name IS NULL AND content='legacy' AND pinned=1 AND sort_order=4 AND file_path IS NULL;")).Should().Be(1);
        var legacyNote = (await NoteStore.GetNotesForDesktopAsync("desk")).Single();
        legacyNote.CreatedAt.Should().Be(legacyDate);
        legacyNote.UpdatedAt.Should().Be(legacyDate);
        (await NoteStore.InsertNoteAsync("desk", null, "new", false, 0)).Should().BeGreaterThan(73);
        (await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM app_state WHERE id=17 AND desktop_name IS NULL;")).Should().Be(1);
        (await DatabaseCore.ExecuteScalarAsync<string>("SELECT value FROM preferences WHERE key='legacy';")).Should().Be("kept");
        (await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pending_moves WHERE id=9 AND to_desktop IS NULL;")).Should().Be(1);
        (await DatabaseCore.ExecuteScalarAsync<string>("SELECT group_concat(name || ':' || \"desc\", '|') FROM pragma_index_xinfo('idx_notes_desktop') WHERE key=1;")).Should().Be("desktop_guid:0|pinned:1|sort_order:0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EnsureSchema_DoesNotRepairPartialDatabase(int version)
    {
        await DatabaseCore.ExecuteNonQueryAsync("DROP TABLE pending_moves;");
        await DatabaseCore.ExecuteNonQueryAsync($"PRAGMA user_version = {version};");
        await DatabaseCore.EnsureSchemaAsync();
        (await DatabaseCore.VerifyIntegrityAsync()).Should().BeFalse();
        (await DatabaseCore.ExecuteScalarAsync<long>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='pending_moves';")).Should().Be(0);
    }

    [Fact]
    public async Task ParameterizedCommands_PreserveNullAndHostileText()
    {
        const string text = "apostrophe ' and %_ & < >";
        await DatabaseCore.ExecuteNonQueryAsync("INSERT INTO notes (desktop_guid, name, content) VALUES (@desktop, @name, @content);", ("@desktop", "params"), ("@name", null), ("@content", text));
        var note = (await NoteStore.GetNotesForDesktopAsync("params")).Single();
        note.Name.Should().BeNull();
        note.Content.Should().Be(text);
        note.Pinned.Should().BeFalse();
        note.SortOrder.Should().Be(0);
        note.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    private static Task<string> SchemaSnapshotAsync() => DatabaseCore.ExecuteScalarAsync<string>(
        "SELECT group_concat(type || ':' || name || ':' || coalesce(sql, ''), '|') FROM (SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name);");
}
