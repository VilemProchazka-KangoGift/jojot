using System.IO;
using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// One-time removal of log files from the old naming scheme, which the rolling retention
/// (jojot{Date}.log pattern only) never deletes.
/// </summary>
public class LogServiceLegacyCleanupTests : IDisposable
{
    private readonly string _tempDir;

    public LogServiceLegacyCleanupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jojot_logcleanup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void CleanupLegacyLogs_DeletesBothLegacyFiles()
    {
        Touch("jojot.log");
        Touch("jojot.log.old");

        var deleted = LogService.CleanupLegacyLogs(_tempDir);

        deleted.Should().BeEquivalentTo(["jojot.log", "jojot.log.old"]);
        File.Exists(Path.Combine(_tempDir, "jojot.log")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, "jojot.log.old")).Should().BeFalse();
    }

    [Fact]
    public void CleanupLegacyLogs_LeavesCurrentLogsAndOtherFilesAlone()
    {
        var keep = new[]
        {
            Touch("jojot20261021.log"),
            Touch("jojot20261021_001.log"),
            Touch("jojot.db"),
            Touch("jojot.log.bak"),
            Touch("startup-cache.json"),
        };
        Touch("jojot.log");

        var deleted = LogService.CleanupLegacyLogs(_tempDir);

        deleted.Should().Equal("jojot.log");
        foreach (var path in keep)
        {
            File.Exists(path).Should().BeTrue(path);
        }
    }

    [Fact]
    public void CleanupLegacyLogs_NothingToDelete_ReturnsEmpty()
    {
        LogService.CleanupLegacyLogs(_tempDir).Should().BeEmpty();
    }

    [Fact]
    public void CleanupLegacyLogs_MissingDirectory_DoesNotThrow()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        LogService.CleanupLegacyLogs(missing).Should().BeEmpty();
    }

    [Fact]
    public void CleanupLegacyLogs_LockedFile_IsSkippedWithoutThrowing()
    {
        var locked = Touch("jojot.log");
        Touch("jojot.log.old");

        List<string> deleted;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deleted = [.. LogService.CleanupLegacyLogs(_tempDir)];
        }

        deleted.Should().Equal("jojot.log.old");
        File.Exists(locked).Should().BeTrue();
    }
}

/// <summary>
/// The production file sink is shared, so a second instance appends to the primary's file instead
/// of spilling into jojot{Date}_001.log (which ate the 5-file retention). Uses a local logger built
/// from the production configuration, not the global Log.Logger other tests swap concurrently.
/// </summary>
public class LogServiceSharedFileTests : IDisposable
{
    private readonly string _tempDir;

    public LogServiceSharedFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jojot_logshared_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ProductionFileSink_SecondWriterAppendsToSameFile()
    {
        using (var primary = LogService.CreateConfiguration(_tempDir).CreateLogger())
        {
            // Fatal: never filtered by the shared level switch other tests change.
            primary.Fatal("from primary");

            // The async sink opens the file on its worker thread; wait until it exists.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Directory.GetFiles(_tempDir, "jojot*.log").Length == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            // Stand-in for the second instance, while the primary still holds the file open.
            using (var second = LogService.CreateConfiguration(_tempDir).CreateLogger())
            {
                second.Fatal("from second instance");
            }
        }

        var files = Directory.GetFiles(_tempDir, "jojot*.log");
        files.Should().ContainSingle();
        var text = File.ReadAllText(files[0]);
        text.Should().Contain("from primary").And.Contain("from second instance");
    }
}
