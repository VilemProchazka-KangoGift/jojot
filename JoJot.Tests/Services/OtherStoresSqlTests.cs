using JoJot.Models;
using JoJot.Services;
using JoJot.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace JoJot.Tests.Services;

[Collection("Database")]
public sealed class OtherStoresSqlTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    public async Task InitializeAsync() => _database = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task SessionQueriesAndMutationsPreserveNullsNamesGeometryAndScope()
    {
        const string hostileGuid = "g'uid_%";
        await SessionStore.CreateSessionAsync(hostileGuid, null, null);
        await SessionStore.CreateSessionAsync(hostileGuid, "ignored", 7);
        await SessionStore.CreateSessionAsync("untouched", "Other", 8);
        (await SessionStore.SessionExistsAsync("G'UID_%")).Should().BeTrue();
        (await SessionStore.SessionExistsAsync("gXuidA%")).Should().BeFalse();
        (await SessionStore.GetDesktopNameAsync(hostileGuid)).Should().BeNull();
        await SessionStore.UpdateDesktopNameAsync(hostileGuid, "Work ' <>&");
        await SessionStore.SaveWindowGeometryAsync(hostileGuid, new WindowGeometry(-12.5, 20.25, 900.5, 700.75, true));
        (await SessionStore.GetWindowGeometryAsync(hostileGuid)).Should().Be(new WindowGeometry(-12.5, 20.25, 900.5, 700.75, true));
        (await SessionStore.GetWindowGeometryAsync("untouched")).Should().BeNull();
        await SessionStore.SaveWindowGeometryAsync("untouched", new WindowGeometry(1, 2, 640, 480, false));
        (await SessionStore.GetWindowGeometryAsync("untouched")).Should().Be(new WindowGeometry(1, 2, 640, 480, false));

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().Contain(x => x.DesktopGuid == hostileGuid && x.DesktopName == "Work ' <>&" && x.DesktopIndex == null);
        sessions.Should().Contain(x => x.DesktopGuid == "untouched" && x.DesktopIndex == 8);
        (await ScalarAsync("SELECT desktop_name FROM app_state WHERE desktop_guid = @value", hostileGuid)).Should().Be("Work ' <>&");
        (await ScalarAsync("SELECT window_state FROM app_state WHERE desktop_guid = @value", hostileGuid)).Should().Be("Maximized");
        (await ScalarAsync("SELECT desktop_name FROM app_state WHERE desktop_guid = @value", "untouched")).Should().Be("Other");
    }

    [Fact]
    public async Task SessionRehomingAndOrphanQueriesKeepRowsScopedAndReturnFallbacks()
    {
        await SessionStore.CreateSessionAsync("old", "Old", 1);
        await SessionStore.CreateSessionAsync("new", "Existing destination", 2);
        await NoteStore.InsertNoteAsync("old", "Tab", "old note", false, 0);
        await NoteStore.InsertNoteAsync("old", "Second", "another note", false, 1);
        await NoteStore.InsertNoteAsync("new", "Keep", "destination note", false, 0);
        await SessionStore.UpdateSessionAsync("old", "moved", null, null);
        (await NoteStore.GetNotesForDesktopAsync("moved")).Select(n => n.Content).Should().Contain("old note");
        (await NoteStore.GetNotesForDesktopAsync("new")).Select(n => n.Content).Should().Contain("destination note");

        await SessionStore.CreateSessionAsync("replace-old", "Old name", 0);
        await SessionStore.CreateSessionAsync("replace-new", "New name", 1);
        await NoteStore.InsertNoteAsync("replace-old", "Rehomed", "must stay old guid", false, 0);
        await SessionStore.UpdateSessionDesktopAsync("replace-old", "replace-new", "Merged", 3);
        (await SessionStore.SessionExistsAsync("replace-old")).Should().BeFalse();
        (await SessionStore.GetDesktopNameAsync("replace-new")).Should().Be("Merged");
        (await NoteStore.GetNotesForDesktopAsync("replace-old")).Should().ContainSingle();
        (await NoteStore.GetNotesForDesktopAsync("replace-new")).Should().BeEmpty();

        var info = await SessionStore.GetOrphanedSessionInfoAsync(["moved", "no-notes", "missing"]);
        info.Should().HaveCount(3);
        info[0].DesktopName.Should().BeNull();
        info[0].TabCount.Should().Be(2);
        info[0].LastUpdated.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
        info[1].TabCount.Should().Be(0);
        info[1].LastUpdated.Should().Be(new DateTime(2000, 1, 1));
        info[2].DesktopName.Should().BeNull();
        (await SessionStore.GetOrphanedSessionInfoAsync([])).Should().BeEmpty();

        await SessionStore.DeleteSessionAndNotesAsync("moved");
        (await ScalarAsync("SELECT COUNT(*) FROM app_state WHERE desktop_guid = @value", "moved")).Should().Be(0L);
        (await ScalarAsync("SELECT COUNT(*) FROM notes WHERE desktop_guid = @value", "moved")).Should().Be(0L);
        (await ScalarAsync("SELECT COUNT(*) FROM notes WHERE desktop_guid = @value", "new")).Should().Be(1L);
    }

    [Fact]
    public async Task PreferenceUpsertAndCacheMaintainSpecialAndEmptyValues()
    {
        await PreferenceStore.SetPreferenceAsync("quote'_%", "value <>& ' {0}");
        (await PreferenceStore.GetPreferenceAsync("quote'_%")).Should().Be("value <>& ' {0}");
        await PreferenceStore.SetPreferenceAsync("quote'_%", "");
        (await PreferenceStore.GetPreferenceAsync("quote'_%")).Should().BeEmpty();
        (await PreferenceStore.GetPreferenceAsync("quoteX_%")).Should().BeNull();

        var loaded = await PreferenceStore.InitializeCacheAsync();
        loaded["quote'_%"].Should().BeEmpty();
        await PreferenceStore.SetPreferenceAsync("quote'_%", "updated");
        (await PreferenceStore.GetPreferenceAsync("quote'_%")).Should().Be("updated");
        loaded["quote'_%"].Should().BeEmpty();
        (await ScalarAsync("SELECT value FROM preferences WHERE key = @value", "quote'_%")).Should().Be("updated");
    }

    [Fact]
    public async Task PendingMoveInsertReadAndDeletesPreserveNullableDestinationAndOtherRows()
    {
        await PendingMoveStore.DeleteAllPendingMovesAsync();
        long first = await PendingMoveStore.InsertPendingMoveAsync("w'_%", "from <>&", null);
        long second = await PendingMoveStore.InsertPendingMoveAsync("w'_%", "from 2", "to ' <>&");
        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().HaveCount(2);
        moves[0].Id.Should().Be(first);
        moves[0].ToDesktop.Should().BeNull();
        moves[0].FromDesktop.Should().Be("from <>&");
        moves[1].Id.Should().Be(second);
        moves[1].ToDesktop.Should().Be("to ' <>&");
        moves[0].DetectedAt.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$");

        await PendingMoveStore.DeletePendingMoveAsync("w'_%");
        (await PendingMoveStore.GetPendingMovesAsync()).Should().BeEmpty();
        await PendingMoveStore.InsertPendingMoveAsync("keep", "a", "b");
        await PendingMoveStore.InsertPendingMoveAsync("delete", "a", null);
        await PendingMoveStore.DeletePendingMoveAsync("delete");
        await PendingMoveStore.DeletePendingMoveAsync("absent");
        (await PendingMoveStore.GetPendingMovesAsync()).Should().ContainSingle(x => x.WindowId == "keep");
        (await ScalarAsync("SELECT COUNT(*) FROM pending_moves WHERE window_id = @value", "keep")).Should().Be(1L);
        await PendingMoveStore.DeleteAllPendingMovesAsync();
        (await PendingMoveStore.GetPendingMovesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OrphanInfoHandlesLargeListsAndDuplicatesAcrossChunks()
    {
        await SessionStore.CreateSessionAsync("large'_%", "Matched", null);
        await NoteStore.InsertNoteAsync("large'_%", null, "note", false, 0);
        var guids = Enumerable.Range(0, 1300).Select(i => $"missing-{i}").ToList();
        guids[499] = "large'_%";
        guids[1000] = "large'_%";

        var info = await SessionStore.GetOrphanedSessionInfoAsync(guids);

        info.Should().HaveCount(1300);
        info[499].DesktopName.Should().Be("Matched");
        info[499].TabCount.Should().Be(1);
        info[1000].Should().Be(info[499]);
        info[1299].DesktopGuid.Should().Be("missing-1299");
        info[1299].LastUpdated.Should().Be(new DateTime(2000, 1, 1));
    }

    [Fact]
    public async Task MultiTableUpdatesAndDeletesRollBackWhenSecondStatementFails()
    {
        await SessionStore.CreateSessionAsync("old", "Original", 1);
        await NoteStore.InsertNoteAsync("old", "Note", "text", false, 0);
        await ExecuteAsync("CREATE TRIGGER fail_note_move BEFORE UPDATE ON notes BEGIN SELECT RAISE(ABORT, 'blocked'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => SessionStore.UpdateSessionAsync("old", "new", null, null));
        (await ScalarAsync("SELECT desktop_name FROM app_state WHERE desktop_guid = @value", "old")).Should().Be("Original");
        (await ScalarAsync("SELECT COUNT(*) FROM notes WHERE desktop_guid = @value", "old")).Should().Be(1L);
        await ExecuteAsync("DROP TRIGGER fail_note_move;");

        await ExecuteAsync("CREATE TRIGGER fail_session_delete BEFORE DELETE ON app_state BEGIN SELECT RAISE(ABORT, 'blocked'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => SessionStore.DeleteSessionAndNotesAsync("old"));
        (await ScalarAsync("SELECT COUNT(*) FROM notes WHERE desktop_guid = @value", "old")).Should().Be(1L);
        (await ScalarAsync("SELECT COUNT(*) FROM app_state WHERE desktop_guid = @value", "old")).Should().Be(1L);
        await ExecuteAsync("DROP TRIGGER fail_session_delete;");

        await SessionStore.CreateSessionAsync("destination", "Destination", 2);
        await ExecuteAsync("CREATE TRIGGER fail_session_update BEFORE UPDATE ON app_state BEGIN SELECT RAISE(ABORT, 'blocked'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => SessionStore.UpdateSessionDesktopAsync("old", "destination", null, null));
        (await ScalarAsync("SELECT desktop_name FROM app_state WHERE desktop_guid = @value", "destination")).Should().Be("Destination");
        (await ScalarAsync("SELECT desktop_name FROM app_state WHERE desktop_guid = @value", "old")).Should().Be("Original");
    }

    private static async Task<object?> ScalarAsync(string sql, string value)
    {
        await using var connection = new SqliteConnection(DatabaseCore.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@value", value);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(DatabaseCore.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
