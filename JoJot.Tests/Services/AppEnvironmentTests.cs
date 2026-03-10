using JoJot.Services;

namespace JoJot.Tests.Services;

public class AppEnvironmentTests
{
    [Fact]
    public void AppDataDirectory_EndsWithExpectedFolderName()
    {
        // Tests run in Debug configuration, so folder should be JoJot.Dev
        AppEnvironment.AppDataDirectory.Should().EndWith("JoJot.Dev");
    }

    [Fact]
    public void DatabasePath_IsInsideAppDataDirectory()
    {
        AppEnvironment.DatabasePath.Should().StartWith(AppEnvironment.AppDataDirectory);
        AppEnvironment.DatabasePath.Should().EndWith("jojot.db");
    }

    [Fact]
    public void PipeName_ContainsDevSuffix_InDebug()
    {
        AppEnvironment.PipeName.Should().EndWith(".Dev");
    }

    [Fact]
    public void MutexName_ContainsDevSuffix_InDebug()
    {
        AppEnvironment.MutexName.Should().EndWith(".Dev");
    }

    [Fact]
    public void IsDebug_True_InTestConfiguration()
    {
        AppEnvironment.IsDebug.Should().BeTrue();
    }
}
