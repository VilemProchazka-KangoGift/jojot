using System.IO;
using JoJot.Services;
using JoJot.Tests.Helpers;
using Microsoft.Data.Sqlite;

namespace JoJot.Tests.Services;

/// <summary>Pure tests for <see cref="DatabaseCore.ShouldRunQuickCheck"/>.</summary>
public class QuickCheckPolicyTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Fact]
    public void NoMarker_Runs()
    {
        DatabaseCore.ShouldRunQuickCheck(false, Today, Today).Should().BeTrue();
        DatabaseCore.ShouldRunQuickCheck(false, null, Today).Should().BeTrue();
    }

    [Fact]
    public void Marker_WithoutDate_Runs()
    {
        DatabaseCore.ShouldRunQuickCheck(true, null, Today).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void Marker_RecentCheck_Skips(int daysAgo)
    {
        DatabaseCore.ShouldRunQuickCheck(true, Today.AddDays(-daysAgo), Today).Should().BeFalse();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(400)]
    public void Marker_CheckAWeekOrMoreOld_Runs(int daysAgo)
    {
        DatabaseCore.ShouldRunQuickCheck(true, Today.AddDays(-daysAgo), Today).Should().BeTrue();
    }

    [Fact]
    public void Marker_CheckDateInFuture_Runs()
    {
        // Clock moved backwards: the recorded date cannot be trusted.
        DatabaseCore.ShouldRunQuickCheck(true, Today.AddDays(1), Today).Should().BeTrue();
    }
}

/// <summary>
/// File-database tests for the clean-shutdown marker and the WAL checkpoint on close.
/// </summary>
[Collection("Database")]
public class CleanShutdownMarkerTests : IAsyncLifetime
{
    private readonly TestClock _clock = new() { UtcNow = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc) };
    private string _dir = null!;
    private string _dbPath = null!;
    private string _markerPath = null!;

    public Task InitializeAsync()
    {
        LogService.InitializeNoop();
        DatabaseCore.SetClock(_clock);
        _dir = Path.Combine(Path.GetTempPath(), $"jojot_marker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "jojot.db");
        _markerPath = Path.Combine(_dir, DatabaseCore.CleanShutdownMarkerName);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await DatabaseCore.CloseAsync();
        DatabaseCore.SetClock(SystemClock.Instance);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* best-effort temp cleanup */ }
    }

    private async Task OpenAndVerifyAsync()
    {
        await DatabaseCore.OpenAsync(_dbPath);
        await DatabaseCore.EnsureSchemaAsync();
        (await DatabaseCore.VerifyIntegrityAsync()).Should().BeTrue();
        await DatabaseCore.RunPendingMigrationsAsync();
    }

    [Fact]
    public async Task FirstRun_NoMarker_RunsCheck_AndCloseWritesMarkerWithToday()
    {
        File.Exists(_markerPath).Should().BeFalse();

        await OpenAndVerifyAsync();
        await DatabaseCore.CloseAsync();

        File.Exists(_markerPath).Should().BeTrue();
        (await File.ReadAllTextAsync(_markerPath)).Should().Be("2026-03-10");
    }

    [Fact]
    public async Task Open_DeletesMarker_SoACrashLeavesNone()
    {
        await File.WriteAllTextAsync(_markerPath, "2026-03-09");

        await DatabaseCore.OpenAsync(_dbPath);

        File.Exists(_markerPath).Should().BeFalse();
    }

    [Fact]
    public async Task CleanShutdown_RecentCheck_SkipsQuickCheck_AndKeepsDate()
    {
        await File.WriteAllTextAsync(_markerPath, "2026-03-08");

        await OpenAndVerifyAsync();
        await DatabaseCore.CloseAsync();

        // Skipped: the recorded last-check date is carried forward unchanged.
        (await File.ReadAllTextAsync(_markerPath)).Should().Be("2026-03-08");
    }

    [Fact]
    public async Task CleanShutdown_WeekOldCheck_RunsQuickCheck_AndRecordsToday()
    {
        await File.WriteAllTextAsync(_markerPath, "2026-03-03");

        await OpenAndVerifyAsync();
        await DatabaseCore.CloseAsync();

        (await File.ReadAllTextAsync(_markerPath)).Should().Be("2026-03-10");
    }

    [Fact]
    public async Task MarkerWithGarbage_RunsQuickCheck()
    {
        await File.WriteAllTextAsync(_markerPath, "not a date");

        await OpenAndVerifyAsync();
        await DatabaseCore.CloseAsync();

        (await File.ReadAllTextAsync(_markerPath)).Should().Be("2026-03-10");
    }

    [Fact]
    public async Task Close_CheckpointsAndTruncatesWal()
    {
        await OpenAndVerifyAsync();
        for (int i = 0; i < 50; i++)
        {
            await NoteStore.InsertNoteAsync("wal-desk", $"Note {i}", new string('x', 2000), false, i);
        }
        var walPath = _dbPath + "-wal";
        File.Exists(walPath).Should().BeTrue();
        new FileInfo(walPath).Length.Should().BeGreaterThan(0);

        await DatabaseCore.CloseAsync();

        (!File.Exists(walPath) || new FileInfo(walPath).Length == 0).Should().BeTrue(
            "wal_checkpoint(TRUNCATE) on close must leave no WAL to replay");
    }

    [Fact]
    public async Task Close_IsIdempotent_AndDoesNotRewriteMarkerWhenNothingOpen()
    {
        await OpenAndVerifyAsync();
        await DatabaseCore.CloseAsync();
        File.Delete(_markerPath);

        await DatabaseCore.CloseAsync();

        File.Exists(_markerPath).Should().BeFalse();
    }
}
