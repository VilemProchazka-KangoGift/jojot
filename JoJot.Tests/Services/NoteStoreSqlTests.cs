using JoJot.Services;
using JoJot.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace JoJot.Tests.Services;

[Collection("Database")]
public sealed class NoteStoreSqlTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<SqliteConnection> OpenReaderAsync()
    {
        var connection = new SqliteConnection(DatabaseCore.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task InsertUpdatesAndQueries_PreserveValuesAndScope()
    {
        const string guid = "desk' OR 1=1 --";
        const string hostile = "<>&'\" {0} %_";
        var id = await NoteStore.InsertNoteAsync(guid, null, hostile, false, 4, hostile);
        var untouched = await NoteStore.InsertNoteAsync("other", "other", "safe", false, 3);
        var pinned = await NoteStore.InsertNoteAsync(guid, "pinned", "text", true, 8);
        var rows = await NoteStore.GetNotesForDesktopAsync(guid);
        Assert.Equal(new[] { pinned, id }, rows.Select(n => n.Id));
        Assert.Null(rows[1].Name);
        Assert.Equal(hostile, rows[1].Content);
        Assert.Equal(hostile, rows[1].FilePath);
        Assert.Equal(rows[1].CreatedAt, rows[1].UpdatedAt);
        Assert.Equal(8, await NoteStore.GetMaxSortOrderAsync(guid));
        Assert.Equal(-1, await NoteStore.GetMaxSortOrderAsync("missing"));
        Assert.Equal(2, await NoteStore.GetNoteCountForDesktopAsync(guid));
        var counts = await NoteStore.GetNoteCountsForDesktopsAsync([guid, "missing", guid]);
        Assert.Single(counts);
        Assert.Equal(2, counts[guid]);

        await NoteStore.UpdateNoteContentAsync(id, hostile + " changed");
        await NoteStore.UpdateNoteNameAsync(id, hostile);
        await NoteStore.UpdateNotePinnedAsync(id, true);
        await NoteStore.UpdateNoteFilePathAndNameAsync(id, null, null);
        await using var conn = await OpenReaderAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, content, pinned, file_path FROM notes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
        Assert.Equal(hostile + " changed", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal("safe", (await NoteStore.GetNotesForDesktopAsync("other")).Single(n => n.Id == untouched).Content);
    }

    [Fact]
    public async Task EachUpdate_ChangesOnlyItsFieldsAndUsesTheClock()
    {
        var clock = new TestClock { Now = DateTime.Now };
        DatabaseCore.SetClock(clock);
        try
        {
            var id = await NoteStore.InsertNoteAsync("updates", "old", "original", false, 4, "old-path");
            var other = await NoteStore.InsertNoteAsync("updates", "other", "safe", false, 9);
            await using var conn = await OpenReaderAsync();
            async Task<(string? Name, string Content, bool Pinned, string? Path, DateTime Updated)> ReadAsync(long rowId)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name, content, pinned, file_path, updated_at FROM notes WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", rowId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetDateTime(4));
            }

            var unchanged = await ReadAsync(other);
            clock.Advance(TimeSpan.FromMinutes(1));
            await NoteStore.UpdateNoteContentAsync(id, "content'--");
            Assert.Equal(("old", "content'--", false, "old-path", clock.Now), await ReadAsync(id));
            clock.Advance(TimeSpan.FromMinutes(1));
            await NoteStore.UpdateNoteNameAsync(id, "name'--");
            Assert.Equal(("name'--", "content'--", false, "old-path", clock.Now), await ReadAsync(id));
            clock.Advance(TimeSpan.FromMinutes(1));
            await NoteStore.UpdateNotePinnedAsync(id, true);
            Assert.Equal(("name'--", "content'--", true, "old-path", clock.Now), await ReadAsync(id));
            var lastUpdated = clock.Now;
            clock.Advance(TimeSpan.FromMinutes(1));
            await NoteStore.UpdateNoteFilePathAndNameAsync(id, "path'--", null);
            Assert.Equal(((string?)null, "content'--", true, "path'--", lastUpdated), await ReadAsync(id));
            await NoteStore.UpdateNoteContentAsync(-1, "wrong");
            await NoteStore.UpdateNoteNameAsync(-1, "wrong");
            await NoteStore.UpdateNotePinnedAsync(-1, true);
            await NoteStore.UpdateNoteFilePathAndNameAsync(-1, "wrong", "wrong");
            Assert.Equal(unchanged, await ReadAsync(other));
        }
        finally
        {
            DatabaseCore.SetClock(SystemClock.Instance);
        }
    }

    [Fact]
    public async Task ReadNotes_RestoresStoredEditorPosition()
    {
        var id = await NoteStore.InsertNoteAsync("positions", null, "content", false, 0);
        await using (var conn = await OpenReaderAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE notes SET editor_scroll_offset=37, cursor_position=19 WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        var note = Assert.Single(await NoteStore.GetNotesForDesktopAsync("positions"));
        Assert.Equal(37, note.EditorScrollOffset);
        Assert.Equal(19, note.CursorPosition);
    }

    [Fact]
    public async Task DeleteOldNotes_UsesStrictCutoffAndProtectsPinnedRowsAcrossDesktops()
    {
        var cutoff = DateTime.Now;
        var old = await NoteStore.InsertNoteAsync("first", null, "old", false, 0);
        var equal = await NoteStore.InsertNoteAsync("first", null, "equal", false, 1);
        var newer = await NoteStore.InsertNoteAsync("second", null, "newer", false, 0);
        var pinned = await NoteStore.InsertNoteAsync("second", null, "pinned", true, 1);
        await using (var conn = await OpenReaderAsync())
        {
            foreach (var (id, date) in new[] { (old, cutoff.AddSeconds(-1)), (equal, cutoff), (newer, cutoff.AddSeconds(1)), (pinned, cutoff.AddSeconds(-1)) })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE notes SET updated_at=@date WHERE id=@id";
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        Assert.Equal(1, await NoteStore.DeleteOldNotesAsync(cutoff));
        Assert.Equal(equal, Assert.Single(await NoteStore.GetNotesForDesktopAsync("first")).Id);
        var remaining = await NoteStore.GetNotesForDesktopAsync("second");
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, n => n.Id == newer);
        Assert.Contains(remaining, n => n.Id == pinned);
    }

    [Fact]
    public async Task PreviewsAndNames_UseTheirDocumentedOrderAndExcerptRules()
    {
        const string guid = "preview'--";
        var content = new string('a', 59) + "😀suffix";
        await NoteStore.InsertNoteAsync(guid, " ", content, true, 2);
        await NoteStore.InsertNoteAsync(guid, "", "second", false, 1);
        await NoteStore.InsertNoteAsync("elsewhere", "wrong", "wrong", false, 0);
        var names = await NoteStore.GetNoteNamesForDesktopAsync(guid, 2);
        Assert.Equal(new[] { "second", " " }, names);
        var previews = await NoteStore.GetNotePreviewsForDesktopAsync(guid, 2);
        Assert.Equal(2, previews.Count);
        Assert.Null(previews[0].Name);
        Assert.Equal("second", previews[0].Excerpt);
        Assert.Null(previews[1].Name);
        Assert.Equal(content[..60], previews[1].Excerpt);
        Assert.Single(await NoteStore.GetNotePreviewsForDesktopAsync(guid, 1));
        Assert.Single(await NoteStore.GetNoteNamesForDesktopAsync(guid, 1));
    }

    [Fact]
    public async Task SortAndDeletes_OnlyAffectRequestedRowsAndHonorCutoff()
    {
        var first = await NoteStore.InsertNoteAsync("sort", null, "one", false, 1);
        var second = await NoteStore.InsertNoteAsync("sort", null, "two", false, 2);
        var keep = await NoteStore.InsertNoteAsync("sort", null, "keep", true, 3);
        await NoteStore.UpdateNoteSortOrdersAsync([(first, 11), (first, 99), (second, -2)]);
        await using (var conn = await OpenReaderAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT sort_order FROM notes WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", first);
            Assert.Equal(11L, (long)(await cmd.ExecuteScalarAsync())!);
        }
        var rows = await NoteStore.GetNotesForDesktopAsync("sort");
        Assert.Equal(-2, rows.Single(n => n.Id == second).SortOrder);
        Assert.Equal(3, rows.Single(n => n.Id == keep).SortOrder);
        await NoteStore.DeleteNoteAsync(second);
        Assert.Equal(1, await NoteStore.DeleteNotesAsync([first, first, -1]));
        Assert.Equal(0, await NoteStore.DeleteNotesAsync([]));
        Assert.Equal(0, await NoteStore.DeleteOldNotesAsync(DateTime.Now.AddDays(1)));
        Assert.Equal(keep, (await NoteStore.GetNotesForDesktopAsync("sort")).Single().Id);
        var old = await NoteStore.InsertNoteAsync("sort", null, "old", false, 0);
        Assert.Equal(1, await NoteStore.DeleteOldNotesAsync(DateTime.Now.AddDays(1)));
        Assert.DoesNotContain(await NoteStore.GetNotesForDesktopAsync("sort"), n => n.Id == old);
    }

    [Fact]
    public async Task Batches_RollBackEarlierChunksWhenLaterChunkFails()
    {
        var first = await NoteStore.InsertNoteAsync("batch", null, "first", false, 1);
        var last = await NoteStore.InsertNoteAsync("batch", null, "last", false, 2);
        await using (var conn = await OpenReaderAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TRIGGER fail_sort BEFORE UPDATE OF sort_order ON notes WHEN OLD.id = {last} BEGIN SELECT RAISE(ABORT, 'sort failure'); END";
            await cmd.ExecuteNonQueryAsync();
        }
        var updates = new List<(long Id, int SortOrder)> { (first, 100) };
        updates.AddRange(Enumerable.Range(1, 299).Select(i => (-(long)i, i)));
        updates.Add((last, 200));
        await Assert.ThrowsAsync<SqliteException>(() => NoteStore.UpdateNoteSortOrdersAsync(updates));
        Assert.Equal(1, (await NoteStore.GetNotesForDesktopAsync("batch")).Single(n => n.Id == first).SortOrder);

        await using (var conn = await OpenReaderAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TRIGGER fail_delete BEFORE DELETE ON notes WHEN OLD.id = {last} BEGIN SELECT RAISE(ABORT, 'delete failure'); END";
            await cmd.ExecuteNonQueryAsync();
        }
        var ids = new List<long> { first };
        ids.AddRange(Enumerable.Range(1, 899).Select(i => -(long)i));
        ids.Add(last);
        await Assert.ThrowsAsync<SqliteException>(() => NoteStore.DeleteNotesAsync(ids));
        var rows = await NoteStore.GetNotesForDesktopAsync("batch");
        Assert.Contains(rows, n => n.Id == first);
        Assert.Contains(rows, n => n.Id == last);

        await using (var conn = await OpenReaderAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP TRIGGER fail_sort; DROP TRIGGER fail_delete";
            await cmd.ExecuteNonQueryAsync();
        }
        await NoteStore.UpdateNoteSortOrdersAsync(updates);
        rows = await NoteStore.GetNotesForDesktopAsync("batch");
        Assert.Equal(100, rows.Single(n => n.Id == first).SortOrder);
        Assert.Equal(200, rows.Single(n => n.Id == last).SortOrder);
        var guids = Enumerable.Range(0, 900).Select(i => $"missing-{i}").Append("batch").ToArray();
        Assert.Equal(2, (await NoteStore.GetNoteCountsForDesktopsAsync(guids))["batch"]);
        Assert.Equal(2, await NoteStore.DeleteNotesAsync(ids));
        Assert.Empty(await NoteStore.GetNotesForDesktopAsync("batch"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateTabs_OffsetsOrdersAndPreservesRequestedPins(bool preservePins)
    {
        const string source = "source'--";
        const string target = "target'--";
        var moved = await NoteStore.InsertNoteAsync(source, "move", "payload", true, 2);
        var existing = await NoteStore.InsertNoteAsync(target, "existing", "target", false, 7);
        var unrelated = await NoteStore.InsertNoteAsync("unrelated", "other", "safe", true, 9);
        if (preservePins) await NoteStore.MigrateTabsPreservePinsAsync(source, target);
        else await NoteStore.MigrateTabsAsync(source, target);
        var rows = await NoteStore.GetNotesForDesktopAsync(target);
        Assert.Equal(10, rows.Single(n => n.Id == moved).SortOrder);
        Assert.Equal(preservePins, rows.Single(n => n.Id == moved).Pinned);
        Assert.Equal(7, rows.Single(n => n.Id == existing).SortOrder);
        Assert.Equal(unrelated, (await NoteStore.GetNotesForDesktopAsync("unrelated")).Single().Id);
        Assert.Empty(await NoteStore.GetNotesForDesktopAsync(source));
        await NoteStore.MigrateNotesDesktopGuidAsync(target, source);
        Assert.Equal(2, await NoteStore.GetNoteCountForDesktopAsync(source));
        Assert.Equal(unrelated, (await NoteStore.GetNotesForDesktopAsync("unrelated")).Single().Id);
    }
}
