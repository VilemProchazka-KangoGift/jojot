---
phase: quick-2
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Services/AppEnvironment.cs
  - JoJot/App.xaml.cs
  - JoJot/Services/IpcService.cs
autonomous: true
requirements: [QUICK-2]
must_haves:
  truths:
    - "Debug builds use a separate database from production installed builds"
    - "Debug and production instances can run simultaneously without mutex conflict"
    - "Debug and production instances use separate IPC pipes"
    - "Debug logs are written to a separate directory from production logs"
  artifacts:
    - path: "JoJot/Services/AppEnvironment.cs"
      provides: "Centralized debug/release environment detection and path resolution"
      exports: ["AppDataDirectory", "DatabasePath", "MutexName", "PipeName", "IsDebug"]
    - path: "JoJot/App.xaml.cs"
      provides: "Uses AppEnvironment for appDir and dbPath"
    - path: "JoJot/Services/IpcService.cs"
      provides: "Uses AppEnvironment for pipe name and mutex name"
  key_links:
    - from: "JoJot/App.xaml.cs"
      to: "JoJot/Services/AppEnvironment.cs"
      via: "AppEnvironment.AppDataDirectory and AppEnvironment.DatabasePath"
      pattern: "AppEnvironment\\.(AppDataDirectory|DatabasePath)"
    - from: "JoJot/Services/IpcService.cs"
      to: "JoJot/Services/AppEnvironment.cs"
      via: "AppEnvironment.PipeName and AppEnvironment.MutexName"
      pattern: "AppEnvironment\\.(PipeName|MutexName)"
---

<objective>
Separate debug and production data storage so that running JoJot via `dotnet run` during development does not read/write the same SQLite database, log files, IPC pipe, or single-instance mutex as the installed production app.

Purpose: Prevent data corruption and interference between debug sessions and the user's real notes. Currently both debug and production share `%LocalAppData%\JoJot\jojot.db`, the `JoJot_IPC` pipe, and the `Global\JoJot_SingleInstance` mutex -- meaning you cannot even run debug while the installed app is open.

Output: Debug builds use `%LocalAppData%\JoJot.Dev\` with suffixed IPC/mutex names; release builds are unchanged.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@CLAUDE.md

<interfaces>
<!-- From JoJot/App.xaml.cs (lines 73-76, 110): -->
```csharp
// Current hardcoded path construction:
var appDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "JoJot");
// ...
var dbPath = Path.Combine(appDir, "jojot.db");
```

<!-- From JoJot/Services/IpcService.cs (lines 17-20): -->
```csharp
public const string PipeName = "JoJot_IPC";
public const string MutexName = "Global\\JoJot_SingleInstance";
```

<!-- From JoJot/Services/LogService.cs (line 23): -->
```csharp
public static void Initialize(string directory)
// Called with appDir -- log path is derived from appDir
```

<!-- From JoJot/Services/DatabaseCore.cs (line 55): -->
```csharp
public static async Task OpenAsync(string dbPath)
// Called with the constructed dbPath
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create AppEnvironment and wire into App.xaml.cs and IpcService</name>
  <files>JoJot/Services/AppEnvironment.cs, JoJot/App.xaml.cs, JoJot/Services/IpcService.cs</files>
  <action>
Create `JoJot/Services/AppEnvironment.cs` as a static class with these members:

```csharp
namespace JoJot.Services;

/// <summary>
/// Provides environment-aware paths and identifiers that differ between
/// Debug and Release builds, preventing debug sessions from sharing
/// the production database, logs, IPC pipe, and single-instance mutex.
/// </summary>
public static class AppEnvironment
{
#if DEBUG
    public const bool IsDebug = true;
    private const string FolderName = "JoJot.Dev";
    private const string Suffix = ".Dev";
#else
    public const bool IsDebug = false;
    private const string FolderName = "JoJot";
    private const string Suffix = "";
#endif

    /// <summary>App data directory: %LocalAppData%\JoJot (release) or %LocalAppData%\JoJot.Dev (debug).</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>Full path to the SQLite database file.</summary>
    public static string DatabasePath { get; } = Path.Combine(AppDataDirectory, "jojot.db");

    /// <summary>Named pipe name for IPC communication.</summary>
    public const string PipeName = "JoJot_IPC" + Suffix;

    /// <summary>Global mutex name for single-instance enforcement.</summary>
    public const string MutexName = "Global\\JoJot_SingleInstance" + Suffix;
}
```

Then update `JoJot/App.xaml.cs` `OnAppStartup`:
- Replace lines 73-76 (`var appDir = Path.Combine(Environment.GetFolderPath(...), "JoJot")`) with `var appDir = AppEnvironment.AppDataDirectory;`
- Replace line 110 (`var dbPath = Path.Combine(appDir, "jojot.db")`) with `var dbPath = AppEnvironment.DatabasePath;`
- Keep `Directory.CreateDirectory(appDir)` and `LogService.Initialize(appDir)` unchanged (they already use the `appDir` variable)

Then update `JoJot/Services/IpcService.cs`:
- Change `public const string PipeName = "JoJot_IPC";` to `public static string PipeName => AppEnvironment.PipeName;`
- Change `public const string MutexName = "Global\\JoJot_SingleInstance";` to `public static string MutexName => AppEnvironment.MutexName;`

Note: PipeName and MutexName on IpcService change from `const` to `static` property (forwarding to AppEnvironment). This is safe because they are only used at runtime, never in attribute arguments or switch cases.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx 2>&1 | tail -5</automated>
  </verify>
  <done>
    - AppEnvironment.cs exists with #if DEBUG conditional compilation
    - Debug build uses "JoJot.Dev" folder name, ".Dev" suffix on pipe/mutex
    - Release build uses "JoJot" folder name, no suffix (identical to current behavior)
    - App.xaml.cs references AppEnvironment instead of hardcoded paths
    - IpcService references AppEnvironment instead of hardcoded constants
    - Solution builds without errors
  </done>
</task>

<task type="auto">
  <name>Task 2: Verify tests still pass and add AppEnvironment test</name>
  <files>JoJot.Tests/Services/AppEnvironmentTests.cs</files>
  <action>
First run the existing test suite to ensure no regressions from the IpcService const-to-property change.

Then create `JoJot.Tests/Services/AppEnvironmentTests.cs` with these tests:

1. `AppDataDirectory_EndsWithExpectedFolderName` -- In debug builds (which is what tests run as), verify `AppEnvironment.AppDataDirectory` ends with `JoJot.Dev`. This confirms the #if DEBUG branch is active.

2. `DatabasePath_IsInsideAppDataDirectory` -- Verify `AppEnvironment.DatabasePath` starts with `AppEnvironment.AppDataDirectory` and ends with `jojot.db`.

3. `PipeName_ContainsDevSuffix_InDebug` -- Verify `AppEnvironment.PipeName` ends with `.Dev` (since tests run in Debug config).

4. `MutexName_ContainsDevSuffix_InDebug` -- Verify `AppEnvironment.MutexName` ends with `.Dev`.

5. `IsDebug_True_InTestConfiguration` -- Verify `AppEnvironment.IsDebug` is `true`.

Use xUnit with AwesomeAssertions (`.Should().Be()`, `.Should().EndWith()`, `.Should().StartWith()`).
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --verbosity minimal 2>&1 | tail -10</automated>
  </verify>
  <done>
    - All 302 existing tests still pass
    - 5 new AppEnvironment tests pass
    - Tests confirm debug configuration uses ".Dev" suffix
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` succeeds
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` -- all tests pass (302 existing + 5 new)
3. Grep for hardcoded "JoJot" folder name or "JoJot_IPC" / "JoJot_SingleInstance" in App.xaml.cs and IpcService.cs confirms they now go through AppEnvironment
</verification>

<success_criteria>
- Debug builds (`dotnet run`) use `%LocalAppData%\JoJot.Dev\jojot.db` with `JoJot_IPC.Dev` pipe and `Global\JoJot_SingleInstance.Dev` mutex
- Release/published builds use `%LocalAppData%\JoJot\jojot.db` with `JoJot_IPC` pipe and `Global\JoJot_SingleInstance` mutex (unchanged from current behavior)
- Debug and production instances can run simultaneously without interfering
- All tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/2-do-not-share-notes-db-between-debug-and-/2-SUMMARY.md`
</output>
