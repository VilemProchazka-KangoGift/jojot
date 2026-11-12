using JoJot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace JoJot.Storage.Tests;

public sealed class StorageIntegrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EfFixtureMigratesAndPersistsAllStores(int version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jojot-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.db");
        try
        {
            await using (var input = typeof(StorageIntegrationTests).Assembly.GetManifestResourceStream("ef-sqlite-v2.db")!)
            await using (var output = File.Create(path)) await input.CopyToAsync(output);
            AppEnvironment.SetAppDataDirectory(directory);
            await DatabaseCore.OpenAsync(path);
            if (version == 0)
                await DatabaseCore.ExecuteNonQueryAsync("ALTER TABLE notes DROP COLUMN file_path; ALTER TABLE app_state DROP COLUMN window_state;");
            if (version < 2)
                await DatabaseCore.ExecuteNonQueryAsync("DROP INDEX idx_notes_desktop; CREATE INDEX idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);");
            await DatabaseCore.ExecuteNonQueryAsync($"PRAGMA user_version = {version};");
            await DatabaseCore.EnsureSchemaAsync();
            Assert.True(await DatabaseCore.VerifyIntegrityAsync());
            await DatabaseCore.RunPendingMigrationsAsync();
            Assert.Equal(2L, await DatabaseCore.ExecuteScalarAsync<long>("PRAGMA user_version"));
            Assert.Contains(await NoteStore.GetNotesForDesktopAsync("legacy-desktop"), n => n.Name == null && n.Content == "Text with 'quotes' and čárky.");
            Assert.Contains(await PendingMoveStore.GetPendingMovesAsync(), m => m.WindowId == "legacy-window");
            Assert.Equal("light", await PreferenceStore.GetPreferenceAsync("theme"));
            Assert.True(await SessionStore.SessionExistsAsync("legacy-desktop"));
            var id = await NoteStore.InsertNoteAsync("portable", "name", "initial", false, 0);
            await NoteStore.UpdateNoteContentAsync(id, "updated č");
            await DatabaseCore.CloseAsync();
            await DatabaseCore.OpenAsync(path);
            Assert.Contains(await NoteStore.GetNotesForDesktopAsync("portable"), n => n.Id == id && n.Content == "updated č");
            await NoteStore.DeleteNoteAsync(id);
            Assert.Empty(await NoteStore.GetNotesForDesktopAsync("portable"));
        }
        finally
        {
            await DatabaseCore.CloseAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}
