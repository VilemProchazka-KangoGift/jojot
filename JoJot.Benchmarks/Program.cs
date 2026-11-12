using System.Diagnostics;
using JoJot.Benchmarks;
using JoJot.Services;
using Microsoft.Data.Sqlite;

if (args.Length > 0)
{
    await StartupDatabase.RunAsync(args);
    return;
}

const string desktop = "benchmark-desktop";
const int iterations = 500;
const int samples = 15;

LogService.InitializeNoop();
var path = Path.Combine(Path.GetTempPath(), $"jojot-bench-{Guid.NewGuid():N}.db");
try
{
    await DatabaseCore.OpenAsync(path);
    await DatabaseCore.EnsureSchemaAsync();
    foreach (var count in new[] { 10, 100, 1000 })
    {
        var guid = $"{desktop}-{count}";
        for (var i = 0; i < count; i++)
            await NoteStore.InsertNoteAsync(guid, $"Note {i}", new string('x', 1024), i < 10, i);
    }

    foreach (var count in new[] { 10, 100, 1000 })
    {
        var guid = $"{desktop}-{count}";
        await Measure($"Load {count} notes", async () =>
        {
            var notes = await NoteStore.GetNotesForDesktopAsync(guid);
            if (notes.Count != count) throw new InvalidOperationException("Incorrect note count");
        });
    }

    var hundredGuid = $"{desktop}-100";
    await Measure("Count notes", async () =>
    {
        if (await NoteStore.GetNoteCountForDesktopAsync(hundredGuid) != 100)
            throw new InvalidOperationException("Incorrect note count");
    });
    await Measure("Get max sort order", async () =>
    {
        if (await NoteStore.GetMaxSortOrderAsync(hundredGuid) != 99)
            throw new InvalidOperationException("Incorrect sort order");
    });
    await Measure("Get 5 note names", async () =>
    {
        if ((await NoteStore.GetNoteNamesForDesktopAsync(hundredGuid)).Count != 5)
            throw new InvalidOperationException("Incorrect name count");
    });
    await Measure("Get 5 note previews", async () =>
    {
        if ((await NoteStore.GetNotePreviewsForDesktopAsync(hundredGuid)).Count != 5)
            throw new InvalidOperationException("Incorrect preview count");
    });

    var firstId = (await NoteStore.GetNotesForDesktopAsync(hundredGuid))[0].Id;
    var content = new string('y', 1024);
    await Measure("Update note content", () => NoteStore.UpdateNoteContentAsync(firstId, content));
    await Measure("Update note name", () => NoteStore.UpdateNoteNameAsync(firstId, "Renamed"));

    var inserted = 0;
    await Measure("Insert 1 KiB note", async () =>
    {
        var id = await NoteStore.InsertNoteAsync("benchmark-inserts", "New note", content, false, inserted++);
        if (id <= 0) throw new InvalidOperationException("Insert failed");
    });
}
finally
{
    await DatabaseCore.CloseAsync();
    SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm", ".clean-shutdown" })
    {
        try { File.Delete(path + suffix); }
        catch (IOException) { /* A pooled native SQLite handle can outlive CloseAsync. */ }
    }
}

static async Task Measure(string name, Func<Task> action)
{
    for (var i = 0; i < 100; i++) await action();
    var values = new double[samples];
    for (var sample = 0; sample < samples; sample++)
    {
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) await action();
        values[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000 / iterations;
    }
    Array.Sort(values);
    Console.WriteLine($"{name}: median {values[samples / 2]:F1} us/op; range {values[0]:F1}-{values[^1]:F1} us/op");
}
