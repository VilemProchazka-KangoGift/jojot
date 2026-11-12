using System.IO;
using JoJot.Services;

namespace JoJot.Tests.Services;

public class AppEnvironmentTests
{
#if DEBUG
    private const string ExpectedFolder = "JoJot.Dev";
    private const string ExpectedSuffix = ".Dev";
    private const bool ExpectedDebug = true;
#else
    private const string ExpectedFolder = "JoJot";
    private const string ExpectedSuffix = "";
    private const bool ExpectedDebug = false;
#endif

    [Fact]
    public void AppDataDirectory_EndsWithExpectedFolderName()
    {
        AppEnvironment.AppDataDirectory.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ExpectedFolder));
    }

    [Fact]
    public void DatabasePath_IsInsideAppDataDirectory()
    {
        AppEnvironment.DatabasePath.Should().Be(Path.Combine(AppEnvironment.AppDataDirectory, "jojot.db"));
    }

    [Fact]
    public void PipeName_IsSessionScopedWithExpectedSuffix()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        AppEnvironment.PipeName.Should().Be($"JoJot_IPC_S{process.SessionId}{ExpectedSuffix}");
    }

    [Fact]
    public void MutexName_HasExpectedNamespaceAndSuffix()
    {
        AppEnvironment.MutexName.Should().Be($"Local\\JoJot_SingleInstance{ExpectedSuffix}");
    }

    [Fact]
    public void IsDebug_MatchesBuildConfiguration()
    {
        AppEnvironment.IsDebug.Should().Be(ExpectedDebug);
    }
}
