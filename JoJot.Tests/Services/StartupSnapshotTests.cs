using System.IO;
using JoJot.Models;
using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>Round-trip tests for the startup snapshot sidecar file.</summary>
public sealed class StartupSnapshotTests : IDisposable
{
    private readonly string _tempFile;

    public StartupSnapshotTests()
    {
        LogService.InitializeNoop();
        _tempFile = Path.Combine(Path.GetTempPath(), $"jojot-snap-{Guid.NewGuid():N}.json");
        StartupSnapshot.OverrideFilePathForTests = _tempFile;
        StartupSnapshot.ResetForTests();
    }

    public void Dispose()
    {
        StartupSnapshot.OverrideFilePathForTests = null;
        StartupSnapshot.ResetForTests();
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void TryLoad_ReturnsFalse_WhenFileMissing()
    {
        StartupSnapshot.TryLoad().Should().BeFalse();
    }

    [Fact]
    public async Task UpdateThenLoad_RoundTripsPreferencesAndGeometry()
    {
        StartupSnapshot.UpdatePreference("theme", "dark");
        StartupSnapshot.UpdateGeometry("desk-1", new WindowGeometry(10, 20, 800, 600, false));
        await StartupSnapshot.FlushForTestsAsync();

        StartupSnapshot.ResetForTests();
        StartupSnapshot.TryLoad().Should().BeTrue();
        StartupSnapshot.Preferences.Should().ContainKey("theme").WhoseValue.Should().Be("dark");
        StartupSnapshot.GetGeometry("desk-1").Should().Be(new WindowGeometry(10, 20, 800, 600, false));
        StartupSnapshot.GetGeometry("unknown").Should().BeNull();
    }

    [Fact]
    public async Task TryLoad_ReturnsFalse_AndDeletes_WhenFileCorrupt()
    {
        await File.WriteAllTextAsync(_tempFile, "{not json!!");

        StartupSnapshot.TryLoad().Should().BeFalse();
        File.Exists(_tempFile).Should().BeFalse("a corrupt snapshot must be discarded");
    }

    [Fact]
    public async Task ReplacePreferences_OverwritesProvisionalValues()
    {
        StartupSnapshot.UpdatePreference("theme", "dark");
        StartupSnapshot.ReplacePreferences(new Dictionary<string, string> { ["theme"] = "light" });
        await StartupSnapshot.FlushForTestsAsync();

        StartupSnapshot.ResetForTests();
        StartupSnapshot.TryLoad().Should().BeTrue();
        StartupSnapshot.Preferences["theme"].Should().Be("light");
    }
}
