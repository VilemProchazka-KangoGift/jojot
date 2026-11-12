using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using JoJot.Models;
using JoJot.Services;
using Microsoft.Data.Sqlite;

namespace JoJot.Benchmarks;

internal static class StartupDatabase
{
    private const int FixtureNoteCount = 100;

    public static async Task RunAsync(string[] args)
    {
        LogService.InitializeNoop();
        if (args is ["seed-startup" or "seed-compatibility", var directory])
        {
            await SeedAsync(Path.GetFullPath(directory), args[0] == "seed-compatibility");
        }
        else if (args is ["measure-startup-db", var database, var desktop])
        {
            await MeasureAsync(Path.GetFullPath(database), desktop);
        }
        else
        {
            throw new ArgumentException(
                "Use seed-startup <new directory>, seed-compatibility <new directory>, or measure-startup-db <database copy> <desktop>.");
        }
    }

    private static async Task SeedAsync(string directory, bool compatibility)
    {
        var desktop = compatibility ? "default" : DetectDesktop();
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "jojot.db");
        using (File.Open(database, FileMode.CreateNew))
        {
        }

        // All writes, including the preference snapshot, stay inside the fixture directory.
        StartupSnapshot.OverrideFilePathForTests = Path.Combine(directory, "startup-cache.json");
        await DatabaseCore.OpenAsync(database);
        try
        {
            await DatabaseCore.EnsureSchemaAsync();
            await DatabaseCore.RunPendingMigrationsAsync();
            if (!await DatabaseCore.VerifyIntegrityAsync())
            {
                throw new InvalidOperationException("Fixture database failed verification.");
            }

            await SessionStore.CreateSessionAsync(desktop, "Fixture desktop", 0);
            var geometry = new WindowGeometry(80, 80, 850, 560, false);
            await SessionStore.SaveWindowGeometryAsync(desktop, geometry);
            StartupSnapshot.UpdateGeometry(desktop, geometry);
            await PreferenceStore.SetPreferenceAsync("theme", "light");
            await PreferenceStore.SetPreferenceAsync("language", "en");
            await PreferenceStore.SetPreferenceAsync("font_size", "14");
            await PreferenceStore.SetPreferenceAsync("log_level", "Information");
            var content = new string('x', 1024);
            for (var index = 0; index < FixtureNoteCount; index++)
            {
                await NoteStore.InsertNoteAsync(desktop, $"Note {index}", content, index < 10, index);
            }

            if (compatibility)
            {
                await SessionStore.CreateSessionAsync("legacy-desktop", null, null);
                await NoteStore.InsertNoteAsync("legacy-desktop", null, "Text with 'quotes' and čárky.", false, 0);
                await NoteStore.InsertNoteAsync("legacy-desktop", "File-backed note", "Local content", true, 1, @"C:\Fixture\example.txt");
                await PendingMoveStore.InsertPendingMoveAsync("legacy-window", "legacy-desktop", null);
            }
            await StartupSnapshot.FlushForTestsAsync();
        }
        finally
        {
            await DatabaseCore.CloseAsync();
            SqliteConnection.ClearAllPools();
        }

        File.WriteAllText(Path.Combine(directory, "desktop.txt"), desktop);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        command.CommandText = """
            SELECT sql FROM sqlite_master
            WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'
            ORDER BY type DESC, name;
            """;
        using var reader = command.ExecuteReader();
        using var output = File.CreateText(Path.Combine(directory, "schema.sql"));
        while (reader.Read())
        {
            output.WriteLine(reader.GetString(0) + ";");
        }
        output.WriteLine($"PRAGMA user_version = {version};");
    }

    private static string DetectDesktop()
    {
        var desktop = "default";
        var thread = new Thread(() =>
        {
            VirtualDesktopService.InitializeAsync().GetAwaiter().GetResult();
            desktop = VirtualDesktopService.CurrentDesktopGuid;
            VirtualDesktopService.Shutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return desktop;
    }

    private static async Task MeasureAsync(string database, string desktop)
    {
        if (!File.Exists(database))
        {
            throw new FileNotFoundException("Pass a copy of the seeded startup database.", database);
        }

        using var process = Process.GetCurrentProcess();
        var fromProcessStart = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var start = Stopwatch.GetTimestamp();
        try
        {
            await DatabaseCore.OpenAsync(database);
            await DatabaseCore.EnsureSchemaAsync();
            if (!await DatabaseCore.VerifyIntegrityAsync())
            {
                throw new InvalidOperationException("Startup database failed verification.");
            }

            // The same benchmark also runs against revisions where the EF startup step was removed.
            if (typeof(DatabaseCore).GetMethod("WarmupModelAsync")?.Invoke(null, null) is Task warmup)
            {
                await warmup;
            }
            var preferences = await PreferenceStore.InitializeCacheAsync();
            var haveSession = await SessionStore.SessionExistsAsync(desktop);
            await SessionStore.CreateSessionAsync(desktop, "Fixture desktop", 0);
            await DatabaseCore.RunPendingMigrationsAsync();
            await StartupService.CreateWelcomeTabIfFirstLaunch();
            var notes = await NoteStore.GetNotesForDesktopAsync(desktop);
            var elapsed = Stopwatch.GetElapsedTime(start);
            if (!haveSession || preferences.GetValueOrDefault("theme") != "light" ||
                notes.Count != FixtureNoteCount || notes[0].Name != "Note 0" || notes[0].Content.Length != 1024)
            {
                throw new InvalidOperationException("Startup did not load the expected fixture.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ProcessToNotesMs = (fromProcessStart + elapsed).TotalMilliseconds,
                StorageToNotesMs = elapsed.TotalMilliseconds,
                Notes = notes.Count
            }));
        }
        finally
        {
            await DatabaseCore.CloseAsync();
        }
    }
}
