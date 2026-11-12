using JoJot.Models;
using JoJot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JoJot.Storage.Tests;

/// <summary>Shared-store compatibility against a copied EF/WPF fixture, not a frontend process test.</summary>
public sealed class CompatibilityParityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LegacyFixtureMigrationsAndRichRowsSurviveEditsAndReopen(int version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jojot-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "jojot.db");
        var fixture = Path.Combine(directory, "original.db");
        try
        {
            await using (var source = typeof(CompatibilityParityTests).Assembly.GetManifestResourceStream("ef-sqlite-v2.db")!)
            await using (var output = File.Create(fixture)) await source.CopyToAsync(output);
            File.Copy(fixture, path);
            AppEnvironment.SetAppDataDirectory(directory);
            await DatabaseCore.OpenAsync(path);
            if (version == 0)
                await DatabaseCore.ExecuteNonQueryAsync("ALTER TABLE notes DROP COLUMN file_path; ALTER TABLE app_state DROP COLUMN window_state;");
            if (version < 2)
                await DatabaseCore.ExecuteNonQueryAsync("DROP INDEX idx_notes_desktop; CREATE INDEX idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);");
            await DatabaseCore.ExecuteNonQueryAsync($"PRAGMA user_version = {version};");
            await DatabaseCore.EnsureSchemaAsync();
            await DatabaseCore.RunPendingMigrationsAsync();
            Assert.True(await DatabaseCore.VerifyIntegrityAsync());
            Assert.Equal(2L, await DatabaseCore.ExecuteScalarAsync<long>("PRAGMA user_version"));
            await AssertMetadataEqualsFixtureAsync(fixture, path);

            await SessionStore.CreateSessionAsync("compat-č", "Plocha 🐇", 7);
            await SessionStore.SaveWindowGeometryAsync("compat-č", new WindowGeometry(12.5, 33.25, 850, 560, true));
            var fileId = await NoteStore.InsertNoteAsync("compat-č", "soubor č", "before 🐇", true, 3, @"C:\notes\č.txt");
            var nullId = await NoteStore.InsertNoteAsync("compat-č", null, "untouched Ω", false, 9);
            await DatabaseCore.ExecuteNonQueryAsync("UPDATE notes SET cursor_position=17, editor_scroll_offset=42 WHERE id=@id;", ("@id", fileId));
            await DatabaseCore.ExecuteNonQueryAsync("INSERT INTO preferences(key,value) VALUES(@key,@value);", ("@key", "future.unknown"), ("@value", "č & Ω"));
            var moveId = await PendingMoveStore.InsertPendingMoveAsync("compat-window", "compat-č", null);
            await NoteStore.UpdateNoteContentAsync(fileId, "after 🐇");
            await DatabaseCore.CloseAsync();
            await DatabaseCore.OpenAsync(path);
            Assert.True(await DatabaseCore.VerifyIntegrityAsync());
            var notes = await NoteStore.GetNotesForDesktopAsync("compat-č");
            Assert.Equal(2, notes.Count);
            var file = Assert.Single(notes, n => n.Id == fileId);
            Assert.Equal("soubor č", file.Name);
            Assert.Equal("after 🐇", file.Content);
            Assert.True(file.Pinned);
            Assert.Equal(3, file.SortOrder);
            Assert.Equal(@"C:\notes\č.txt", file.FilePath);
            Assert.Equal(17, file.CursorPosition);
            Assert.Equal(42, file.EditorScrollOffset);
            var other = Assert.Single(notes, n => n.Id == nullId);
            Assert.Null(other.Name);
            Assert.Null(other.FilePath);
            Assert.Equal("untouched Ω", other.Content);
            Assert.Equal(9, other.SortOrder);
            Assert.Equal(new WindowGeometry(12.5, 33.25, 850, 560, true), await SessionStore.GetWindowGeometryAsync("compat-č"));
            Assert.Contains(await SessionStore.GetAllSessionsAsync(), s => s.DesktopGuid == "compat-č" && s.DesktopName == "Plocha 🐇" && s.DesktopIndex == 7);
            Assert.Equal("č & Ω", await PreferenceStore.GetPreferenceAsync("future.unknown"));
            Assert.Contains(await PendingMoveStore.GetPendingMovesAsync(), m => m.Id == moveId && m.WindowId == "compat-window" && m.FromDesktop == "compat-č" && m.ToDesktop == null);
            Assert.Contains(await NoteStore.GetNotesForDesktopAsync("legacy-desktop"), n => n.Content == "Text with 'quotes' and čárky.");
            await NoteStore.DeleteNoteAsync(nullId);
            await DatabaseCore.CloseAsync();
            await DatabaseCore.OpenAsync(path);
            Assert.DoesNotContain(await NoteStore.GetNotesForDesktopAsync("compat-č"), n => n.Id == nullId);
        }
        finally
        {
            await DatabaseCore.CloseAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertMetadataEqualsFixtureAsync(string fixture, string migrated)
    {
        await using var expected = new SqliteConnection($"Data Source={fixture};Mode=ReadOnly;Pooling=False");
        await using var actual = new SqliteConnection($"Data Source={migrated};Mode=ReadOnly;Pooling=False");
        await expected.OpenAsync();
        await actual.OpenAsync();
        foreach (var table in new[] { "app_state", "notes", "preferences", "pending_moves" })
        {
            var columns = $"SELECT name || ':' || type || ':' || \"notnull\" || ':' || coalesce(dflt_value,'-') || ':' || pk FROM pragma_table_info('{table}') ORDER BY name";
            var indexes = $"SELECT name || ':' || \"unique\" || ':' || origin FROM pragma_index_list('{table}') ORDER BY name";
            Assert.Equal(await QueryAsync(expected, columns), await QueryAsync(actual, columns));
            Assert.Equal(await QueryAsync(expected, indexes), await QueryAsync(actual, indexes));
        }
        foreach (var index in new[] { "idx_notes_desktop", "IX_app_state_desktop_guid" })
        {
            var sql = $"SELECT coalesce(name,'-') || ':' || \"desc\" || ':' || key FROM pragma_index_xinfo('{index}') ORDER BY seqno";
            Assert.Equal(await QueryAsync(expected, sql), await QueryAsync(actual, sql));
        }
    }

    private static async Task<List<string>> QueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }
}
